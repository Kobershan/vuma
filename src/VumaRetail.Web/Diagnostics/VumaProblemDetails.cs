using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions;
using VumaRetail.Contracts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Web.Diagnostics;

/// <summary>
/// The one place an exception becomes an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// <c>CONVENTIONS.md</c> §5. Every error is a <c>ProblemDetails</c> carrying a stable
/// machine-readable <c>code</c> and the request's <c>correlationId</c>. Mapping lives here and
/// nowhere else, so that adding a module does not mean adding a <c>try/catch</c> to forty endpoints
/// and getting one of them subtly wrong.
/// </para>
/// <para>
/// The status code comes from <see cref="DomainProblemKind"/> — the domain's own vocabulary — rather
/// than from a table of error-code strings that would have to grow with every module.
/// </para>
/// <para>
/// ASP.NET Core registers an <see cref="IExceptionHandler"/> as a <b>singleton</b>, so the
/// request-scoped correlation id is resolved from the request's own container rather than injected.
/// The container validates lifetimes at startup and refuses to build otherwise, which is how this
/// was found rather than shipped.
/// </para>
/// </remarks>
/// <param name="problemDetails">Writes the response body in the negotiated format.</param>
/// <param name="logger">Where an unhandled failure is recorded.</param>
public sealed class VumaExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<VumaExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // A cancelled request is the caller hanging up, not a fault. Writing a body to a socket that
        // is already gone just produces a second, more confusing exception in the log.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        ProblemDetails problem = exception switch
        {
            ValidationFailedException validation => Validation(validation),
            DomainException domain => Domain(domain),
            InvalidOperationException { Message: "COMPANY_NOT_FOUND" } => Build(StatusCodes.Status404NotFound, "Not found", "The company was not found.", "COMPANY_NOT_FOUND"),
            InvalidOperationException { Message: "COMPANY_NOT_READY" } => Build(StatusCodes.Status422UnprocessableEntity, "Rule violation", "The company is not ready to activate.", "COMPANY_NOT_READY"),
            InvalidOperationException { Message: "COMPANY_SELECTION_REQUIRED" } => Build(StatusCodes.Status400BadRequest, "Bad request", "A valid company selection header is required.", "COMPANY_SELECTION_REQUIRED"),
            InvalidOperationException { Message: "COMPANY_DUPLICATE" } => Build(StatusCodes.Status409Conflict, "Conflict", "A company with those identifying details already exists.", "COMPANY_DUPLICATE"),
            InvalidOperationException { Message: "LINK_STATUS_UNKNOWN" } => Build(StatusCodes.Status400BadRequest, "Bad request", "Unknown company link status.", "LINK_STATUS_UNKNOWN"),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation } postgres } => DbCheckViolation(postgres),
            PostgresException { SqlState: PostgresErrorCodes.CheckViolation } postgres => DbCheckViolation(postgres),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres } => DbUniqueViolation(postgres),
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres => DbUniqueViolation(postgres),
            BadHttpRequestException malformed => Malformed(malformed),
            _ => Unhandled(exception, httpContext),
        };

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    private static ProblemDetails Validation(ValidationFailedException exception)
    {
        ProblemDetails problem = Build(
            StatusCodes.Status400BadRequest,
            "Validation failed",
            "One or more values in the request are not acceptable.",
            ApiErrorCodes.ValidationFailed);

        problem.Extensions["errors"] = exception.Errors;

        return problem;
    }

    private static ProblemDetails Domain(DomainException exception)
    {
        ProblemDetails problem = exception.Kind switch
        {
            DomainProblemKind.NotFound => Build(
                StatusCodes.Status404NotFound,
                "Not found",
                exception.Message,
                exception.Code),
            DomainProblemKind.Conflict => Build(
                StatusCodes.Status409Conflict,
                "Conflict",
                exception.Message,
                exception.Code),
            DomainProblemKind.Malformed => Build(
                StatusCodes.Status400BadRequest,
                "Bad request",
                exception.Message,
                exception.Code),
            DomainProblemKind.Forbidden => Build(
                StatusCodes.Status403Forbidden,
                "Not permitted",
                exception.Message,
                exception.Code),
            _ => Build(
                StatusCodes.Status422UnprocessableEntity,
                "Rule violation",
                exception.Message,
                exception.Code),
        };

        // Structured fields the caller needs in order to act — the amount due and a working payment
        // link on a read-only refusal, the upgrade reference on an unlicensed module. Merged here,
        // generically, rather than through a table mapping error codes to extra fields: the mapping
        // stays in one place and each module still decides what it has to say (Stage 04b).
        if (exception is IProblemExtensions extended)
        {
            // A named problem type (Stage 06e's company-link-required) travels on the document's
            // type, where a client branches on it, not smuggled inside extensions.
            if (extended.ProblemTypeUrl is not null)
            {
                problem.Type = extended.ProblemTypeUrl;
            }

            foreach ((string name, object? value) in extended.ProblemExtensions)
            {
                problem.Extensions[name] = value;
            }
        }

        return problem;
    }

    /// <summary>
    /// A request that never reached a handler: model binding failed before the endpoint ran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Minimal APIs raise <see cref="BadHttpRequestException"/> when a required non-nullable query or
    /// route parameter is absent or unparseable, when a body cannot be deserialised, and when a body
    /// exceeds the size limit. Without this arm every one of those fell through to
    /// <see cref="Unhandled"/> and the caller got a <c>500 INTERNAL_ERROR</c> — telling them the
    /// server had broken when in fact their request was incomplete and they could have fixed it. It
    /// also logged an error and burned a correlation id per malformed request, which makes a caller's
    /// bad integration look like an outage.
    /// </para>
    /// <para>
    /// The exception's own <see cref="BadHttpRequestException.StatusCode"/> is honoured rather than
    /// hard-coding 400, because the framework already distinguishes these: a body over the limit is a
    /// 413, not a 400, and answering it with 400 would send the caller looking at their JSON rather
    /// than at its size.
    /// </para>
    /// <para>
    /// Only the framework's own message is disclosed, never an inner exception's. The outer message
    /// names the parameter or the reason ("Required parameter ... was not provided from query
    /// string") and is exactly what the caller needs; an inner <c>JsonException</c> can carry a
    /// fragment of the payload and a path into it, which is the caller's data echoed back through a
    /// log and a response body and is not disclosed here.
    /// </para>
    /// </remarks>
    private static ProblemDetails Malformed(BadHttpRequestException exception) => Build(
        exception.StatusCode,
        "Bad request",
        exception.Message,
        ApiErrorCodes.MalformedRequest);

    private ProblemDetails Unhandled(Exception exception, HttpContext httpContext)
    {
        string correlationId = httpContext.RequestServices
            .GetService<ICorrelationContext>()?.CorrelationId ?? httpContext.TraceIdentifier;

        // Logged in full here; disclosed in none. A stack trace or a connection string in a response
        // body is a gift to anybody probing the API, and a shop's own staff cannot act on either.
        logger.LogError(exception, "Unhandled {ExceptionType} on correlation {CorrelationId}",
            exception.GetType().Name,
            correlationId);

        return Build(
            StatusCodes.Status500InternalServerError,
            "Something went wrong",
            "The request could not be completed. Quote the correlation id when reporting this.",
            ApiErrorCodes.InternalError);
    }

    private static ProblemDetails Build(int status, string title, string detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Extensions = { ["code"] = code },
    };

    /// <summary>
    /// A row the database refused on a check constraint (SQL state 23514). The constraint is the
    /// last line of defence behind the domain and its validators, so reaching it means a rule with
    /// no coded refusal yet — answered as one (422), never as a 500.
    /// </summary>
    private static ProblemDetails DbCheckViolation(PostgresException exception) => WithConstraint(
        Build(
            StatusCodes.Status422UnprocessableEntity,
            "Rule violation",
            "A business rule enforced by the database refused the change.",
            "CHECK_VIOLATION"),
        exception);

    /// <summary>
    /// A row the database refused on a unique constraint (SQL state 23505): something that must be
    /// unique already exists. Answered as a conflict (409), never as a 500.
    /// </summary>
    private static ProblemDetails DbUniqueViolation(PostgresException exception) => WithConstraint(
        Build(
            StatusCodes.Status409Conflict,
            "Conflict",
            "Something that must be unique already exists.",
            "UNIQUE_VIOLATION"),
        exception);

    private static ProblemDetails WithConstraint(ProblemDetails problem, PostgresException exception)
    {
        // The constraint name is a stable identifier, not business data: it tells the caller which
        // rule fired without echoing the row that fired it.
        if (exception.ConstraintName is not null)
        {
            problem.Extensions["constraint"] = exception.ConstraintName;
        }

        return problem;
    }
}

/// <summary>Registers <c>ProblemDetails</c>, the exception handler and the status-code pages.</summary>
public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Makes every error response — thrown, or produced by the framework — a <c>ProblemDetails</c>
    /// with a stable code and a correlation id.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            // 401 and 403 never reach the exception handler — the authentication and authorisation
            // middleware short-circuit before it. Without this they would come back with an empty
            // body, and a client written against the error contract would find two endpoints' worth
            // of exceptions to the rule.
            context.ProblemDetails.Extensions.TryAdd("code", CodeFor(context.ProblemDetails.Status));

            ICorrelationContext? correlation = context.HttpContext.RequestServices
                .GetService<ICorrelationContext>();

            if (correlation is not null)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlation.CorrelationId;
            }

            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
        });

        services.AddExceptionHandler<VumaExceptionHandler>();

        return services;
    }

    private static string CodeFor(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => ApiErrorCodes.ValidationFailed,
        StatusCodes.Status401Unauthorized => ApiErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ApiErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ApiErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ApiErrorCodes.Conflict,
        StatusCodes.Status422UnprocessableEntity => ApiErrorCodes.RuleViolation,
        _ => ApiErrorCodes.InternalError,
    };
}

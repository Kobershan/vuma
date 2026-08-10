using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Serilog.Context;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Web.Diagnostics;

/// <summary>
/// Adopts or mints the request's correlation id, echoes it, and puts it on every log line the
/// request produces.
/// </summary>
/// <remarks>
/// <para>
/// Runs before authentication, because a request that fails to authenticate is exactly the sort
/// support gets called about and it needs an id too.
/// </para>
/// <para>
/// The header is echoed on the way out so a terminal can write the same id into its own log. When
/// a till and a store server disagree about what happened, the only cheap way to line up their two
/// log files is a shared id that the till chose.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The header carrying the id, in both directions.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request.</param>
    /// <param name="correlation">The scoped correlation context.</param>
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(correlation);

        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues supplied))
        {
            correlation.Set(supplied.ToString());
        }

        string id = correlation.CorrelationId;

        // Set on OnStarting rather than now: a later middleware may clear the response headers when
        // it replaces the response, and a correlation id that survives only the happy path is not
        // worth having.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}

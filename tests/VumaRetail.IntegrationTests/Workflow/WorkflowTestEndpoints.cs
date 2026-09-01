using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.IntegrationTests.Workflow;

/// <summary>
/// Adds one test-only route, <c>POST /test/workflow/raise</c>, to the running host — nothing in
/// <c>VumaRetail.StoreServer.Program</c> changes.
/// </summary>
/// <remarks>
/// <para>
/// Raising a gated action is deliberately not a public workflow endpoint: <c>IApprovalService</c> is
/// called in-process by the module that owns the action (ADR-019), and Stage 05 builds no business
/// module of its own to call it from. Proving the self-approval rule over real HTTP — as the API test
/// level requires (<c>docs/TESTING.md</c> §1) — needs a caller whose identity comes from a real bearer
/// token, which is exactly what an <see cref="IStartupFilter"/> lets the test project add without
/// touching the host it is testing.
/// </para>
/// <para>
/// Registered only by tests that opt in, via <c>ApiHarness.CreateAsync</c>'s <c>configureServices</c>
/// hook — nothing about the production host's route table changes for anybody else.
/// </para>
/// </remarks>
internal sealed class WorkflowTestEndpointsStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            next(app);

            app.Use(async (context, nextMiddleware) =>
            {
                if (!HttpMethods.IsPost(context.Request.Method)
                    || context.Request.Path != "/test/workflow/raise")
                {
                    await nextMiddleware(context).ConfigureAwait(false);
                    return;
                }

                if (context.User.Identity is not { IsAuthenticated: true })
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                RaiseTestApprovalCommand command = await context.Request
                    .ReadFromJsonAsync<RaiseTestApprovalCommand>()
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Malformed test request body.");

                IDispatcher dispatcher = context.RequestServices.GetRequiredService<IDispatcher>();

                object? outcome = await dispatcher.SendAsync(command).ConfigureAwait(false);

                await context.Response.WriteAsJsonAsync(outcome).ConfigureAwait(false);
            });
        };
}

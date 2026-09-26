using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Apperture.Management.Host.ExceptionHandling;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Browser request-forgery policy for Management.Host. It runs after CORS (so preflight is answered first)
/// and before authentication, so a rejection is always 403 and never reveals whether a session cookie is valid.
/// </summary>
internal sealed class BrowserRequestTrustMiddleware(RequestDelegate next, ILogger<BrowserRequestTrustMiddleware> logger)
{
    public const string MeterName = "apperture.browser_trust";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("apperture.browser_trust.rejected");

    public async Task InvokeAsync(
        HttpContext context,
        BrowserRequestTrustOptions options,
        IConfiguration configuration,
        IOptionsMonitor<OpenIdConnectOptions> oidc)
    {
        var trust = WithCallbacks(options, oidc.Get(OpenIdConnectDefaults.AuthenticationScheme), configuration);
        var facts = Read(context);
        var decision = BrowserRequestTrustEvaluator.Evaluate(facts, trust);
        if (decision.Allowed)
        {
            await next(context);
            return;
        }

        var rule = BrowserRequestTrustEvaluator.RuleId(decision.Rule);
        var reportOnly = trust.ReportOnlyActive;
        Rejected.Add(1, new KeyValuePair<string, object?>("rule", rule), new KeyValuePair<string, object?>("mode", reportOnly ? "report" : "enforce"));
        Log(context, facts, rule, reportOnly, trust);

        if (reportOnly)
        {
            await next(context);
            return;
        }

        await WriteRejectionAsync(context);
    }

    private void Log(HttpContext context, BrowserRequestTrustFacts facts, string rule, bool reportOnly, BrowserRequestTrustOptions trust)
    {
        var endpoint = context.GetEndpoint() as RouteEndpoint;
        logger.Log(
            reportOnly ? LogLevel.Information : LogLevel.Warning,
            "Browser request trust {Disposition}. Rule={Rule} Method={Method} Route={Route} Origin={Origin} TrustedOrigin={TrustedOrigin} SecFetchSite={SecFetchSite} SecFetchMode={SecFetchMode} Authenticated={Authenticated} CorrelationId={CorrelationId}",
            reportOnly ? "report" : "rejected",
            rule,
            facts.Method,
            endpoint?.RoutePattern.RawText ?? "unmatched",
            Bound(facts.Origins),
            trust.TrustedOrigins.Count == 0 ? "(none)" : trust.TrustedOrigins[0].ToString(),
            Bound(facts.FetchSites),
            BoundHeader(context.Request.Headers["Sec-Fetch-Mode"]),
            context.User.Identity?.IsAuthenticated == true,
            ExceptionHandling.CorrelationId.Get(context));
    }

    private static async Task WriteRejectionAsync(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = BrowserRequestTrustOptions.RejectionTitle,
            Type = "https://httpstatuses.com/403",
            Extensions =
            {
                ["code"] = BrowserRequestTrustOptions.RejectionCode,
                ["correlationId"] = ExceptionHandling.CorrelationId.Get(context),
            },
        };

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, cancellationToken: context.RequestAborted);
    }

    private static BrowserRequestTrustOptions WithCallbacks(
        BrowserRequestTrustOptions options,
        OpenIdConnectOptions oidc,
        IConfiguration configuration)
    {
        var paths = new List<string>
        {
            oidc.CallbackPath.HasValue ? oidc.CallbackPath.Value! : "/signin-oidc",
            oidc.SignedOutCallbackPath.HasValue ? oidc.SignedOutCallbackPath.Value! : EntraSignOut.SignedOutCallbackPath,
        };
        if (oidc.RemoteSignOutPath.HasValue)
            paths.Add(oidc.RemoteSignOutPath.Value!);

        return new BrowserRequestTrustOptions
        {
            TrustedOrigins = BrowserRequestTrustExtensions.TrustedOrigins(configuration),
            OidcCallbackPaths = paths,
            ReportOnlyActive = options.ReportOnlyActive,
            ReportOnlyConfiguredButIgnored = options.ReportOnlyConfiguredButIgnored,
        };
    }

    private static BrowserRequestTrustFacts Read(HttpContext context)
    {
        var request = context.Request;
        var endpoint = context.GetEndpoint();
        return new BrowserRequestTrustFacts(
            request.Method,
            request.Path.Value ?? "",
            Values(request.Headers.Origin),
            Values(request.Headers[BrowserRequest.HeaderName]),
            Values(request.Headers["Sec-Fetch-Site"]),
            endpoint?.Metadata.GetMetadata<BrowserRequestTrustExemptAttribute>() is not null,
            endpoint?.Metadata.GetMetadata<NativeNavigationEndpoint>() is not null);
    }

    private static IReadOnlyList<string> Values(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count == 0)
            return [];

        var list = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
            list[i] = values[i] ?? "";
        return list;
    }

    private static string Bound(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "(absent)",
        1 => BoundHeader(values[0]),
        _ => "(ambiguous)",
    };

    private static string BoundHeader(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "(absent)";

        var cleaned = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        return cleaned.Length <= 200 ? cleaned : cleaned[..200];
    }
}

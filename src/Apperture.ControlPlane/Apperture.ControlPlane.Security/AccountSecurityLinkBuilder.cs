using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

/// <summary>Public UI links for account-security emails. Uses the management UI origin, not notification templates.</summary>
public sealed class AccountSecurityLinkBuilder
{
    public const string VerifyEmailPath = "/account/verify-email";
    public const string SetupPasswordPath = "/account/setup-password";
    public const string ResetPasswordPath = "/account/reset-password";

    private const string UiBaseKey = "ShellUi:ManagementUiUrl";
    private const string DevelopmentBaseUrl = "http://localhost:9001";

    private readonly IConfiguration _configuration;

    public AccountSecurityLinkBuilder(IConfiguration configuration) => _configuration = configuration;

    public string VerifyEmail(string token) => Build(VerifyEmailPath, token);

    public string SetupPassword(string token) => Build(SetupPasswordPath, token);

    public string ResetPassword(string token) => Build(ResetPasswordPath, token);

    public string SignInWithMicrosoft()
    {
        var configured = _configuration[UiBaseKey];
        var baseUrl = string.IsNullOrWhiteSpace(configured) ? DevelopmentBaseUrl : configured.TrimEnd('/');
        return $"{baseUrl}/signin";
    }

    public string Build(string relativePath, string token)
    {
        var configured = _configuration[UiBaseKey];
        var baseUrl = string.IsNullOrWhiteSpace(configured) ? DevelopmentBaseUrl : configured.TrimEnd('/');
        return $"{baseUrl}{relativePath}?token={Uri.EscapeDataString(token)}";
    }
}

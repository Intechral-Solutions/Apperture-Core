using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Moq;

namespace Apperture.UnitTests;

/// <summary>
/// The integration authorization helper for controller tests that never reach an integration routing write. Every dependency is
/// an inert mock, so a test that did reach one would fail rather than be allowed through.
/// </summary>
internal static class UnusedIntegrationAuthorization
{
    public static IntegrationSettingsAuthorization Create() => new(
        Mock.Of<IPrincipalContext>(),
        Mock.Of<IAuthorizationEvaluationService>(),
        Mock.Of<IIntegrationRepository>());
}

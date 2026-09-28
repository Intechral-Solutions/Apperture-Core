using System.Net;
using System.Net.Http.Json;
using Apperture.Core.Application;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Resolves a local platform administrator for the shared bootstrap HTTP catalog.
/// First-admin bootstrap is available only while the user table is empty.
/// </summary>
internal static class BootstrapHttpPlatformAdmin
{
    public static async Task<string> EnsureLocalExternalIdAsync(ManagementHostWebApplicationFactory factory)
    {
        var existing = await FindLocalExternalIdAsync(factory).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (await db.Users.AnyAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "No local platform administrator exists, and first-admin bootstrap is closed because platform users already exist.");
            }
        }

        var bootstrap = factory.CreateClient();
        var created = await bootstrap.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest(
                "Bootstrap HTTP Admin",
                "Local",
                $"boot-admin-{Guid.NewGuid():N}"[..16],
                "unique-local-passphrase")).ConfigureAwait(false);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var established = await FindLocalExternalIdAsync(factory).ConfigureAwait(false);
        if (established is null)
        {
            throw new InvalidOperationException(
                "First-admin bootstrap completed without a local platform administrator.");
        }

        return established;
    }

    private static async Task<string?> FindLocalExternalIdAsync(ManagementHostWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var platformAdminRoleId = BootstrapConstants.PlatformAdminRoleId;
        return await db.IdentityLinks
            .AsNoTracking()
            .Where(l => l.Source == (int)IdentitySource.Local)
            .Where(l => db.UserRoles.Any(r => r.UserId == l.UserId && r.RoleId == platformAdminRoleId))
            .OrderBy(l => l.ExternalId)
            .Select(l => l.ExternalId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }
}

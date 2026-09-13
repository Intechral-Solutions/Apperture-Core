using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Persistence;

public sealed class AppertureDbContextModelConsistencyTests
{
    [Fact]
    public void Compiled_model_matches_the_EF_snapshot()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseSqlServer("Server=(local);Database=unused;Trusted_Connection=True;")
            .Options;

        using var db = new AppertureDbContext(options);
        db.Database.HasPendingModelChanges().Should().BeFalse(
            "the EF model snapshot must include tenant-scoped Entra identity uniqueness so a future migration does not recreate the indexes");
    }
}

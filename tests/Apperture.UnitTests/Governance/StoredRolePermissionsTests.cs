using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The corrected reading of the permissions stored on a role (IAM-10 gate P-1): a list that cannot be read is flagged and
/// contributes no grants at all, and nothing is salvaged or thrown. This is an intentional change from the parser the held-set
/// equivalence tests froze (<see cref="LegacyRolePermissionsParser"/>), which threw for a null element and kept a grant with no
/// action. Those tests are about the evaluator and are not affected by this one.
/// </summary>
public sealed class StoredRolePermissionsTests
{
    private const string Read = """{"action":"connection.read","resource":{"resourceType":"Connection","scopeKey":"instance"}}""";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void A_blank_or_empty_value_is_a_readable_role_with_no_grants(string? json)
    {
        var reading = StoredRolePermissions.Read(json);

        reading.IsReadable.Should().BeTrue();
        reading.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void A_readable_list_reads_its_grants_in_order_with_any_property_casing()
    {
        var reading = StoredRolePermissions.Read(
            """[{"Action":"connection.read","Resource":{"ResourceType":"Connection","ScopeKey":"instance"}},{"action":"workflow.execute"}]""");

        reading.IsReadable.Should().BeTrue();
        reading.Permissions.Should().Equal(
            new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "instance")),
            new PermissionDescriptor("workflow.execute"));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"text\"")]
    [InlineData("[1,2]")]
    [InlineData("[null]")]
    [InlineData("[{\"action\":null}]")]
    [InlineData("[{\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":\"instance\"}}]")]
    [InlineData("[{\"action\":\"  \"}]")]
    [InlineData("[{\"action\":\"user.read\",\"resource\":{\"resourceType\":null,\"scopeKey\":\"instance\"}}]")]
    [InlineData("[{\"action\":\"user.read\",\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":null}}]")]
    public void An_unreadable_value_is_flagged_and_contributes_no_grants_and_nothing_throws(string json)
    {
        var reading = StoredRolePermissions.Read(json);

        reading.IsReadable.Should().BeFalse();
        reading.Permissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[" + Read + ",null]")]
    [InlineData("[null," + Read + "]")]
    [InlineData("[" + Read + ",{\"action\":null}]")]
    [InlineData("[" + Read + ",{\"action\":\"user.read\",\"resource\":{\"resourceType\":\"Role\",\"scopeKey\":null}}]")]
    public void The_readable_part_of_a_damaged_list_is_not_salvaged(string json)
    {
        var reading = StoredRolePermissions.Read(json);

        reading.IsReadable.Should().BeFalse();
        reading.Permissions.Should().BeEmpty("a partly trusted list would silently grant less than the role appears to hold");
    }

    [Fact]
    public void A_grant_with_a_blank_but_present_resource_type_or_scope_is_readable_and_inert_as_it_always_was()
    {
        var reading = StoredRolePermissions.Read("""[{"action":"user.read","resource":{"resourceType":"","scopeKey":""}}]""");

        reading.IsReadable.Should().BeTrue();
        reading.Permissions.Should().ContainSingle();
    }

    [Fact]
    public void Serialization_round_trips_through_the_reader()
    {
        var grants = new[]
        {
            new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "instance")),
            new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", "module:X")),
            new PermissionDescriptor("role.manage"),
        };

        var reading = StoredRolePermissions.Read(StoredRolePermissions.Serialize(grants));

        reading.IsReadable.Should().BeTrue();
        reading.Permissions.Should().Equal(grants);
        StoredRolePermissions.Serialize([]).Should().Be("[]");
    }

    [Fact]
    public void The_frozen_parser_of_the_equivalence_tests_still_reads_as_it_did_so_the_two_are_told_apart()
    {
        // The frozen copy throws for a null element; the production reader does not. Both facts are intended.
        var frozen = () => LegacyRolePermissionsParser.Read("[null]");
        frozen.Should().Throw<NullReferenceException>();
        StoredRolePermissions.Read("[null]").IsReadable.Should().BeFalse();
    }
}

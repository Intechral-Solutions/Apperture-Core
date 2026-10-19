using System.Text.Json;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A frozen copy of the role permission parser of <c>SqlRoleRepository</c> as it was at master 1ab3b5f. The differential
/// equivalence tests of the held-set refactor (A10.2.1) feed the original evaluator and the refactored one the roles this parser
/// produced, including its defects: an unreadable list reads as no grants, but a null element makes it throw, and an element
/// with a null action becomes a grant that matches nothing.
/// </summary>
/// <remarks>
/// The production parser has since been corrected on purpose (<c>StoredRolePermissions</c>): an unreadable list, or one with an
/// unreadable element, is a role flagged as unreadable that contributes no grants, and nothing is salvaged or thrown. That change
/// is tested in <c>StoredRolePermissionsTests</c> and is deliberately kept out of the equivalence tests, which are about the
/// evaluator and must not change when the repository does. Do not edit this copy.
/// </remarks>
internal static class LegacyRolePermissionsParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<PermissionDescriptor> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return Array.Empty<PermissionDescriptor>();

        List<PermissionDescriptorDto>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<PermissionDescriptorDto>>(json, JsonOptions);
        }
        catch
        {
            return Array.Empty<PermissionDescriptor>();
        }

        if (list is null)
            return Array.Empty<PermissionDescriptor>();

        return list.Select(dto => new PermissionDescriptor(
            dto.Action,
            dto.Resource is null ? null : new ResourceDescriptor(dto.Resource.ResourceType, dto.Resource.ScopeKey))).ToList();
    }

    private sealed class PermissionDescriptorDto
    {
        public string Action { get; set; } = "";
        public ResourceDescriptorDto? Resource { get; set; }
    }

    private sealed class ResourceDescriptorDto
    {
        public string ResourceType { get; set; } = "";
        public string ScopeKey { get; set; } = "";
    }
}

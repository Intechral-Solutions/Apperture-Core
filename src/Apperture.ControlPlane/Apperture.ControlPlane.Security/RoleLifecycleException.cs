namespace Apperture.ControlPlane.Security;

/// <summary>
/// Expected role-lifecycle refusal with a stable client-facing code.
/// </summary>
public sealed class RoleLifecycleException : Exception
{
    public RoleLifecycleException(string code, string message, int? assignmentCount = null) : base(message)
    {
        Code = code;
        AssignmentCount = assignmentCount;
    }

    public string Code { get; }

    public int? AssignmentCount { get; }
}

/// <summary>Stable role-lifecycle error codes for APIs and clients.</summary>
public static class RoleLifecycleErrorCodes
{
    public const string SystemRole = "system_role";
    public const string AssignmentsRemain = "assignments_remain";
    public const string DuplicateName = "duplicate_name";
}

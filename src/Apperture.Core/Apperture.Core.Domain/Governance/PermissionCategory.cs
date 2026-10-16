namespace Apperture.Core.Domain.Governance;

/// <summary>
/// The closed set of groups a permission is presented under. There is deliberately no zero value, so an unset field is invalid.
/// </summary>
public enum PermissionCategory
{
    /// <summary>Users, roles, role assignment and credentials.</summary>
    AccessControl = 1,

    /// <summary>Sign-in configuration and Entra registrations.</summary>
    Authentication = 2,

    /// <summary>Integrations, connections, connectors, bindings, capabilities and mappings.</summary>
    Integrations = 3,

    /// <summary>Installing and configuring modules, and the permissions modules declare.</summary>
    Modules = 4,

    /// <summary>Running and recovering work: workflows, operations, schedules, teams and recovery.</summary>
    Operations = 5,

    /// <summary>Environments, runtimes and runtime health.</summary>
    Runtime = 6,

    /// <summary>Contacts and notification definitions.</summary>
    Notifications = 7,

    /// <summary>Secrets, audit and trust.</summary>
    Security = 8,

    /// <summary>Instance-wide settings and contexts.</summary>
    Platform = 9,
}

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Intentional failure classification for operation items and procedure outcomes (SDK-6A).
/// Used for retry eligibility, operator diagnostics, and platform events — not an exhaustive taxonomy.
/// </summary>
public enum OperationFailureCategory
{
    /// <summary>Input or record failed validation rules.</summary>
    Validation = 0,

    /// <summary>Integration or module configuration is missing or invalid.</summary>
    Configuration = 1,

    /// <summary>Authentication or authorization to an external system failed.</summary>
    Authentication = 2,

    /// <summary>Transient external dependency failure; may be retryable.</summary>
    TransientExternal = 3,

    /// <summary>Permanent external dependency failure; typically not retryable without data fix.</summary>
    PermanentExternal = 4,

    /// <summary>Malformed or unreadable input payload.</summary>
    MalformedInput = 5,

    /// <summary>Input type or shape is not supported by this procedure.</summary>
    UnsupportedInput = 6,

    /// <summary>Unexpected platform or infrastructure failure.</summary>
    Infrastructure = 7,

    /// <summary>Execution was cancelled.</summary>
    Cancellation = 8,

    /// <summary>Business rule skip — not a failure; item may be Skipped.</summary>
    SkippedByPolicy = 9,
}

namespace Apperture.Core.Application.PlatformSettings;

public enum PlatformSettingInputKind
{
    Toggle,
    Number,
    Text,
    Duration,
    Select,
    MultiSelect,
    /// <summary>Password/secret editor; persisted value is a secret reference, not plaintext.</summary>
    Secret
}

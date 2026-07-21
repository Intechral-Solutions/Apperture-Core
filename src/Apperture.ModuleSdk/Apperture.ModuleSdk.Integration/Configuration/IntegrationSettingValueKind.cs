namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>Typed value kinds for integration-instance setting declarations.</summary>
public enum IntegrationSettingValueKind
{
    String = 0,
    MultilineString = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    Choice = 5,
    Uri = 6,
    Duration = 7,
    TimeZone = 8,
    SensitiveText = 9,
}

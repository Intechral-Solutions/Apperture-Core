using Apperture.Core.Application.PlatformSettings;

namespace Apperture.UnitTests.PlatformSettings;

internal sealed class TestIntegrationOptionsOptionProvider : IPlatformSettingOptionProvider
{
    public const string ProviderKeyConst = "test.integration.options";

    public string ProviderKey => ProviderKeyConst;

    public Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsAsync(
        PlatformSettingDefinition definition,
        PlatformSettingOptionProviderContext context,
        CancellationToken cancellationToken = default)
    {
        _ = definition;

        if (context.ContextKind != PlatformSettingOptionProviderContextKind.Integration
            || string.IsNullOrWhiteSpace(context.IntegrationId))
        {
            throw new ArgumentException("Integration context is required.");
        }

        var integrationId = context.IntegrationId;
        IReadOnlyList<PlatformSettingAllowedValue> options =
        [
            new PlatformSettingAllowedValue
            {
                Value = $"{integrationId}:alpha",
                Label = $"Alpha ({integrationId})"
            },
            new PlatformSettingAllowedValue
            {
                Value = $"{integrationId}:beta",
                Label = $"Beta ({integrationId})"
            }
        ];

        return Task.FromResult(options);
    }
}

namespace Apperture.Core.Application.PlatformSettings;

public static class PlatformSettingsUpdateParser
{
    public static object Parse(PlatformSettingDefinition definition, PlatformSettingUpdatePayload payload)
    {
        var hasScalar = !string.IsNullOrWhiteSpace(payload.ScalarValue);
        var hasArray = payload.ArrayValues is not null;

        if (definition.ValueType == PlatformSettingValueType.StringArray)
        {
            if (hasScalar)
                throw new ArgumentException("StringArray settings require the values array payload.", nameof(payload));

            if (!hasArray)
                throw new ArgumentException("values is required for StringArray settings.", nameof(payload));

            return PlatformSettingsValueCodec.ParseFromArrayInput(definition, payload.ArrayValues);
        }

        if (hasArray)
            throw new ArgumentException("values is only supported for StringArray settings.", nameof(payload));

        if (!hasScalar)
            throw new ArgumentException("value is required.", nameof(payload));

        return PlatformSettingsValueCodec.ParseFromInput(definition, payload.ScalarValue!);
    }
}

using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public sealed class OperationDefinitionQueryService : IOperationDefinitionQueryService
{
    private readonly IOperationRegistryService _operations;
    private readonly IOperationDefinitionReadAuthorizationService _readAuthz;
    private readonly IOperationStartAuthorizationService _startAuthz;

    public OperationDefinitionQueryService(
        IOperationRegistryService operations,
        IOperationDefinitionReadAuthorizationService readAuthz,
        IOperationStartAuthorizationService startAuthz)
    {
        _operations = operations;
        _readAuthz = readAuthz;
        _startAuthz = startAuthz;
    }

    public async ValueTask<IReadOnlyList<OperationDefinitionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await _operations.ListAsync(cancellationToken);
        var result = new List<OperationDefinitionDto>(all.Count);
        foreach (var definition in all)
        {
            try
            {
                await _readAuthz.EnsureCanReadDefinitionAsync(definition.Id, cancellationToken);
                result.Add(await ToDtoAsync(definition, cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }

        return result;
    }

    public async ValueTask<OperationDefinitionDto?> GetAsync(OperationId operationDefinitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _operations.GetOperationAsync(operationDefinitionId, cancellationToken);
        if (definition is null)
            return null;

        await _readAuthz.EnsureCanReadDefinitionAsync(operationDefinitionId, cancellationToken);
        return await ToDtoAsync(definition, cancellationToken);
    }

    private async ValueTask<OperationDefinitionDto> ToDtoAsync(OperationDefinition definition, CancellationToken cancellationToken)
    {
        var canStart = true;
        try
        {
            await _startAuthz.EnsureCanStartOperationAsync(definition.Id, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            canStart = false;
        }

        return new OperationDefinitionDto(
            OperationDefinitionId: definition.Id.Value,
            ModuleId: definition.ModuleId,
            InvariantName: definition.Name,
            DisplayName: definition.Name,
            Description: null,
            Category: definition.Category.ToString(),
            CanStart: canStart,
            InputSchema: ParseSchema(definition.ParameterSchema));
    }

    private static OperationInputSchema? ParseSchema(string? parameterSchema)
    {
        if (string.IsNullOrWhiteSpace(parameterSchema))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(parameterSchema);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
                return null;

            var fields = new List<OperationInputFieldDto>();
            var order = 0;
            foreach (var item in fieldsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var key = item.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                var label = item.TryGetProperty("label", out var labelEl) ? labelEl.GetString() : null;
                var helpText = item.TryGetProperty("helpText", out var helpEl) ? helpEl.GetString() : null;
                var required = item.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True;
                var defaultValue = item.TryGetProperty("defaultValue", out var defEl) ? defEl.ToString() : null;
                var parsedOrder = item.TryGetProperty("order", out var orderEl) && orderEl.TryGetInt32(out var o) ? o : order++;
                decimal? min = item.TryGetProperty("min", out var minEl) && minEl.TryGetDecimal(out var minVal) ? minVal : null;
                decimal? max = item.TryGetProperty("max", out var maxEl) && maxEl.TryGetDecimal(out var maxVal) ? maxVal : null;
                int? maxLength = item.TryGetProperty("maxLength", out var lenEl) && lenEl.TryGetInt32(out var l) ? l : null;

                IReadOnlyList<string>? allowedValues = null;
                if (item.TryGetProperty("allowedValues", out var valuesEl) && valuesEl.ValueKind == JsonValueKind.Array)
                {
                    allowedValues = valuesEl.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString()!)
                        .ToList();
                }

                fields.Add(new OperationInputFieldDto(
                    key,
                    string.IsNullOrWhiteSpace(type) ? "string" : type!,
                    string.IsNullOrWhiteSpace(label) ? key : label!,
                    helpText,
                    required,
                    defaultValue,
                    parsedOrder,
                    allowedValues,
                    min,
                    max,
                    maxLength));
            }

            if (fields.Count == 0)
                return null;

            return new OperationInputSchema(fields.OrderBy(f => f.Order).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}


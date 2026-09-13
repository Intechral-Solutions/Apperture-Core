using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Binds <see cref="IOperationItemStore"/> to the current step emit context (run/module/integration).
/// </summary>
public sealed class ContextBoundOperationItemWriter : IOperationItemWriter
{
    private readonly IOperationItemStore _store;
    private readonly IModulePlatformEmitContext _context;

    public ContextBoundOperationItemWriter(IOperationItemStore store, IModulePlatformEmitContext context)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async ValueTask<OperationGroup> BeginGroupAsync(
        string operationKind,
        string? sourceLabel = null,
        string? summaryJson = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        OperationItemizationValidation.EnsureRunId(_context.RunId);

        var request = new BeginOperationGroupRequest(
            RunId: _context.RunId!,
            ModuleId: _context.ModuleId,
            IntegrationId: _context.IntegrationId,
            ProcedureId: _context.ProcedureId,
            OperationKind: operationKind,
            SourceLabel: sourceLabel,
            SummaryJson: summaryJson,
            MetadataJson: metadataJson);

        return await _store.BeginGroupAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationItem> AppendItemAsync(
        AppendOperationItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationItemizationValidation.EnsureRunId(_context.RunId);

        if (!string.Equals(request.RunId, _context.RunId, StringComparison.Ordinal))
        {
            throw new OperationItemizationException(
                $"AppendItem RunId '{request.RunId}' does not match execution RunId '{_context.RunId}'.");
        }

        return await _store.AppendItemAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<OperationGroup> CompleteGroupAsync(
        CompleteOperationGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationItemizationValidation.EnsureRunId(_context.RunId);
        return new ValueTask<OperationGroup>(_store.CompleteGroupAsync(request, cancellationToken));
    }
}

/// <summary>Fail-loud writer when no durable store is registered.</summary>
public sealed class NullOperationItemWriter : IOperationItemWriter
{
    public static NullOperationItemWriter Instance { get; } = new();

    private NullOperationItemWriter() { }

    public ValueTask<OperationGroup> BeginGroupAsync(
        string operationKind,
        string? sourceLabel = null,
        string? summaryJson = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public ValueTask<OperationItem> AppendItemAsync(
        AppendOperationItemRequest request,
        CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public ValueTask<OperationGroup> CompleteGroupAsync(
        CompleteOperationGroupRequest request,
        CancellationToken cancellationToken = default) =>
        throw Unavailable();

    private static OperationItemizationException Unavailable() =>
        new("Operation itemization store is not registered. Register IOperationItemStore (e.g. AddOperationItemizationPersistence) on the Worker Host.");
}

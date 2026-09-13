namespace Unisa.CampusNexus.PaymentPlans.Persistence;

/// <summary>
/// High-water mark for SFTP/file imports (replaces per-machine INI).
/// </summary>
public sealed class UnisaFileImportWatermarkEntity
{
    /// <summary>e.g. EpromInbound, TransInbound.</summary>
    public string SourceKey { get; set; } = "";

    public DateTimeOffset LastWriteTimeUtc { get; set; }
}

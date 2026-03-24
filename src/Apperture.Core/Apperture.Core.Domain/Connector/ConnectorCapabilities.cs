namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Optional capability markers for a connector definition. Does not enable runtime behavior by itself.
/// </summary>
[Flags]
public enum ConnectorCapabilities
{
    None = 0,

    /// <summary>
    /// Reserved for a future brokered/gateway access pattern; ordinary connectors use direct module consumption.
    /// </summary>
    BrokeredAccessCandidate = 1,
}

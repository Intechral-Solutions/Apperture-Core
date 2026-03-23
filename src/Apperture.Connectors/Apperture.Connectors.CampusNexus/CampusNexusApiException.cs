namespace Apperture.Connectors.CampusNexus;

public sealed class CampusNexusApiException : Exception
{
    public CampusNexusApiException(string message) : base(message) { }

    public CampusNexusApiException(string message, Exception inner) : base(message, inner) { }
}

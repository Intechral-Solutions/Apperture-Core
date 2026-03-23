namespace Unisa.CampusNexus.PaymentPlans;

public static class UnisaCampusNexusPaymentPlansConstants
{
    public const string ModuleId = "Unisa.CampusNexus.PaymentPlans";

    /// <summary>Campus Nexus API client (<see cref="Apperture.Connectors.CampusNexus.ICampusNexusApiClient"/>).</summary>
    public const string SlotCnsApi = "CnsApi";

    /// <summary>SFTP file client (<see cref="Apperture.Connectors.Sftp.ISftpFileClient"/>).</summary>
    public const string SlotSftp = "Sftp";

    /// <summary>SQL connection for UNISA payment plan mapping tables (<see cref="System.Data.IDbConnection"/>).</summary>
    public const string SlotIntegrationDb = "IntegrationDb";
}

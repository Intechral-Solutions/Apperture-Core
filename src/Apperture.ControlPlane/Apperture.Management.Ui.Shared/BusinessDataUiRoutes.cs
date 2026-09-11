namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Transitional Business Data routes. Customers remain a demo/sandbox projection —
/// not the final collection architecture.
/// </summary>
public static class BusinessDataUiRoutes
{
    public const string Root = "/business-data";
    public const string Customers = "/business-data/customers";

    public static string CustomerDetail(string businessKey) =>
        $"{Customers}/{Uri.EscapeDataString(businessKey.Trim())}";

    public static string CustomerActivity(string businessKey) =>
        $"{CustomerDetail(businessKey)}/activity";
}

namespace Apperture.Management.Ui.Shared;

/// <summary>BDS-5 — demo-scoped customer business-record UI routes (not final Records navigation).</summary>
public static class DemoCustomerUiRoutes
{
    public const string List = "/demo/customers";

    public static string Detail(string businessKey) =>
        $"{List}/{Uri.EscapeDataString(businessKey.Trim())}";

    /// <summary>BDS-6 — cross-run activity for a demo customer.</summary>
    public static string Activity(string businessKey) =>
        $"{Detail(businessKey)}/activity";
}

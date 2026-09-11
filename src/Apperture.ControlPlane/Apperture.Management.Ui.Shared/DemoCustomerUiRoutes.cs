namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Canonical Business Data customer routes, plus legacy /demo/customers aliases.
/// </summary>
public static class DemoCustomerUiRoutes
{
    public const string List = BusinessDataUiRoutes.Customers;
    public const string LegacyList = "/demo/customers";

    public static string Detail(string businessKey) =>
        BusinessDataUiRoutes.CustomerDetail(businessKey);

    public static string Activity(string businessKey) =>
        BusinessDataUiRoutes.CustomerActivity(businessKey);

    public static string LegacyDetail(string businessKey) =>
        $"{LegacyList}/{Uri.EscapeDataString(businessKey.Trim())}";

    public static string LegacyActivity(string businessKey) =>
        $"{LegacyDetail(businessKey)}/activity";
}

using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// UIX-4 — Demo Customer typed context: <c>from=demo-customer</c>, <c>fromKey</c>=business key.
/// </summary>
public sealed class DemoCustomerNavigationContextDefinition : IManagementNavigationContextDefinition
{
    public string Type => ManagementNavigationReturnContext.DemoCustomerType;

    public bool TryResolve(string key, out ManagementResolvedNavigationContext context)
    {
        context = default!;
        if (!AffectedRecordNavigation.IsSafeContextKey(key))
            return false;

        var trimmed = key.Trim();
        var detailHref = DemoCustomerUiRoutes.Detail(trimmed);
        IReadOnlyList<BreadcrumbItem> prefix =
        [
            new BreadcrumbItem("Customers", DemoCustomerUiRoutes.List),
            new BreadcrumbItem(trimmed, detailHref),
        ];

        context = new ManagementResolvedNavigationContext(
            prefix,
            detailHref,
            $"Back to customer {trimmed}");
        return true;
    }
}

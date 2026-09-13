namespace Apperture.Core.Application.Notifications;

public interface INotificationTemplateVariableCatalog
{
    IReadOnlySet<string> GetAllowlist(string eventType);

    IReadOnlySet<string> GetUnionAllowlist(IReadOnlyList<string> eventTypes);
}

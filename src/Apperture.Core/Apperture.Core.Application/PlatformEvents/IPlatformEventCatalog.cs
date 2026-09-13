namespace Apperture.Core.Application.PlatformEvents;

public interface IPlatformEventCatalog
{
    bool IsKnownType(string eventType);

    PlatformEventTypeDescriptor GetRequired(string eventType);

    IReadOnlyList<PlatformEventTypeDescriptor> GetAll();
}

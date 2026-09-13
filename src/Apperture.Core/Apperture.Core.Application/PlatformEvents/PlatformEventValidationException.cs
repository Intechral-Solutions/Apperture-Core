namespace Apperture.Core.Application.PlatformEvents;

public sealed class PlatformEventValidationException : Exception
{
    public PlatformEventValidationException(string message) : base(message) { }

    public PlatformEventValidationException(string message, Exception inner) : base(message, inner) { }
}

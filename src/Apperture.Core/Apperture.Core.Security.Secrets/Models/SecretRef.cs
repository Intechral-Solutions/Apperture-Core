namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Opaque secret reference that can be passed across contracts without exposing secret material.
/// </summary>
public sealed record SecretRef(string Value, string? Version = null);

namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Opaque secret reference that can be passed across contracts without exposing secret material.
/// Resolution contract: Value = GUID string → SQL-backed store; Value = kv:-prefixed or Key Vault URI → future Key Vault resolver.
/// </summary>
public sealed record SecretRef(string Value, string? Version = null);

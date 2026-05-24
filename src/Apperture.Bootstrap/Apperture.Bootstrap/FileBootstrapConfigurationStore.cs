using System.Text.Json;
using Apperture.Bootstrap.Models;
using Microsoft.Extensions.Options;

namespace Apperture.Bootstrap;

/// <summary>
/// File-backed <see cref="IBootstrapConfigurationStore"/> for non-secret bootstrap state.
/// </summary>
public sealed class FileBootstrapConfigurationStore : IBootstrapConfigurationStore
{
    private readonly BootstrapConfigurationStoreOptions _options;
    private readonly string? _explicitConfigurationFilePath;

    public FileBootstrapConfigurationStore(IOptions<BootstrapConfigurationStoreOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>For explicit path (tests or advanced wiring).</summary>
    public FileBootstrapConfigurationStore(string configurationFilePath)
    {
        _explicitConfigurationFilePath = Path.GetFullPath(configurationFilePath);
        _options = new BootstrapConfigurationStoreOptions();
    }

    public async Task<BootstrapConfigurationDocument?> GetAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BootstrapConfigurationPersistenceException(
                $"Failed to read bootstrap configuration from '{path}'. Check file permissions.",
                ex) { FilePath = path };
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BootstrapConfigurationDocument>(json, BootstrapConfigurationJsonSerializer.Options);
        }
        catch (JsonException ex)
        {
            throw new BootstrapConfigurationPersistenceException(
                $"Bootstrap configuration file '{path}' contains invalid JSON. Fix or remove the file and re-run bootstrap CLI. Details: {ex.Message}",
                ex) { FilePath = path };
        }
    }

    public async Task SaveAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Profile);

        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var toSave = string.IsNullOrWhiteSpace(document.Version)
            ? document with { Version = Guid.NewGuid().ToString("N") }
            : document;

        var json = JsonSerializer.Serialize(toSave, BootstrapConfigurationJsonSerializer.Options);
        EnsureNoPlaintextSecretsInJson(json, path);

        var tempPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(tempPath);
            throw new BootstrapConfigurationPersistenceException(
                $"Failed to write bootstrap configuration to '{path}'. Check directory permissions.",
                ex) { FilePath = path };
        }
    }

    private string ResolvePath() =>
        _explicitConfigurationFilePath ?? BootstrapConfigurationStorePathResolver.Resolve(_options);

    private static void EnsureNoPlaintextSecretsInJson(string json, string path)
    {
        if (json.Contains("\"Password\"", StringComparison.Ordinal) ||
            json.Contains("\"User\"", StringComparison.Ordinal))
        {
            throw new BootstrapConfigurationPersistenceException(
                $"Bootstrap configuration for '{path}' must not contain SqlServer User/Password fields. Use CredentialReference only.")
            {
                FilePath = path
            };
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of temp file.
        }
    }
}

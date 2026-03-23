using System.Text;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Renci.SshNet;

namespace Apperture.Connectors.Sftp;

/// <summary>
/// Builds <see cref="ISftpFileClient"/> (SSH.NET). Material keys:
/// <list type="bullet">
/// <item><description><c>Host</c>, <c>Port</c> (optional, default 22), <c>Username</c> — required.</description></item>
/// <item><description><c>PrivateKeyPem</c> — PEM-encoded RSA/ED25519 private key; used with optional <c>PrivateKeyPassphrase</c>.</description></item>
/// <item><description><c>Password</c> — password authentication if no private key.</description></item>
/// </list>
/// </summary>
public sealed class SftpConnectorClientFactory : IConnectorClientFactory<ISftpFileClient>
{
    private const string HostKey = "Host";
    private const string PortKey = "Port";
    private const string UsernameKey = "Username";
    private const string PrivateKeyPemKey = "PrivateKeyPem";
    private const string PrivateKeyPassphraseKey = "PrivateKeyPassphrase";
    private const string PasswordKey = "Password";

    public ISftpFileClient Create(Connection connection, ResolvedConnectionMaterial material)
    {
        var dict = material.ConfigAndResolvedSecrets;
        string host = GetRequired(dict, HostKey);
        int port = dict.TryGetValue(PortKey, out var portStr) && int.TryParse(portStr, out var p) ? p : 22;
        string username = GetRequired(dict, UsernameKey);

        ConnectionInfo connectionInfo;
        if (dict.TryGetValue(PrivateKeyPemKey, out var pem) && !string.IsNullOrWhiteSpace(pem))
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pem));
            var keyFile = new PrivateKeyFile(ms, dict.TryGetValue(PrivateKeyPassphraseKey, out var pp) ? pp : null);
            connectionInfo = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keyFile));
        }
        else if (dict.TryGetValue(PasswordKey, out var password) && password is not null)
        {
            connectionInfo = new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password));
        }
        else
            throw new InvalidOperationException("SFTP connection requires PrivateKeyPem or Password in resolved material.");

        var client = new SftpClient(connectionInfo);
        client.Connect();
        return new SshNetSftpFileClient(client);
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"Connection material missing required key '{key}'.");
        return v.Trim();
    }
}

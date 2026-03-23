using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Apperture.Connectors.Sftp;

internal sealed class SshNetSftpFileClient : ISftpFileClient
{
    private readonly SftpClient _client;
    private bool _disposed;

    public SshNetSftpFileClient(SftpClient client) => _client = client;

    public bool IsConnected => _client.IsConnected;

    public IEnumerable<ISftpFile> ListDirectory(string path) => _client.ListDirectory(path);

    public void DownloadFile(string remotePath, Stream output) => _client.DownloadFile(remotePath, output);

    public void RenameFile(string oldPath, string newPath) => _client.RenameFile(oldPath, newPath);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _client.Dispose();
    }
}

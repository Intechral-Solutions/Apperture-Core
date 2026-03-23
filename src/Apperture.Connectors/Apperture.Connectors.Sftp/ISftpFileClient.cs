using Renci.SshNet.Sftp;

namespace Apperture.Connectors.Sftp;

/// <summary>
/// SFTP session for listing, downloading, and renaming remote files. Dispose to disconnect.
/// </summary>
public interface ISftpFileClient : IDisposable
{
    bool IsConnected { get; }

    IEnumerable<ISftpFile> ListDirectory(string path);

    void DownloadFile(string remotePath, Stream output);

    void RenameFile(string oldPath, string newPath);
}

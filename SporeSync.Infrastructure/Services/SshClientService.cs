using Microsoft.Extensions.Options;
using Renci.SshNet;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using SporeSync.Infrastructure.Configuration;
using Directory = System.IO.Directory;

namespace SporeSync.Infrastructure.Services;

public class SshClientService : ISshService, IDisposable
{
    private readonly SshClient _sshClient;
    private readonly SftpClient _sftpClient;
    private readonly SettingsOptions _config;
    private readonly object _lock = new object();
    private bool _disposed = false;

    public SshClientService(IOptions<SettingsOptions> config)
    {
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));

        // Create SSH client
        _sshClient = CreateSshClient(_config.SshConfiguration);

        // Create SFTP client using the same connection info
        _sftpClient = new SftpClient(_sshClient.ConnectionInfo);

        // Connect both clients
        lock (_lock)
        {
            try
            {
                _sshClient.Connect();
                _sftpClient.Connect();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to connect to SSH server", ex);
            }
        }
    }



    public SshClientOptions GetConnectionState()
    {
        return _config.SshConfiguration;
    }

    public async Task<bool> UploadFileAsync(string localPath, string remotePath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(localPath))
                    throw new FileNotFoundException($"Local file not found: {localPath}");

                var fileInfo = new FileInfo(localPath);
                var uploadProgress = new UploadProgress
                {
                    FileName = Path.GetFileName(localPath),
                    TotalBytes = fileInfo.Length,
                    BytesUploaded = 0,
                    Timestamp = DateTime.UtcNow
                };

                using var fileStream = File.OpenRead(localPath);
                using var remoteStream = _sftpClient.Create(remotePath);

                var buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    remoteStream.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    uploadProgress.BytesUploaded = totalBytesRead;
                    progress?.Report(uploadProgress);

                    cancellationToken.ThrowIfCancellationRequested();
                }

                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DownloadFileAsync(string remotePath, string localPath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!_sftpClient.Exists(remotePath))
                    throw new FileNotFoundException($"Remote file not found: {remotePath}");

                var fileSize = _sftpClient.GetAttributes(remotePath).Size;
                var downloadProgress = new UploadProgress
                {
                    FileName = Path.GetFileName(remotePath),
                    TotalBytes = fileSize,
                    BytesUploaded = 0,
                    Timestamp = DateTime.UtcNow
                };

                using var remoteStream = _sftpClient.OpenRead(remotePath);
                using var localStream = File.Create(localPath);

                var buffer = new byte[8192]; // Fixed: Use reasonable buffer size
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = remoteStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    localStream.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    downloadProgress.BytesUploaded = totalBytesRead;
                    progress?.Report(downloadProgress);

                    cancellationToken.ThrowIfCancellationRequested();
                }

                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DownloadDirectoryAsync(string remotePath, string localPath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!_sftpClient.Exists(remotePath))
                    throw new DirectoryNotFoundException($"Remote directory not found: {remotePath}");

                var attributes = _sftpClient.GetAttributes(remotePath);
                if (!attributes.IsDirectory)
                    throw new InvalidOperationException($"Remote path is not a directory: {remotePath}");
            }

            // Create local directory if it doesn't exist
            if (!Directory.Exists(localPath))
                Directory.CreateDirectory(localPath);

            var files = await ListFilesAsync(remotePath);
            var totalFiles = files.Count();
            var processedFiles = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remoteFilePath = file.Path;
                var localFilePath = Path.Combine(localPath, file.Name);

                if (file.IsDirectory)
                {
                    // Recursively download subdirectory
                    await DownloadDirectoryAsync(remoteFilePath, localFilePath, progress, cancellationToken);
                }
                else
                {
                    // Download file
                    await DownloadFileAsync(remoteFilePath, localFilePath, progress, cancellationToken);
                }

                processedFiles++;

                // Report directory progress
                var directoryProgress = new UploadProgress
                {
                    FileName = $"Directory: {Path.GetFileName(remotePath)}",
                    TotalBytes = totalFiles,
                    BytesUploaded = processedFiles,
                    Timestamp = DateTime.UtcNow
                };
                progress?.Report(directoryProgress);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> UploadDirectoryAsync(string localPath, string remotePath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {

            if (!Directory.Exists(localPath))
                throw new DirectoryNotFoundException($"Local directory not found: {localPath}");

            // Create remote directory if it doesn't exist
            if (!await DirectoryExistsAsync(remotePath))
            {
                await CreateDirectoryAsync(remotePath);
            }

            var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var processedFiles = 0;

            foreach (var localFilePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(localPath, localFilePath);
                var remoteFilePath = Path.Combine(remotePath, relativePath).Replace('\\', '/');

                // Ensure remote directory exists for this file
                var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(remoteDir) && !await DirectoryExistsAsync(remoteDir))
                {
                    await CreateDirectoryAsync(remoteDir);
                }

                // Upload file
                await UploadFileAsync(localFilePath, remoteFilePath, progress, cancellationToken);

                processedFiles++;

                // Report directory progress
                var directoryProgress = new UploadProgress
                {
                    FileName = $"Directory: {Path.GetFileName(localPath)}",
                    TotalBytes = totalFiles,
                    BytesUploaded = processedFiles,
                    Timestamp = DateTime.UtcNow
                };
                progress?.Report(directoryProgress);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteFileAsync(string remotePath)
    {
        try
        {
            lock (_lock)
            {
                _sftpClient.DeleteFile(remotePath);
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IEnumerable<RemoteFileInfo>> ListFilesAsync(string remotePath)
    {
        try
        {
            lock (_lock)
            {
                var files = _sftpClient.ListDirectory(remotePath);

                var fileInfos = new List<RemoteFileInfo>();
                foreach (var file in files)
                {
                    if (file.Name == "." || file.Name == "..") continue;

                    fileInfos.Add(new RemoteFileInfo
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        IsDirectory = file.IsDirectory
                    });
                }

                return fileInfos;
            }
        }
        catch (Exception)
        {
            return new List<RemoteFileInfo>();
        }
    }

    public async Task<bool> CreateDirectoryAsync(string remotePath)
    {
        try
        {
            lock (_lock)
            {
                _sftpClient.CreateDirectory(remotePath);
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DirectoryExistsAsync(string remotePath)
    {
        try
        {
            lock (_lock)
            {
                return _sftpClient.Exists(remotePath) && _sftpClient.GetAttributes(remotePath).IsDirectory;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(string remotePath)
    {
        try
        {
            lock (_lock)
            {
                return _sftpClient.Exists(remotePath) && !_sftpClient.GetAttributes(remotePath).IsDirectory;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<long> GetFileSize(string remotePath)
    {
        try
        {
            lock (_lock)
            {
                return _sftpClient.GetAttributes(remotePath).Size;
            }
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private SshClient CreateSshClient(SshClientOptions config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        switch (config.AuthType)
        {
            case AuthenticationType.Password:
                if (string.IsNullOrEmpty(config.Password))
                    throw new ArgumentException("Password is required for password authentication.");
                return new SshClient(config.Host, config.Port, config.Username, config.Password);

            case AuthenticationType.PrivateKey:
                if (string.IsNullOrEmpty(config.PrivateKeyPath))
                    throw new ArgumentException("Private key path is required for private key authentication.");
                var keyFile = new PrivateKeyFile(config.PrivateKeyPath);
                return new SshClient(config.Host, config.Port, config.Username, keyFile);

            case AuthenticationType.PasswordAndPrivateKey:
                if (string.IsNullOrEmpty(config.PrivateKeyPath))
                    throw new ArgumentException("Private key path is required for password and private key authentication.");
                var keyFileWithPassword = new PrivateKeyFile(config.PrivateKeyPath, config.Password);
                var authMethods = new AuthenticationMethod[]
                {
                    new PasswordAuthenticationMethod(config.Username, config.Password ?? string.Empty),
                    new PrivateKeyAuthenticationMethod(config.Username, keyFileWithPassword)
                };
                var connectionInfo = new ConnectionInfo(config.Host, config.Port, config.Username, authMethods);
                return new SshClient(connectionInfo);

            default:
                throw new ArgumentException($"Unsupported authentication type: {config.AuthType}");
        }
    }

    private bool IsSameConfiguration(SshClientOptions config)
    {
        return config.Host == _config.SshConfiguration.Host &&
               config.Port == _config.SshConfiguration.Port &&
               config.Username == _config.SshConfiguration.Username &&
               config.AuthType == _config.SshConfiguration.AuthType;
    }

    private void ValidateConfiguration(SshClientOptions config)
    {
        if (!IsSameConfiguration(config))
        {
            throw new InvalidOperationException("Configuration does not match the injected service configuration.");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _sftpClient?.Dispose();
            _sshClient?.Dispose();
            _disposed = true;
        }
    }

}

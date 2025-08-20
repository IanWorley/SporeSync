using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using Directory = System.IO.Directory;

namespace SporeSync.Infrastructure.Services;

public class SshClientService : ISshService, IDisposable
{
    private readonly SshClient _sshClient;
    private readonly SftpClient _sftpClient;
    private readonly SshConfiguration _config;
    private bool _disposed = false;

    public SshClientService(IOptions<SshConfiguration> config)
    {
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));

        // Create SSH client
        _sshClient = CreateSshClient(_config);

        // Create SFTP client using the same connection info
        _sftpClient = new SftpClient(_sshClient.ConnectionInfo);

        // Connect both clients
        _sshClient.Connect();
        _sftpClient.Connect();
    }

    public async Task<bool> TestConnectionAsync(SshConfiguration config)
    {
        try
        {
            // Use the injected client, but validate config matches
            if (!IsSameConfiguration(config))
            {
                throw new InvalidOperationException("Configuration does not match the injected service configuration.");
            }

            return _sshClient.IsConnected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> UploadFileAsync(SshConfiguration config, string localPath, string remotePath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateConfiguration(config);

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

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await remoteStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;

                uploadProgress.BytesUploaded = totalBytesRead;
                progress?.Report(uploadProgress);

                cancellationToken.ThrowIfCancellationRequested();
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DownloadFileAsync(SshConfiguration config, string remotePath, string localPath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateConfiguration(config);

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

            while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await localStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;

                downloadProgress.BytesUploaded = totalBytesRead;
                progress?.Report(downloadProgress);

                cancellationToken.ThrowIfCancellationRequested();
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DownloadDirectoryAsync(SshConfiguration config, string remotePath, string localPath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateConfiguration(config);

            if (!_sftpClient.Exists(remotePath))
                throw new DirectoryNotFoundException($"Remote directory not found: {remotePath}");

            var attributes = _sftpClient.GetAttributes(remotePath);
            if (!attributes.IsDirectory)
                throw new InvalidOperationException($"Remote path is not a directory: {remotePath}");

            // Create local directory if it doesn't exist
            if (!Directory.Exists(localPath))
                Directory.CreateDirectory(localPath);

            var files = await ListFilesAsync(config, remotePath);
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
                    await DownloadDirectoryAsync(config, remoteFilePath, localFilePath, progress, cancellationToken);
                }
                else
                {
                    // Download file
                    await DownloadFileAsync(config, remoteFilePath, localFilePath, progress, cancellationToken);
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

    public async Task<bool> UploadDirectoryAsync(SshConfiguration config, string localPath, string remotePath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateConfiguration(config);

            if (!Directory.Exists(localPath))
                throw new DirectoryNotFoundException($"Local directory not found: {localPath}");

            // Create remote directory if it doesn't exist
            if (!await DirectoryExistsAsync(config, remotePath))
            {
                await CreateDirectoryAsync(config, remotePath);
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
                if (!string.IsNullOrEmpty(remoteDir) && !await DirectoryExistsAsync(config, remoteDir))
                {
                    await CreateDirectoryAsync(config, remoteDir);
                }

                // Upload file
                await UploadFileAsync(config, localFilePath, remoteFilePath, progress, cancellationToken);

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

    public async Task<bool> DeleteFileAsync(SshConfiguration config, string remotePath)
    {
        try
        {
            ValidateConfiguration(config);
            _sftpClient.DeleteFile(remotePath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IEnumerable<RemoteFileInfo>> ListFilesAsync(SshConfiguration config, string remotePath)
    {
        try
        {
            ValidateConfiguration(config);
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
        catch (Exception)
        {
            return new List<RemoteFileInfo>();
        }
    }

    public async Task<bool> CreateDirectoryAsync(SshConfiguration config, string remotePath)
    {
        try
        {
            ValidateConfiguration(config);
            _sftpClient.CreateDirectory(remotePath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DirectoryExistsAsync(SshConfiguration config, string remotePath)
    {
        try
        {
            ValidateConfiguration(config);
            return _sftpClient.Exists(remotePath) && _sftpClient.GetAttributes(remotePath).IsDirectory;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(SshConfiguration config, string remotePath)
    {
        try
        {
            ValidateConfiguration(config);
            return _sftpClient.Exists(remotePath) && !_sftpClient.GetAttributes(remotePath).IsDirectory;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<long> GetFileSizeAsync(SshConfiguration config, string remotePath)
    {
        try
        {
            ValidateConfiguration(config);
            return _sftpClient.GetAttributes(remotePath).Size;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private SshClient CreateSshClient(SshConfiguration config)
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
                var keyFile = new PrivateKeyFile(config.PrivateKeyPath, config.PrivateKeyPassphrase);
                return new SshClient(config.Host, config.Port, config.Username, keyFile);

            case AuthenticationType.PasswordAndPrivateKey:
                if (string.IsNullOrEmpty(config.PrivateKeyPath))
                    throw new ArgumentException("Private key path is required for password and private key authentication.");
                var keyFileWithPassword = new PrivateKeyFile(config.PrivateKeyPath, config.PrivateKeyPassphrase);
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

    private bool IsSameConfiguration(SshConfiguration config)
    {
        return config.Host == _config.Host &&
               config.Port == _config.Port &&
               config.Username == _config.Username &&
               config.AuthType == _config.AuthType;
    }

    private void ValidateConfiguration(SshConfiguration config)
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

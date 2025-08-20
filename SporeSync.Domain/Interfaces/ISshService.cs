using SporeSync.Domain.Models;

namespace SporeSync.Domain.Interfaces
{
    public interface ISshService
    {
        Task<bool> TestConnectionAsync(SshConfiguration config);
        Task<bool> UploadFileAsync(SshConfiguration config, string localPath, string remotePath, 
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DownloadFileAsync(SshConfiguration config, string remotePath, string localPath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> UploadDirectoryAsync(SshConfiguration config, string localPath, string remotePath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DownloadDirectoryAsync(SshConfiguration config, string remotePath, string localPath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteFileAsync(SshConfiguration config, string remotePath);
        Task<IEnumerable<RemoteFileInfo>> ListFilesAsync(SshConfiguration config, string remotePath);
        Task<bool> CreateDirectoryAsync(SshConfiguration config, string remotePath);
        Task<bool> DirectoryExistsAsync(SshConfiguration config, string remotePath);
        Task<bool> FileExistsAsync(SshConfiguration config, string remotePath);
        Task<long> GetFileSizeAsync(SshConfiguration config, string remotePath);
    }
    
    public class RemoteFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsDirectory { get; set; }
    }
    
    public class UploadProgress
    {
        public long BytesUploaded { get; set; }
        public long TotalBytes { get; set; }
        public double ProgressPercentage => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100 : 0;
        public string FileName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

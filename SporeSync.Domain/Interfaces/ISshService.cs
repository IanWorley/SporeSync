using SporeSync.Domain.Models;

namespace SporeSync.Domain.Interfaces
{
    public interface ISshService
    {
        Task<bool> TestConnectionAsync();
        Task<bool> UploadFileAsync(string localPath, string remotePath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DownloadFileAsync(string remotePath, string localPath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> UploadDirectoryAsync(string localPath, string remotePath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DownloadDirectoryAsync(string remotePath, string localPath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteFileAsync(string remotePath);
        Task<IEnumerable<RemoteFileInfo>> ListFilesAsync(string remotePath);
        Task<bool> CreateDirectoryAsync(string remotePath);
        Task<bool> DirectoryExistsAsync(string remotePath);
        Task<bool> FileExistsAsync(string remotePath);
        Task<long> GetFileSize(string remotePath);
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

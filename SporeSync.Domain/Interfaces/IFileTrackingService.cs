using SporeSync.Domain.Models;

namespace SporeSync.Domain.Interfaces
{
    public interface IFileTrackingService
    {
        Task<IEnumerable<TrackedItem>> ScanDirectoryAsync(string remotePath);
        Task<IEnumerable<TrackedItem>> ScanDirectoriesAsync(string remotePath, CancellationToken cancellationToken);
        Task<IEnumerable<TrackedItem>> GetChangedFilesAsync(int directoryId);
        Task<TrackedItem?> GetFileInfoAsync(int fileId);
        Task<string> CalculateFileHashAsync(string filePath);
        Task<bool> MarkFileAsSyncedAsync(int fileId);
        Task<IEnumerable<TrackedItem>> GetFilesByStatusAsync(FileStatus status);
        Task<bool> RemoveDeletedFilesAsync(int directoryId);
    }
}

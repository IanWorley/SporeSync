using System.ComponentModel.DataAnnotations;

namespace SporeSync.Domain.Models
{
    public class TrackedItem
    {
        public String Id { get; set; }

        [Required]
        [StringLength(500)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string FilePath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        [StringLength(255)]
        public string? FileExtension { get; set; }

        [StringLength(64)]
        public string? FileHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastModified { get; set; }

        public DateTime? LastSynced { get; set; }

        public FileStatus Status { get; set; } = FileStatus.Tracked;

        [StringLength(500)]
        public string? RemotePath { get; set; }

        public int DirectoryId { get; set; }

        public virtual Directory Directory { get; set; } = null!;
    }

    public enum FileStatus
    {
        Tracked,
        Modified,
        Deleted,
        New,
        Syncing,
        SyncError
    }
}

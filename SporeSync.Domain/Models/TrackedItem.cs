using System.ComponentModel.DataAnnotations;

namespace SporeSync.Domain.Models
{
    public class TrackedItem
    {
        [Required]
        [StringLength(500)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string DestinationFilePath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        [StringLength(255)]
        public string? FileExtension { get; set; }

        [StringLength(64)]
        public string? FileHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastModified { get; set; }

        public DateTime? LastSynced { get; set; }

        [StringLength(500)]
        public string? RemotePath { get; set; }

        public bool IsDirectory { get; set; } = false;

        public List<TrackedItem> Children { get; set; } = new List<TrackedItem>();
    }
}

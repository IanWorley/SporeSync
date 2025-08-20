using System.ComponentModel.DataAnnotations;

namespace SporeSync.Domain.Models
{
    public class Directory
    {
        public int Id { get; set; }

        [Required]
        [StringLength(500)]
        public string DirectoryName { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string LocalPath { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? RemotePath { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastScanned { get; set; }

        public bool IsActive { get; set; } = true;

        public bool AutoSync { get; set; } = false;

        public int FileCount { get; set; } = 0;

        public long TotalSize { get; set; } = 0;

        public virtual ICollection<TrackedItem> Files { get; set; } = new List<TrackedItem>();
    }
}

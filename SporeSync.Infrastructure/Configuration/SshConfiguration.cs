

using System.ComponentModel.DataAnnotations;

namespace SporeSync.Domain.Models
{
    public class SshConfiguration
    {
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string Host { get; set; } = string.Empty;

        public int Port { get; set; } = 22;

        [Required]
        [StringLength(255)]
        public string Username { get; set; } = string.Empty;

        [StringLength(255)]
        public string? Password { get; set; }

        [StringLength(2000)]
        public string? PrivateKeyPath { get; set; }

        [StringLength(255)]
        public string? PrivateKeyPassphrase { get; set; }

        public int TimeoutSeconds { get; set; } = 30;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastConnectionTest { get; set; }

        public bool LastConnectionSuccess { get; set; } = false;

        public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;
    }

    public enum AuthenticationType
    {
        Password,
        PrivateKey,
        PasswordAndPrivateKey
    }
}
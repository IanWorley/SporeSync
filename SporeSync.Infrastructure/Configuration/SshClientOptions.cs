namespace SporeSync.Infrastructure.Configuration;

public class SshClientOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;
}

public enum AuthenticationType
{
    Password,
    PrivateKey,
    PasswordAndPrivateKey
}


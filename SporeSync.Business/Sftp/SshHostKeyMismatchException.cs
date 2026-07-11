namespace SporeSync.Business.Sftp;

/// <summary>
/// Thrown when an SFTP server presents a host key that does not match the
/// fingerprint pinned on the connection profile.
/// </summary>
public sealed class SshHostKeyMismatchException : Exception
{
    public SshHostKeyMismatchException(
        string host,
        int port,
        IReadOnlyCollection<string> trustedFingerprints,
        string actualFingerprint)
        : base(
            $"SSH host key verification failed for {host}:{port}. " +
            $"The server presented key fingerprint '{actualFingerprint}', which is not trusted by the connection profile. " +
            "This can indicate a man-in-the-middle attack or a legitimate host key rotation. " +
            "Verify the key through a trusted channel before updating the profile.")
    {
        Host = host;
        Port = port;
        TrustedFingerprints = trustedFingerprints;
        ActualFingerprint = actualFingerprint;
    }

    public string Host { get; }

    public int Port { get; }

    public IReadOnlyCollection<string> TrustedFingerprints { get; }

    public string ActualFingerprint { get; }
}

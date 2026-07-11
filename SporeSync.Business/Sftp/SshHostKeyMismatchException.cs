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
        string expectedFingerprint,
        string actualFingerprint)
        : base(
            $"SSH host key verification failed for {host}:{port}. " +
            $"The server presented key fingerprint '{actualFingerprint}' but the connection profile has " +
            $"'{expectedFingerprint}' pinned. This can indicate a man-in-the-middle attack or a legitimate " +
            "host key rotation. If the change is expected, update or clear the pinned fingerprint on the profile.")
    {
        Host = host;
        Port = port;
        ExpectedFingerprint = expectedFingerprint;
        ActualFingerprint = actualFingerprint;
    }

    public string Host { get; }

    public int Port { get; }

    public string ExpectedFingerprint { get; }

    public string ActualFingerprint { get; }
}

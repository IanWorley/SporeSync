using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Service;

public sealed class EncryptionKeyInitializer : IEncryptionKeyInitializer
{
    public const string InitializedPropertyName = "security.encryptionKeyInitialized";
    public const string VersionPropertyName = "security.encryptionKeyVersion";
    public const string CreatedAtPropertyName = "security.encryptionKeyCreatedAtUtc";
    public const string FirstRunCompletedAtPropertyName = "system.firstRunCompletedAtUtc";

    private const int KeyLength = 32;
    private readonly IConfiguration _configuration;
    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly ISftpConnectionProfileRepository _profileRepository;
    private readonly ISystemPropertyRepository _systemPropertyRepository;
    private readonly ILogger<EncryptionKeyInitializer> _logger;

    public EncryptionKeyInitializer(
        IConfiguration configuration,
        IEncryptionKeyProvider keyProvider,
        ISftpConnectionProfileRepository profileRepository,
        ISystemPropertyRepository systemPropertyRepository,
        ILogger<EncryptionKeyInitializer> logger)
    {
        _configuration = configuration;
        _keyProvider = keyProvider;
        _profileRepository = profileRepository;
        _systemPropertyRepository = systemPropertyRepository;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var keyPath = ResolveKeyPath(_configuration);
        var firstRunCompleted = await _systemPropertyRepository.GetByNameAsync(
            FirstRunCompletedAtPropertyName,
            cancellationToken) is not null;
        var hasEncryptedSecrets = await _profileRepository.HasAnyEncryptedSecretsAsync(cancellationToken);

        byte[] key;
        DateTimeOffset createdAt;

        if (File.Exists(keyPath))
        {
            key = ReadValidKeyFileAndLogFailure(keyPath);
            createdAt = GetKeyFileCreatedAt(keyPath);
            _logger.LogInformation("Using existing encryption key file at {KeyPath}.", keyPath);
        }
        else
        {
            if (firstRunCompleted)
            {
                const string message = "Encryption key file is missing and this system has already completed first-run initialization. Restore the key file or intentionally reset the deployment.";
                _logger.LogError("{Message} Key path: {KeyPath}", message, keyPath);
                throw new InvalidOperationException(message);
            }

            if (hasEncryptedSecrets)
            {
                const string message = "Encryption key file is missing and encrypted SFTP profiles already exist. Restore the key file or delete/recreate the profiles.";
                _logger.LogError("{Message} Key path: {KeyPath}", message, keyPath);
                throw new InvalidOperationException(message);
            }

            key = await CreateOrReadKeyFileAsync(keyPath, cancellationToken);
            createdAt = GetKeyFileCreatedAt(keyPath);
        }

        _keyProvider.Initialize(key);
        await WriteMetadataAsync(createdAt, cancellationToken);
    }

    public static string ResolveKeyPath(IConfiguration configuration)
    {
        var configuredPath = configuration["Security:EncryptionKeyPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "SporeSync", "encryption.key");
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(home, ".sporesync", "encryption.key");
        }

        return Path.Combine(AppContext.BaseDirectory, ".sporesync", "encryption.key");
    }

    private async Task<byte[]> CreateOrReadKeyFileAsync(
        string keyPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                SetOwnerOnlyDirectoryMode(directory);
            }

            var key = RandomNumberGenerator.GetBytes(KeyLength);
            await using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            SetOwnerOnlyFileMode(keyPath);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(Convert.ToBase64String(key).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);

            _logger.LogInformation("Created new encryption key file at {KeyPath}.", keyPath);
            return key;
        }
        catch (IOException) when (File.Exists(keyPath))
        {
            _logger.LogInformation("Encryption key file was created by another process at {KeyPath}; using existing file.", keyPath);
            return ReadValidKeyFileAndLogFailure(keyPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Unable to create encryption key file at '{keyPath}'. Check directory permissions.",
                exception);
        }
    }

    private static byte[] ReadValidKeyFile(string keyPath)
    {
        try
        {
            var key = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
            if (key.Length == KeyLength)
            {
                return key;
            }
        }
        catch (FormatException)
        {
        }

        throw new InvalidOperationException("Encryption key file is invalid. It must contain base64 text for exactly 32 bytes.");
    }

    private byte[] ReadValidKeyFileAndLogFailure(string keyPath)
    {
        try
        {
            return ReadValidKeyFile(keyPath);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "Encryption key file is invalid at {KeyPath}.", keyPath);
            throw;
        }
    }

    private async Task WriteMetadataAsync(DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _systemPropertyRepository.UpsertAsync(InitializedPropertyName, "true", cancellationToken);
        await _systemPropertyRepository.UpsertAsync(VersionPropertyName, EncryptionKeyProvider.CurrentVersion, cancellationToken);
        await _systemPropertyRepository.InsertIfMissingAsync(
            CreatedAtPropertyName,
            createdAt.UtcDateTime.ToString("O"),
            cancellationToken);
        await _systemPropertyRepository.InsertIfMissingAsync(
            FirstRunCompletedAtPropertyName,
            now.UtcDateTime.ToString("O"),
            cancellationToken);
        await _systemPropertyRepository.InsertIfMissingAsync(
            "db_log_level",
            "info",
            cancellationToken);
    }

    private static DateTimeOffset GetKeyFileCreatedAt(string keyPath)
    {
        try
        {
            return File.GetCreationTimeUtc(keyPath);
        }
        catch (IOException)
        {
            return DateTimeOffset.UtcNow;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static void SetOwnerOnlyDirectoryMode(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void SetOwnerOnlyFileMode(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

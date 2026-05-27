namespace SftpSync.Domain.Model;

public sealed class SystemProperty
{
    public required string Id { get; init; }

    public required string PropertyName { get; init; }

    public required string PropertyValue { get; init; }
}

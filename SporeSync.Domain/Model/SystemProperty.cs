namespace SporeSync.Domain.Model;

public sealed class SystemProperty
{
    public required Guid Id { get; init; }

    public required string PropertyName { get; init; }

    public required string PropertyValue { get; init; }
}

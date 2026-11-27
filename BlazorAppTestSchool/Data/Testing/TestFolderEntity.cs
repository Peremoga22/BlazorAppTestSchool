namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestFolderEntity
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

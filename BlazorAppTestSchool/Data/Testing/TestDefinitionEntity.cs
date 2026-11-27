namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestDefinitionEntity
{
    public Guid Id { get; init; }
    public required string Title { get; set; }
    public required string Subject { get; set; }
    public int? Grade { get; set; }
    public DateTimeOffset UploadedAt { get; init; }
    public bool IsActive { get; set; }
    public Guid? FolderId { get; set; }
    public required string DefinitionJson { get; set; }
}

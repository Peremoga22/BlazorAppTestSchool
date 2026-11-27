using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestDefinitionStore(IServiceScopeFactory scopeFactory)
{
    public async Task<TestDefinitionSummary> SaveAsync(TestDefinitionModel model, string json, Guid? folderId = null, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Several tests can be active for the same subject/grade at once (e.g. "Варіант 1" / "Варіант 2"),
        // so saving one does not deactivate the others — teachers toggle each test's availability explicitly.
        var entity = new TestDefinitionEntity
        {
            Id = Guid.NewGuid(),
            Title = model.Title.Trim(),
            Subject = model.Subject.Trim(),
            Grade = model.Grade,
            UploadedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            FolderId = folderId,
            DefinitionJson = json
        };
        database.TestDefinitions.Add(entity);
        await database.SaveChangesAsync(cancellationToken);

        return ToSummary(entity);
    }

    /// <summary>Tests inside a folder, or the unfiled ones when <paramref name="folderId"/> is null.</summary>
    public async Task<TestDefinitionSummary[]> ListByFolderAsync(Guid? folderId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entities = await database.TestDefinitions.AsNoTracking()
            .Where(test => test.FolderId == folderId)
            .OrderByDescending(test => test.UploadedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToSummary).ToArray();
    }

    /// <summary>The single most-recently-activated test for a subject/grade, for the common case of one variant.</summary>
    public async Task<TestDefinitionModel?> GetActiveAsync(string subject, int? grade, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var json = await database.TestDefinitions.AsNoTracking()
            .Where(test => test.IsActive && test.Subject == subject && test.Grade == grade)
            .OrderByDescending(test => test.UploadedAt)
            .Select(test => test.DefinitionJson)
            .FirstOrDefaultAsync(cancellationToken);

        return json is null ? null : TestDefinitionParser.Parse(json);
    }

    /// <summary>All tests currently active for a subject/grade, so a student can be offered a choice of variants.</summary>
    public async Task<TestDefinitionSummary[]> ListActiveAsync(string subject, int? grade, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entities = await database.TestDefinitions.AsNoTracking()
            .Where(test => test.IsActive && test.Subject == subject && test.Grade == grade)
            .OrderBy(test => test.UploadedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToSummary).ToArray();
    }

    public async Task UpdateAsync(Guid id, TestDefinitionModel model, string json, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entity = await database.TestDefinitions.SingleOrDefaultAsync(test => test.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Title = model.Title.Trim();
        entity.Subject = model.Subject.Trim();
        entity.Grade = model.Grade;
        entity.DefinitionJson = json;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<TestDefinitionModel?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var json = await database.TestDefinitions.AsNoTracking()
            .Where(test => test.Id == id)
            .Select(test => test.DefinitionJson)
            .SingleOrDefaultAsync(cancellationToken);

        return json is null ? null : TestDefinitionParser.Parse(json);
    }

    public async Task<TestDefinitionSummary[]> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entities = await database.TestDefinitions.AsNoTracking()
            .OrderByDescending(test => test.UploadedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToSummary).ToArray();
    }

    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        await SetActiveAsync(id, true, cancellationToken);

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        await SetActiveAsync(id, false, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.TestDefinitions.Where(test => test.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = await database.TestDefinitions.SingleOrDefaultAsync(test => test.Id == id, cancellationToken);
        if (target is null)
        {
            return;
        }

        target.IsActive = isActive;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAnyAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.TestDefinitions.AnyAsync(cancellationToken);
    }

    private static TestDefinitionSummary ToSummary(TestDefinitionEntity entity) => new(
        entity.Id, entity.Title, entity.Subject, entity.Grade, entity.UploadedAt, entity.IsActive, entity.FolderId);
}

public sealed record TestDefinitionSummary(Guid Id, string Title, string Subject, int? Grade, DateTimeOffset UploadedAt, bool IsActive, Guid? FolderId);

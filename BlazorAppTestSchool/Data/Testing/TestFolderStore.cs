using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestFolderStore(IServiceScopeFactory scopeFactory)
{
    public async Task<TestFolderSummary> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entity = new TestFolderEntity
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        database.TestFolders.Add(entity);
        await database.SaveChangesAsync(cancellationToken);

        return new TestFolderSummary(entity.Id, entity.Name, entity.CreatedAt, 0);
    }

    public async Task<TestFolderSummary[]> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await database.TestFolders.AsNoTracking()
            .OrderBy(folder => folder.Name)
            .Select(folder => new TestFolderSummary(
                folder.Id, folder.Name, folder.CreatedAt,
                database.TestDefinitions.Count(test => test.FolderId == folder.Id)))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<TestFolderSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await database.TestFolders.AsNoTracking()
            .Where(folder => folder.Id == id)
            .Select(folder => new TestFolderSummary(
                folder.Id, folder.Name, folder.CreatedAt,
                database.TestDefinitions.Count(test => test.FolderId == folder.Id)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entity = await database.TestFolders.SingleOrDefaultAsync(folder => folder.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Name = name.Trim();
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deletes the folder but keeps the tests inside it — they just become unfiled.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await database.TestDefinitions.Where(test => test.FolderId == id)
            .ExecuteUpdateAsync(set => set.SetProperty(test => test.FolderId, (Guid?)null), cancellationToken);
        await database.TestFolders.Where(folder => folder.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record TestFolderSummary(Guid Id, string Name, DateTimeOffset CreatedAt, int TestCount);

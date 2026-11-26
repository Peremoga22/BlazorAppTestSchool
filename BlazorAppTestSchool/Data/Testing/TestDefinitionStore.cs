using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestDefinitionStore(IServiceScopeFactory scopeFactory)
{
    public async Task<TestDefinitionSummary> SaveAsync(TestDefinitionModel model, string json, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var subject = model.Subject.Trim();
        var previouslyActive = await database.TestDefinitions
            .Where(test => test.IsActive && test.Subject == subject && test.Grade == model.Grade)
            .ToListAsync(cancellationToken);
        foreach (var test in previouslyActive)
        {
            test.IsActive = false;
        }

        var entity = new TestDefinitionEntity
        {
            Id = Guid.NewGuid(),
            Title = model.Title.Trim(),
            Subject = subject,
            Grade = model.Grade,
            UploadedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            DefinitionJson = json
        };
        database.TestDefinitions.Add(entity);
        await database.SaveChangesAsync(cancellationToken);

        return ToSummary(entity);
    }

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

    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = await database.TestDefinitions.SingleOrDefaultAsync(test => test.Id == id, cancellationToken);
        if (target is null)
        {
            return;
        }

        var siblings = await database.TestDefinitions
            .Where(test => test.IsActive && test.Subject == target.Subject && test.Grade == target.Grade && test.Id != id)
            .ToListAsync(cancellationToken);
        foreach (var sibling in siblings)
        {
            sibling.IsActive = false;
        }

        target.IsActive = true;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAnyAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.TestDefinitions.AnyAsync(cancellationToken);
    }

    private static TestDefinitionSummary ToSummary(TestDefinitionEntity entity) => new(
        entity.Id, entity.Title, entity.Subject, entity.Grade, entity.UploadedAt, entity.IsActive);
}

public sealed record TestDefinitionSummary(Guid Id, string Title, string Subject, int? Grade, DateTimeOffset UploadedAt, bool IsActive);

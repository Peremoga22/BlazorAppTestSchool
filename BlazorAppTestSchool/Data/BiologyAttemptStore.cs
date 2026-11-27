using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data;

public sealed class BiologyAttemptStore(IServiceScopeFactory scopeFactory)
{
    public const int PageSize = 25;

    public async Task SaveAsync(Guid attemptId, BiologyAttemptReport report)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await database.BiologyAttempts.AnyAsync(attempt => attempt.Id == attemptId))
        {
            return;
        }

        database.BiologyAttempts.Add(new BiologyAttempt
        {
            Id = attemptId,
            SubmittedAt = DateTimeOffset.UtcNow,
            Grade = report.Grade,
            StudentName = report.StudentName,
            ReportJson = JsonSerializer.Serialize(report)
        });
        await database.SaveChangesAsync();
    }

    /// <summary>Every grade that has at least one submitted attempt, for building one "board" per grade.</summary>
    public async Task<int[]> DistinctGradesAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.BiologyAttempts.AsNoTracking()
            .Select(attempt => attempt.Grade)
            .Distinct()
            .OrderBy(grade => grade)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<(BiologyAttemptSummary[] Attempts, bool HasMore)> ListByGradeAsync(
        int grade, int page, string? search, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var query = database.BiologyAttempts.AsNoTracking().Where(attempt => attempt.Grade == grade);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(attempt => attempt.StudentName.Contains(search));
        }

        var attempts = await query
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .ThenByDescending(attempt => attempt.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize + 1)
            .ToArrayAsync(cancellationToken);

        var summaries = attempts.Take(PageSize).Select(attempt =>
        {
            var report = ReadReport(attempt.ReportJson);
            return new BiologyAttemptSummary(attempt.Id, attempt.SubmittedAt, report.StudentName,
                report.Grade, report.Score, report.Questions.Length,
                report.Questions.Any(question => question.Points is null), report.Duration, report.TestTitle);
        }).ToArray();

        return (summaries, attempts.Length > PageSize);
    }

    public async Task<BiologyAttemptReport?> FindAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportJson = await database.BiologyAttempts
            .Where(attempt => attempt.Id == attemptId)
            .Select(attempt => attempt.ReportJson)
            .SingleOrDefaultAsync(cancellationToken);

        return reportJson is null ? null : ReadReport(reportJson);
    }

    public async Task DeleteAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.BiologyAttempts.Where(attempt => attempt.Id == attemptId).ExecuteDeleteAsync(cancellationToken);
    }

    private static BiologyAttemptReport ReadReport(string json) =>
        JsonSerializer.Deserialize<BiologyAttemptReport>(json)
        ?? throw new InvalidDataException("The attempt report is empty.");
}

public sealed record BiologyAttemptSummary(
    Guid Id, DateTimeOffset? SubmittedAt, string StudentName, int Grade,
    int Score, int MaxScore, bool PendingReview, TimeSpan Duration, string? TestTitle);

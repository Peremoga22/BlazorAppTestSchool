using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data;

public sealed class BiologyAttemptStore(IServiceScopeFactory scopeFactory)
{
    public const int PageSize = 25;

    public static string NormalizeLastName(string? lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName))
        {
            return "";
        }

        var trimmed = lastName.Trim().Replace('’', '\'').Replace('`', '\'');
        return trimmed.ToLowerInvariant();
    }

    public static string LastNameOf(BiologyAttemptReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.LastName))
        {
            return report.LastName;
        }

        var firstWord = report.StudentName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return firstWord.Length == 0 ? "" : firstWord[0];
    }

    /// <summary>
    /// A surname that already has a submitted paper in this grade cannot start again
    /// unless the teacher has an unused approval.
    /// </summary>
    public async Task<bool> CanStartAsync(string lastName, int grade, CancellationToken cancellationToken = default)
    {
        var key = NormalizeLastName(lastName);
        if (key.Length == 0)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var taken = await database.BiologyAttempts.AsNoTracking()
            .AnyAsync(attempt => attempt.LastNameKey == key && attempt.Grade == grade, cancellationToken);
        if (!taken)
        {
            return true;
        }

        return await HasOpenRetakeAsync(key, grade, cancellationToken);
    }

    public async Task<bool> HasOpenRetakeAsync(string lastName, int grade, CancellationToken cancellationToken = default)
    {
        var key = NormalizeLastName(lastName);
        if (key.Length == 0)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.BiologyRetakeApprovals.AsNoTracking()
            .AnyAsync(approval => approval.LastNameKey == key && approval.Grade == grade && approval.ConsumedAt == null,
                cancellationToken);
    }

    public async Task ApproveRetakeAsync(string lastName, int grade, CancellationToken cancellationToken = default)
    {
        var key = NormalizeLastName(lastName);
        if (key.Length == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var alreadyOpen = await database.BiologyRetakeApprovals
            .AnyAsync(approval => approval.LastNameKey == key && approval.Grade == grade && approval.ConsumedAt == null,
                cancellationToken);
        if (alreadyOpen)
        {
            return;
        }

        database.BiologyRetakeApprovals.Add(new BiologyRetakeApproval
        {
            Id = Guid.NewGuid(),
            LastNameKey = key,
            Grade = grade,
            ApprovedAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Fills LastNameKey on papers saved before the column existed.</summary>
    public async Task BackfillLastNameKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var missing = await database.BiologyAttempts
            .Where(attempt => attempt.LastNameKey == "")
            .ToListAsync(cancellationToken);
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var attempt in missing)
        {
            var report = ReadReport(attempt.ReportJson);
            attempt.LastNameKey = NormalizeLastName(LastNameOf(report));
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <returns>False when this surname already submitted in the grade and the teacher has not approved a retake.</returns>
    public async Task<bool> SaveAsync(Guid attemptId, BiologyAttemptReport report)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await database.BiologyAttempts.AnyAsync(attempt => attempt.Id == attemptId))
        {
            return true;
        }

        var key = NormalizeLastName(LastNameOf(report));
        var taken = key.Length > 0 && await database.BiologyAttempts
            .AnyAsync(attempt => attempt.LastNameKey == key && attempt.Grade == report.Grade);
        if (taken)
        {
            var grant = await database.BiologyRetakeApprovals
                .Where(approval => approval.LastNameKey == key && approval.Grade == report.Grade && approval.ConsumedAt == null)
                .OrderBy(approval => approval.ApprovedAt)
                .FirstOrDefaultAsync();
            if (grant is null)
            {
                return false;
            }

            grant.ConsumedAt = DateTimeOffset.UtcNow;
        }

        database.BiologyAttempts.Add(new BiologyAttempt
        {
            Id = attemptId,
            SubmittedAt = DateTimeOffset.UtcNow,
            Grade = report.Grade,
            StudentName = report.StudentName,
            LastNameKey = key,
            ReportJson = JsonSerializer.Serialize(report)
        });
        await database.SaveChangesAsync();
        return true;
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

        var openRetakes = await database.BiologyRetakeApprovals.AsNoTracking()
            .Where(approval => approval.Grade == grade && approval.ConsumedAt == null)
            .Select(approval => approval.LastNameKey)
            .ToListAsync(cancellationToken);

        var summaries = attempts.Take(PageSize).Select(attempt =>
        {
            var report = ReadReport(attempt.ReportJson);
            var lastNameKey = attempt.LastNameKey.Length > 0
                ? attempt.LastNameKey
                : NormalizeLastName(LastNameOf(report));
            return new BiologyAttemptSummary(attempt.Id, attempt.SubmittedAt, report.StudentName,
                report.Grade, report.Score, report.Questions.Length,
                report.Questions.Any(question => question.Points is null), report.Duration, report.TestTitle,
                lastNameKey, openRetakes.Contains(lastNameKey));
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
    int Score, int MaxScore, bool PendingReview, TimeSpan Duration, string? TestTitle,
    string LastNameKey, bool RetakeOpen);

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlazorAppTestSchool.Data;

public sealed class BiologyAttemptStore(IServiceScopeFactory scopeFactory, ILogger<BiologyAttemptStore> logger)
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

        if (string.IsNullOrWhiteSpace(report.StudentName))
        {
            return "";
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
        var taken = await HasSubmittedAsync(database, key, grade, cancellationToken);
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
        try
        {
            return await database.BiologyRetakeApprovals.AsNoTracking()
                .AnyAsync(approval => approval.LastNameKey == key && approval.Grade == grade && approval.ConsumedAt == null,
                    cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read retake approvals.");
            return false;
        }
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
        var taken = key.Length > 0 && await HasSubmittedAsync(database, key, report.Grade, CancellationToken.None);
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

    public async Task<BiologyAttemptSummary[]> ListByGradeAsync(
        int grade, string? search, CancellationToken cancellationToken)
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
            .Select(attempt => new AttemptRow(attempt.Id, attempt.SubmittedAt, attempt.Grade, attempt.StudentName, attempt.ReportJson))
            .ToArrayAsync(cancellationToken);

        var openRetakes = await OpenRetakeKeysAsync(database, grade, cancellationToken);

        return attempts.Select(attempt => ToSummary(attempt, openRetakes)).ToArray();
    }

    private async Task<bool> HasSubmittedAsync(
        ApplicationDbContext database, string lastNameKey, int grade, CancellationToken cancellationToken)
    {
        var rows = await database.BiologyAttempts.AsNoTracking()
            .Where(attempt => attempt.Grade == grade)
            .Select(attempt => new { attempt.StudentName, attempt.ReportJson })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!TryRead(row.ReportJson, out var report))
            {
                if (NormalizeLastName(FirstWord(row.StudentName)) == lastNameKey)
                {
                    return true;
                }

                continue;
            }

            if (NormalizeLastName(LastNameOf(report)) == lastNameKey)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<HashSet<string>> OpenRetakeKeysAsync(
        ApplicationDbContext database, int grade, CancellationToken cancellationToken)
    {
        try
        {
            var keys = await database.BiologyRetakeApprovals.AsNoTracking()
                .Where(approval => approval.Grade == grade && approval.ConsumedAt == null)
                .Select(approval => approval.LastNameKey)
                .ToListAsync(cancellationToken);
            return keys.ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read retake approvals. The submitted papers are still listed.");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static BiologyAttemptSummary ToSummary(AttemptRow attempt, HashSet<string> openRetakes)
    {
        if (!TryRead(attempt.ReportJson, out var report))
        {
            var fallbackName = string.IsNullOrWhiteSpace(attempt.StudentName) ? "Без прізвища" : attempt.StudentName;
            var fallbackKey = NormalizeLastName(FirstWord(fallbackName));
            return new BiologyAttemptSummary(attempt.Id, attempt.SubmittedAt, fallbackName, attempt.Grade,
                0, 0, false, TimeSpan.Zero, null, fallbackKey, openRetakes.Contains(fallbackKey), null);
        }

        var questions = report.Questions ?? [];
        var name = string.IsNullOrWhiteSpace(report.StudentName) ? attempt.StudentName : report.StudentName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Без прізвища";
        }

        var lastNameKey = NormalizeLastName(LastNameOf(report));
        return new BiologyAttemptSummary(attempt.Id, attempt.SubmittedAt, name, report.Grade, report.Score,
            questions.Length, questions.Any(question => question.Points is null), report.Duration, report.TestTitle,
            lastNameKey, lastNameKey.Length > 0 && openRetakes.Contains(lastNameKey), report.TestId);
    }

    private static bool TryRead(string? json, out BiologyAttemptReport report)
    {
        report = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<BiologyAttemptReport>(json);
            if (parsed is null)
            {
                return false;
            }

            report = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FirstWord(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[0];
    }

    private sealed record AttemptRow(Guid Id, DateTimeOffset? SubmittedAt, int Grade, string StudentName, string ReportJson);

    public async Task<BiologyAttemptReport?> FindAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportJson = await database.BiologyAttempts
            .Where(attempt => attempt.Id == attemptId)
            .Select(attempt => attempt.ReportJson)
            .SingleOrDefaultAsync(cancellationToken);

        return reportJson is not null && TryRead(reportJson, out var report) ? report : null;
    }

    public async Task DeleteAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.BiologyAttempts.Where(attempt => attempt.Id == attemptId).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record BiologyAttemptSummary(
    Guid Id, DateTimeOffset? SubmittedAt, string StudentName, int Grade,
    int Score, int MaxScore, bool PendingReview, TimeSpan Duration, string? TestTitle,
    string LastNameKey, bool RetakeOpen, Guid? TestId);

using System.Text.Json;
using BlazorAppTestSchool.Data.Testing;
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
    /// A surname that already submitted this subject in this grade cannot start again
    /// unless the teacher has an unused approval. Without a subject, any paper in the grade blocks a start.
    /// </summary>
    public async Task<bool> CanStartAsync(string lastName, int grade, string? subject = null, CancellationToken cancellationToken = default)
    {
        var key = NormalizeLastName(lastName);
        if (key.Length == 0)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var taken = await HasSubmittedAsync(database, key, grade, subject, cancellationToken);
        if (!taken)
        {
            return true;
        }

        return await HasOpenRetakeAsync(key, grade, subject, cancellationToken);
    }

    public async Task<bool> HasOpenRetakeAsync(string lastName, int grade, string? subject = null, CancellationToken cancellationToken = default)
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
            var subjects = await database.BiologyRetakeApprovals.AsNoTracking()
                .Where(approval => approval.LastNameKey == key && approval.Grade == grade && approval.ConsumedAt == null)
                .Select(approval => approval.Subject)
                .ToListAsync(cancellationToken);
            return subjects.Any(existing => SubjectsMatch(existing, subject));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read retake approvals.");
            return false;
        }
    }

    public async Task ApproveRetakeAsync(string lastName, int grade, string? subject = null, CancellationToken cancellationToken = default)
    {
        var key = NormalizeLastName(lastName);
        if (key.Length == 0)
        {
            return;
        }

        var normalizedSubject = NormalizeSubject(subject);
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var openSubjects = await database.BiologyRetakeApprovals
            .Where(approval => approval.LastNameKey == key && approval.Grade == grade && approval.ConsumedAt == null)
            .Select(approval => approval.Subject)
            .ToListAsync(cancellationToken);
        if (openSubjects.Any(existing => SubjectsMatch(existing, normalizedSubject)))
        {
            return;
        }

        database.BiologyRetakeApprovals.Add(new BiologyRetakeApproval
        {
            Id = Guid.NewGuid(),
            LastNameKey = key,
            Grade = grade,
            Subject = normalizedSubject,
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
        string? subject = null;
        if (report.TestId is Guid testId)
        {
            subject = await database.TestDefinitions.AsNoTracking()
                .Where(test => test.Id == testId)
                .Select(test => test.Subject)
                .FirstOrDefaultAsync();
        }

        var taken = key.Length > 0 && await HasSubmittedAsync(database, key, report.Grade, subject, CancellationToken.None);
        if (taken)
        {
            var grants = await database.BiologyRetakeApprovals
                .Where(approval => approval.LastNameKey == key && approval.Grade == report.Grade && approval.ConsumedAt == null)
                .OrderBy(approval => approval.ApprovedAt)
                .ToListAsync();
            var grant = grants.FirstOrDefault(approval => SubjectsMatch(approval.Subject, subject));
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

        var catalog = await database.TestDefinitions.AsNoTracking()
            .Select(test => new TestDefinitionSummary(
                test.Id, test.Title, test.Subject, test.Grade, test.UploadedAt, test.IsActive, test.FolderId))
            .ToArrayAsync(cancellationToken);
        var openRetakes = await OpenRetakeKeysAsync(database, grade, cancellationToken);

        return attempts.Select(attempt => ToSummary(attempt, openRetakes, catalog)).ToArray();
    }

    private async Task<bool> HasSubmittedAsync(
        ApplicationDbContext database, string lastNameKey, int grade, string? subject, CancellationToken cancellationToken)
    {
        var rows = await database.BiologyAttempts.AsNoTracking()
            .Where(attempt => attempt.Grade == grade)
            .Select(attempt => new { attempt.StudentName, attempt.ReportJson })
            .ToListAsync(cancellationToken);

        var matchSubject = !string.IsNullOrWhiteSpace(subject);
        var catalog = matchSubject
            ? await database.TestDefinitions.AsNoTracking()
                .Select(test => new TestDefinitionSummary(
                    test.Id, test.Title, test.Subject, test.Grade, test.UploadedAt, test.IsActive, test.FolderId))
                .ToArrayAsync(cancellationToken)
            : [];

        foreach (var row in rows)
        {
            var parsed = TryRead(row.ReportJson, out var report);
            var rowKey = parsed
                ? NormalizeLastName(LastNameOf(report))
                : NormalizeLastName(FirstWord(row.StudentName));
            if (rowKey != lastNameKey)
            {
                continue;
            }

            if (!matchSubject)
            {
                return true;
            }

            if (!parsed)
            {
                continue;
            }

            var owner = AttemptFolderLink.OwningTestId(report.TestId, report.TestTitle, report.Grade, catalog);
            var test = owner is Guid id ? catalog.FirstOrDefault(item => item.Id == id) : null;
            if (test is not null
                && test.Grade == grade
                && string.Equals(test.Subject.Trim(), subject!.Trim(), StringComparison.CurrentCultureIgnoreCase))
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
            var rows = await database.BiologyRetakeApprovals.AsNoTracking()
                .Where(approval => approval.Grade == grade && approval.ConsumedAt == null)
                .Select(approval => new { approval.LastNameKey, approval.Subject })
                .ToListAsync(cancellationToken);
            return rows
                .Select(row => RetakeKey(row.LastNameKey, row.Subject))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read retake approvals. The submitted papers are still listed.");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static BiologyAttemptSummary ToSummary(AttemptRow attempt, HashSet<string> openRetakes, TestDefinitionSummary[] catalog)
    {
        if (!TryRead(attempt.ReportJson, out var report))
        {
            var fallbackName = string.IsNullOrWhiteSpace(attempt.StudentName) ? "Без прізвища" : attempt.StudentName;
            var fallbackKey = NormalizeLastName(FirstWord(fallbackName));
            return new BiologyAttemptSummary(attempt.Id, attempt.SubmittedAt, fallbackName, attempt.Grade,
                0, 0, false, TimeSpan.Zero, null, fallbackKey, false, null);
        }

        var questions = report.Questions ?? [];
        var name = string.IsNullOrWhiteSpace(report.StudentName) ? attempt.StudentName : report.StudentName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Без прізвища";
        }

        var lastNameKey = NormalizeLastName(LastNameOf(report));
        var subject = SubjectOf(report, catalog);
        var retakeOpen = lastNameKey.Length > 0 && openRetakes.Contains(RetakeKey(lastNameKey, subject));
        return new BiologyAttemptSummary(attempt.Id, attempt.SubmittedAt, name, report.Grade, report.Score,
            questions.Length, questions.Any(question => question.Points is null), report.Duration, report.TestTitle,
            lastNameKey, retakeOpen, report.TestId);
    }

    private static string? SubjectOf(BiologyAttemptReport report, TestDefinitionSummary[] catalog)
    {
        var owner = AttemptFolderLink.OwningTestId(report.TestId, report.TestTitle, report.Grade, catalog);
        var subject = owner is Guid id ? catalog.FirstOrDefault(test => test.Id == id)?.Subject : null;
        return NormalizeSubject(subject);
    }

    private static string RetakeKey(string lastNameKey, string? subject) =>
        lastNameKey + "\u001f" + (NormalizeSubject(subject) ?? "");

    private static string? NormalizeSubject(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();

    private static bool SubjectsMatch(string? left, string? right) =>
        string.Equals(NormalizeSubject(left), NormalizeSubject(right), StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Deletes submitted papers that belong to these tests. Ownership is resolved while the tests
    /// still exist, so a paper follows its own test and is not taken from another folder.
    /// </summary>
    public async Task DeleteBelongingToAsync(
        IReadOnlyCollection<Guid> testIds,
        IReadOnlyList<TestDefinitionSummary> tests,
        CancellationToken cancellationToken = default)
    {
        if (testIds.Count == 0)
        {
            return;
        }

        var targets = testIds as HashSet<Guid> ?? testIds.ToHashSet();
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await database.BiologyAttempts.AsNoTracking()
            .Select(attempt => new { attempt.Id, attempt.Grade, attempt.ReportJson })
            .ToListAsync(cancellationToken);

        var doomed = new List<Guid>();
        foreach (var row in rows)
        {
            Guid? testId = null;
            string? title = null;
            var grade = row.Grade;
            if (TryRead(row.ReportJson, out var report))
            {
                testId = report.TestId;
                title = report.TestTitle;
                grade = report.Grade;
            }

            var owner = AttemptFolderLink.OwningTestId(testId, title, grade, tests);
            if (owner is Guid id && targets.Contains(id))
            {
                doomed.Add(row.Id);
            }
        }

        if (doomed.Count == 0)
        {
            return;
        }

        await database.BiologyAttempts.Where(attempt => doomed.Contains(attempt.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record BiologyAttemptSummary(
    Guid Id, DateTimeOffset? SubmittedAt, string StudentName, int Grade,
    int Score, int MaxScore, bool PendingReview, TimeSpan Duration, string? TestTitle,
    string LastNameKey, bool RetakeOpen, Guid? TestId);

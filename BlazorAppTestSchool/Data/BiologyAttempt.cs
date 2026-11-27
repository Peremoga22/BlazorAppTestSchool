using System.ComponentModel.DataAnnotations;

namespace BlazorAppTestSchool.Data;

public sealed class BiologyAttempt
{
    public Guid Id { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public int Grade { get; init; }
    public required string StudentName { get; init; }
    public required string ReportJson { get; init; }
}

/// <summary>A teacher's one-time permission for a surname in a grade to take the test again.</summary>
public sealed class BiologyRetakeApproval
{
    public Guid Id { get; set; }
    [MaxLength(80)]
    public string LastNameKey { get; set; } = "";
    public int Grade { get; set; }
    [MaxLength(120)]
    public string? Subject { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed record BiologyAttemptReport(
    string StudentName, int Grade, int Score, TimeSpan Duration, BiologyQuestionResult[] Questions)
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Patronymic { get; init; }
    public string? TestTitle { get; init; }
    public Guid? TestId { get; init; }

    public static string FormatDuration(TimeSpan duration)
    {
        var seconds = (int)Math.Ceiling(duration.TotalSeconds);
        return seconds < 60 ? $"{seconds} с" : $"{seconds / 60} хв {seconds % 60} с";
    }
}

public sealed record BiologyQuestionResult(
    string Question, string? Answer, int? Points, TimeSpan Duration, bool TimeExpired)
{
    public bool HasRootDiagram { get; init; }
}

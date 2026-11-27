namespace BlazorAppTestSchool.Data;

public sealed class BiologyAttempt
{
    public Guid Id { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public int Grade { get; init; }
    public required string StudentName { get; init; }
    public required string ReportJson { get; init; }
}

public sealed record BiologyAttemptReport(
    string StudentName, int Grade, int Score, TimeSpan Duration, BiologyQuestionResult[] Questions)
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Patronymic { get; init; }
    public string? TestTitle { get; init; }

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

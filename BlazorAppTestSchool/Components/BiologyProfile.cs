namespace BlazorAppTestSchool.Components;

public sealed record BiologyProfile(string LastName, string FirstName, string Patronymic, int Grade)
{
    public const string StorageKey = "biology-student-profile";

    public string Subject { get; init; } = "";

    public string Name => $"{LastName} {FirstName} {Patronymic}";
    public bool IsValid => IsNamePartValid(LastName) && IsNamePartValid(FirstName) &&
        IsNamePartValid(Patronymic) && IsNamePartValid(Subject) && Grade is >= 1 and <= 11;

    private static bool IsNamePartValid(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 80;
}

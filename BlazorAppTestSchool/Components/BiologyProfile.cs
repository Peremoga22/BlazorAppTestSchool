namespace BlazorAppTestSchool.Components;

public sealed record BiologyProfile(string LastName, string FirstName, string Patronymic, int Grade)
{
    public const string StorageKey = "biology-student-profile";

    public string Name => $"{LastName} {FirstName} {Patronymic}";
    public bool IsValid => IsNamePartValid(LastName) && IsNamePartValid(FirstName) &&
        IsNamePartValid(Patronymic) && Grade is 7 or 11;

    private static bool IsNamePartValid(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 80;
}

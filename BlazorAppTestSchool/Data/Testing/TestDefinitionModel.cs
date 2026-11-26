using System.Text.Json.Serialization;

namespace BlazorAppTestSchool.Data.Testing;

[JsonConverter(typeof(JsonStringEnumConverter<QuestionType>))]
public enum QuestionType
{
    Single,
    Multiple,
    Blank,
    Explanation,
    Match,
    Order,
    DiagramLabel
}

public sealed record TestDefinitionModel(
    string Title, string Subject, int? Grade, string? Instructions, TestQuestion[] Questions);

public sealed record TestQuestion(
    string Id, QuestionType Type, string Text, int? Seconds = null, TestImage? Image = null,
    string[]? Options = null, int[]? AnswerKey = null, string[]? Labels = null,
    int[]? MatchKey = null, int[]? OrderKey = null, string? BlankAnswer = null,
    TestHotspot[]? Hotspots = null)
{
    public int EffectiveSeconds => Seconds ?? Type switch
    {
        QuestionType.Multiple or QuestionType.Blank => 90,
        QuestionType.Match or QuestionType.Order or QuestionType.DiagramLabel => 120,
        QuestionType.Explanation => 180,
        _ => 60
    };
}

public sealed record TestImage(string Src, string Alt, string? Caption = null);

public sealed record TestHotspot(string Id, double X, double Y, string Label);

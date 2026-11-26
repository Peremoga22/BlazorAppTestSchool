using System.Text.Json;

namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestDefinitionValidationException(string[] errors)
    : Exception("Файл тесту не відповідає стандарту.")
{
    public string[] Errors { get; } = errors;
}

public static class TestDefinitionParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static TestDefinitionModel Parse(string json)
    {
        TestDefinitionModel? model;
        try
        {
            model = JsonSerializer.Deserialize<TestDefinitionModel>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new TestDefinitionValidationException([$"Файл не є коректним JSON: {exception.Message}"]);
        }

        if (model is null)
        {
            throw new TestDefinitionValidationException(["Файл порожній."]);
        }

        var errors = Validate(model);
        if (errors.Length > 0)
        {
            throw new TestDefinitionValidationException(errors);
        }

        return model;
    }

    public static string ToJson(TestDefinitionModel model) => JsonSerializer.Serialize(model, SerializerOptions);

    private static string[] Validate(TestDefinitionModel model)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            errors.Add("Поле \"title\" обов'язкове.");
        }

        if (string.IsNullOrWhiteSpace(model.Subject))
        {
            errors.Add("Поле \"subject\" обов'язкове.");
        }

        if (model.Grade is < 1 or > 11)
        {
            errors.Add("Поле \"grade\" має бути від 1 до 11.");
        }

        if (model.Questions is null || model.Questions.Length == 0)
        {
            errors.Add("Тест повинен містити хоча б одне питання (\"questions\").");
            return errors.ToArray();
        }

        var seenIds = new HashSet<string>();
        for (var i = 0; i < model.Questions.Length; i++)
        {
            var question = model.Questions[i];
            var label = $"Питання #{i + 1}";

            if (string.IsNullOrWhiteSpace(question.Id))
            {
                errors.Add($"{label}: поле \"id\" обов'язкове.");
            }
            else if (!seenIds.Add(question.Id))
            {
                errors.Add($"{label}: \"id\" = \"{question.Id}\" повторюється.");
            }

            if (string.IsNullOrWhiteSpace(question.Text))
            {
                errors.Add($"{label}: поле \"text\" обов'язкове.");
            }

            ValidateQuestion(label, question, errors);
        }

        return errors.ToArray();
    }

    private static void ValidateQuestion(string label, TestQuestion question, List<string> errors)
    {
        switch (question.Type)
        {
            case QuestionType.Single:
            case QuestionType.Multiple:
                if (question.Options is null || question.Options.Length < 2)
                {
                    errors.Add($"{label}: потрібно щонайменше 2 варіанти (\"options\").");
                    break;
                }

                if (question.AnswerKey is null || question.AnswerKey.Length == 0)
                {
                    errors.Add($"{label}: потрібна хоча б одна правильна відповідь (\"answerKey\").");
                    break;
                }

                if (question.Type == QuestionType.Single && question.AnswerKey.Length != 1)
                {
                    errors.Add($"{label}: для типу \"single\" \"answerKey\" повинен містити рівно один індекс.");
                }

                if (question.AnswerKey.Any(index => index < 0 || index >= question.Options.Length))
                {
                    errors.Add($"{label}: \"answerKey\" посилається на індекс поза межами \"options\".");
                }

                break;

            case QuestionType.Blank:
                if (string.IsNullOrWhiteSpace(question.BlankAnswer))
                {
                    errors.Add($"{label}: потрібне поле \"blankAnswer\".");
                }

                break;

            case QuestionType.Explanation:
                break;

            case QuestionType.Match:
                if (question.Options is null || question.Options.Length < 2)
                {
                    errors.Add($"{label}: потрібно щонайменше 2 варіанти (\"options\").");
                    break;
                }

                if (question.Labels is null || question.Labels.Length == 0)
                {
                    errors.Add($"{label}: потрібні підписи слотів (\"labels\").");
                    break;
                }

                if (question.MatchKey is null || question.MatchKey.Length != question.Labels.Length)
                {
                    errors.Add($"{label}: \"matchKey\" повинен мати стільки ж елементів, скільки \"labels\".");
                    break;
                }

                if (question.MatchKey.Any(index => index < 0 || index >= question.Options.Length))
                {
                    errors.Add($"{label}: \"matchKey\" посилається на індекс поза межами \"options\".");
                }

                break;

            case QuestionType.Order:
                if (question.Options is null || question.Options.Length < 2)
                {
                    errors.Add($"{label}: потрібно щонайменше 2 елементи (\"options\").");
                    break;
                }

                if (question.OrderKey is null || question.OrderKey.Length != question.Options.Length ||
                    !question.OrderKey.Order().SequenceEqual(Enumerable.Range(0, question.Options.Length)))
                {
                    errors.Add($"{label}: \"orderKey\" повинен бути перестановкою індексів 0..{question.Options.Length - 1}.");
                }

                break;

            case QuestionType.DiagramLabel:
                if (question.Image is null || string.IsNullOrWhiteSpace(question.Image.Src))
                {
                    errors.Add($"{label}: потрібне зображення (\"image.src\").");
                }

                if (question.Hotspots is null || question.Hotspots.Length == 0)
                {
                    errors.Add($"{label}: потрібна хоча б одна позначка (\"hotspots\").");
                    break;
                }

                if (question.Hotspots.Any(hotspot => hotspot.X is < 0 or > 100 || hotspot.Y is < 0 or > 100))
                {
                    errors.Add($"{label}: координати \"hotspots\" (x, y) повинні бути в межах 0–100.");
                }

                var wordBank = question.Options ?? [];
                var missing = question.Hotspots.Select(hotspot => hotspot.Label)
                    .Where(hotspotLabel => !wordBank.Contains(hotspotLabel))
                    .ToArray();
                if (missing.Length > 0)
                {
                    errors.Add($"{label}: підписи хотспотів [{string.Join(", ", missing)}] відсутні серед \"options\" (банку слів).");
                }

                break;
        }
    }
}

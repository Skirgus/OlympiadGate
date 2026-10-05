using System.Globalization;
using System.Text.Json;

namespace OlympiadGate.Core;

public static class ImportParser
{
    public static List<ImportRow> Parse(string json, ImportDefaults defaults)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new GateException("Пустой JSON.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new GateException("Файл не является JSON. " + ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            JsonElement problems;
            var fileDirection = defaults.Direction ?? "";
            int? fileGrade = defaults.Grade;
            var fileOlympiad = defaults.Olympiad ?? "";

            if (root.ValueKind == JsonValueKind.Array)
            {
                problems = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryProp(root, out var directionElement, "direction") && directionElement.ValueKind == JsonValueKind.String)
                    fileDirection = directionElement.GetString() ?? fileDirection;
                if (TryProp(root, out var gradeElement, "grade") && TryReadGrade(gradeElement, out var parsedGrade))
                    fileGrade = parsedGrade;
                if (TryProp(root, out var olympiadElement, "olympiad") && olympiadElement.ValueKind == JsonValueKind.String)
                    fileOlympiad = olympiadElement.GetString() ?? fileOlympiad;
                if (!TryProp(root, out problems, "problems") || problems.ValueKind != JsonValueKind.Array)
                    throw new GateException("В файле нет списка problems.");
            }
            else
            {
                throw new GateException("JSON должен быть объектом со списком problems.");
            }

            var rows = new List<ImportRow>();
            var index = 1;
            foreach (var item in problems.EnumerateArray())
            {
                rows.Add(ReadRow(item, index, fileDirection, fileGrade, fileOlympiad));
                index++;
            }

            return rows;
        }
    }

    private static ImportRow ReadRow(JsonElement item, int index, string fileDirection, int? fileGrade, string fileOlympiad)
    {
        var row = new ImportRow { Index = index };
        if (item.ValueKind != JsonValueKind.Object)
        {
            row.Status = "error";
            row.Message = "Строка должна быть объектом.";
            return row;
        }

        row.Statement = FirstText(item, "statement", "text", "problem");
        row.Answers = ReadAnswers(item);
        row.Direction = TextKey.Collapse(FirstText(item, "direction"));
        if (row.Direction.Length == 0)
            row.Direction = TextKey.Collapse(fileDirection);
        row.Olympiad = TextKey.Collapse(FirstText(item, "olympiad"));
        if (row.Olympiad.Length == 0)
            row.Olympiad = TextKey.Collapse(fileOlympiad);
        row.Note = FirstText(item, "note").Trim();
        row.Hint = FirstText(item, "hint").Trim();
        row.Solution = FirstText(item, "solution", "solutionComment").Trim();

        if (TryProp(item, out var gradeElement, "grade") && TryReadGrade(gradeElement, out var ownGrade))
            row.Grade = ownGrade;
        else if (fileGrade is int fallback)
            row.Grade = fallback;

        var error = Validate(row, fileGrade is null && row.Grade == 0);
        if (error != null)
        {
            row.Status = "error";
            row.Message = error;
        }

        return row;
    }

    private static string? Validate(ImportRow row, bool gradeMissing)
    {
        if (string.IsNullOrWhiteSpace(row.Statement))
            return "Пустое условие.";
        if (row.Answers.Count == 0)
            return "Нет ни одного ответа.";
        if (string.IsNullOrWhiteSpace(row.Direction))
            return "Не указано направление.";
        if (gradeMissing || row.Grade is < 1 or > 11)
            return "Класс должен быть от 1 до 11.";
        if (AnswerMatch.Matches(row.Hint, row.Answers))
            return "Подсказка совпадает с ответом.";
        return null;
    }

    private static List<string> ReadAnswers(JsonElement item)
    {
        if (!TryProp(item, out var element, "answers", "answer"))
            return [];

        var values = new List<string>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in element.EnumerateArray())
                AddAnswer(values, part);
        }
        else
        {
            AddAnswer(values, element);
        }

        return values;
    }

    private static void AddAnswer(List<string> values, JsonElement element)
    {
        var text = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(text))
            return;
        var trimmed = text.Trim();
        if (trimmed.Length > 0)
            values.Add(trimmed);
    }

    private static string FirstText(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProp(item, out var element, name) && element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString()?.Trim() ?? "";
                if (text.Length > 0)
                    return text;
            }
        }

        return "";
    }

    private static bool TryReadGrade(JsonElement element, out int grade)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out grade))
            return true;
        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out grade))
            return true;
        grade = 0;
        return false;
    }

    private static bool TryProp(JsonElement obj, out JsonElement value, params string[] names)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in obj.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }
}

using System.Text.Json;

namespace OlympiadGate.Core;

public static class ImportPrompt
{
    public sealed record Files(string Prompt, string ExampleJson);

    public static Files Build(string direction, int grade, string olympiad)
    {
        var cleanDirection = string.IsNullOrWhiteSpace(direction) ? "Математика" : direction.Trim();
        var cleanGrade = grade is >= 1 and <= 11 ? grade : 5;
        var cleanOlympiad = olympiad?.Trim() ?? "";
        var exampleObject = new Dictionary<string, object?>
        {
            ["direction"] = cleanDirection,
            ["grade"] = cleanGrade,
            ["problems"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["statement"] = "Полный текст условия первой задачи.",
                    ["answers"] = new[] { "основной ответ", "допустимый вариант" },
                    ["hint"] = "Короткая подсказка: с чего начать и какой приём применить. Без самого ответа.",
                    ["solution"] = "Как прийти к ответу: шаги для администратора. Ученик это поле не видит."
                }
            }
        };
        if (cleanOlympiad.Length > 0)
            exampleObject["olympiad"] = cleanOlympiad;

        var example = JsonSerializer.Serialize(exampleObject, new JsonSerializerOptions { WriteIndented = true });
        var olympiadRule = cleanOlympiad.Length == 0
            ? "olympiad можно не указывать"
            : $"olympiad — название олимпиады, строка «{cleanOlympiad}»";

        var prompt = $"""
            Подготовь JSON для загрузки в OlympiadGate.
            Верни только JSON без пояснений, в таком виде:

            {example}

            Правила:
            - direction — направление подготовки, строка «{cleanDirection}»
            - grade — целое число класса ({cleanGrade})
            - {olympiadRule}
            - statement — условие целиком, без решения и без ответа
            - answers — короткие ответы, которые ученик вводит. Если ответ один, всё равно массив из одной строки
            - hint — подсказка ученику. Подтолкни к способу решения и не пиши сам ответ, число из answers и готовую формулу с подставленным результатом. Поле можно опустить
            - solution — комментарий для администратора: способ решения и как прийти к ответу. Можно описать ход почти полностью. Ученик это поле не видит. Можно опустить
            - note — короткая пометка администратора, не решение. Можно опустить
            - дроби пиши как 1/2, десятичные через точку или запятую
            - не добавляй поля кроме direction, grade, olympiad, statement, answers, hint, solution и note
            - не включай решения и рассуждения в statement и hint
            - direction, grade и olympiad можно указать один раз на весь файл
            - замени пример задачами из приложенного задачника
            """;

        return new Files(prompt, example);
    }
}

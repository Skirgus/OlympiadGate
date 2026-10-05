using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace OlympiadGate.Core.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly string _directory;
    private readonly string _database;
    private DateTime _now = new(2026, 10, 5, 8, 0, 0);

    public CatalogTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "og-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = Path.Combine(_directory, "test.db");
    }

    [Fact]
    public void Answer_ignores_case_spaces_and_decimal_comma()
    {
        Assert.True(AnswerMatch.Matches("  1,5 ", ["1.5"]));
        Assert.True(AnswerMatch.Matches("Кислород", ["кислород"]));
        Assert.False(AnswerMatch.Matches("", ["кислород"]));
        Assert.False(AnswerMatch.Matches("азот", ["кислород", "кислород "]));
    }

    [Fact]
    public void Daily_goal_unlocks_until_midnight_and_does_not_repeat_solved_problems()
    {
        using var catalog = Open();
        catalog.SaveAccount(Account("S-1", "Ученик", "Математика", goal: 2));
        catalog.SaveProblem(Problem("2+2", ["4"], "Математика"));
        catalog.SaveProblem(Problem("3+3", ["6"], "Математика"));
        catalog.SaveProblem(Problem("4+4", ["8"], "Математика"));

        var task = catalog.NextProblem("S-1");
        Assert.NotNull(task);
        var wrong = catalog.Submit("S-1", task!.Id, "нет");
        Assert.False(wrong.Correct);
        Assert.Equal(0, wrong.SolvedToday);

        var firstResult = catalog.Submit("S-1", task.Id, AnswerOf(catalog, task.Id));
        Assert.True(firstResult.Correct);
        Assert.False(firstResult.Unlocked);
        Assert.NotNull(firstResult.Next);
        Assert.NotEqual(task.Id, firstResult.Next!.Id);

        var secondResult = catalog.Submit("S-1", firstResult.Next.Id, AnswerOf(catalog, firstResult.Next.Id));
        Assert.True(secondResult.Unlocked);
        Assert.Null(secondResult.Next);
        Assert.False(catalog.GetLockSnapshot("S-1").ShouldLock);

        _now = _now.AddDays(1);
        var nextDay = catalog.NextProblem("S-1");
        Assert.NotNull(nextDay);
        Assert.Equal(0, nextDay!.SolvedToday);
        Assert.NotEqual(task.Id, nextDay.Id);
        Assert.NotEqual(firstResult.Next.Id, nextDay.Id);
    }

    [Fact]
    public void Switching_direction_keeps_today_open_and_old_solved_problems()
    {
        using var catalog = Open();
        catalog.SaveAccount(Account("S-1", "Ученик", "Математика", goal: 1));
        catalog.SaveProblem(Problem("2+2", ["4"], "Математика"));
        catalog.SaveProblem(Problem("Сколько ног у паука?", ["8"], "Природоведение"));
        var math = catalog.NextProblem("S-1")!;
        Assert.True(catalog.Submit("S-1", math.Id, AnswerOf(catalog, math.Id)).Unlocked);

        catalog.SaveAccount(Account("S-1", "Ученик", "Природоведение", goal: 1));
        Assert.False(catalog.GetLockSnapshot("S-1").ShouldLock);

        _now = _now.AddDays(1);
        var nature = catalog.NextProblem("S-1");
        Assert.Equal("Природоведение", nature!.Direction);
        Assert.Contains("паука", nature.Statement);

        catalog.SaveAccount(Account("S-1", "Ученик", "Математика", goal: 1));
        Assert.Null(catalog.NextProblem("S-1"));
        Assert.Equal(0, catalog.GetLockSnapshot("S-1").Remaining);
    }

    [Fact]
    public void Clearing_today_returns_the_lock_and_keeps_older_solutions()
    {
        using var catalog = Open();
        catalog.SaveAccount(Account("S-1", "Ученик", "Математика", goal: 1));
        catalog.SaveAccount(Account("S-2", "Другой", "Математика", goal: 1));
        catalog.SaveProblem(Problem("2+2", ["4"], "Математика"));
        catalog.SaveProblem(Problem("3+3", ["6"], "Математика"));

        var first = catalog.NextProblem("S-1")!;
        Assert.True(catalog.Submit("S-1", first.Id, AnswerOf(catalog, first.Id)).Unlocked);

        _now = _now.AddDays(1);
        var other = catalog.NextProblem("S-2")!;
        Assert.True(catalog.Submit("S-2", other.Id, AnswerOf(catalog, other.Id)).Unlocked);
        var second = catalog.NextProblem("S-1")!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(catalog.Submit("S-1", second.Id, AnswerOf(catalog, second.Id)).Unlocked);

        Assert.Equal(1, catalog.ClearProgress("S-1", todayOnly: true));
        var snapshot = catalog.GetLockSnapshot("S-1");
        Assert.True(snapshot.ShouldLock);
        Assert.Equal(0, snapshot.SolvedToday);
        Assert.Equal(second.Id, catalog.NextProblem("S-1")!.Id);
        Assert.False(catalog.GetLockSnapshot("S-2").ShouldLock);

        Assert.Equal(1, catalog.ClearProgress("S-1", todayOnly: false));
        Assert.Equal(2, catalog.GetLockSnapshot("S-1").Remaining);
        Assert.Throws<GateException>(() => catalog.ClearProgress(" ", todayOnly: true));
    }

    [Fact]
    public void Same_text_in_another_direction_is_kept()
    {
        using var catalog = Open();
        catalog.SaveProblem(Problem("Общий текст", ["1"], "Математика"));
        var other = catalog.SaveProblem(Problem("Общий текст", ["2"], "Природоведение"));
        Assert.True(other > 0);
        Assert.Throws<GateException>(() => catalog.SaveProblem(Problem("Общий текст", ["3"], "математика")));
    }

    [Fact]
    public void Same_direction_spelling_is_reused()
    {
        using var catalog = Open();
        catalog.SaveProblem(Problem("Первая", ["1"], "Математика"));
        catalog.SaveProblem(Problem("Вторая", ["2"], "математика"));
        Assert.Equal(["Математика"], catalog.ListDirections());
        Assert.All(catalog.ListProblems(), problem => Assert.Equal("Математика", problem.Direction));
    }

    [Fact]
    public void Import_accepts_aliases_skips_duplicates_and_updates_answers()
    {
        using var catalog = Open();
        catalog.SaveProblem(Problem("Уже была", ["1"], "Математика"));
        const string json = """
            {
              "direction": "математика",
              "grade": 5,
              "problems": [
                { "text": "Уже была", "answer": "9" },
                { "problem": "Новая", "answers": ["7", "семь"] },
                { "statement": "", "answers": ["0"] }
              ]
            }
            """;

        var preview = catalog.PreviewImport(json, new ImportDefaults());
        Assert.Equal(1, preview.Ready);
        Assert.Equal(1, preview.Duplicates);
        Assert.Equal(1, preview.Errors);

        var updated = catalog.CommitImport(json, new ImportDefaults(), updateDuplicates: true);
        Assert.Equal(1, updated.Added);
        Assert.Equal(1, updated.Updated);
        Assert.Equal(1, updated.Errors);
        var old = catalog.ListProblems().Single(problem => problem.Statement == "Уже была");
        Assert.Equal(["9"], old.Answers);
    }

    [Fact]
    public void Student_task_includes_the_hint_and_hides_the_solution()
    {
        using var catalog = Open();
        catalog.SaveAccount(Account("S-1", "Ученик", "Математика", goal: 1));
        var id = catalog.SaveProblem(new SaveProblemRequest
        {
            Statement = "2+2",
            Answers = ["4"],
            Direction = "Математика",
            Grade = 5,
            Hint = "Сложи два одинаковых числа.",
            Solution = "Сложить 2 и 2."
        });

        var task = catalog.NextProblem("S-1");
        Assert.Equal(id, task!.Id);
        Assert.Equal("Сложи два одинаковых числа.", task.Hint);
        var json = JsonSerializer.Serialize(task, GateJson.Options);
        Assert.DoesNotContain("Сложить 2 и 2", json);
        Assert.DoesNotContain("solution", json, StringComparison.OrdinalIgnoreCase);

        var stored = catalog.ListProblems().Single(problem => problem.Id == id);
        Assert.Equal("Сложить 2 и 2.", stored.Solution);

        var leaked = Problem("3+3", ["6"], "Математика");
        leaked.Hint = "6";
        var error = Assert.Throws<GateException>(() => catalog.SaveProblem(leaked));
        Assert.Contains("Подсказка", error.Message);
    }

    [Fact]
    public void Import_stores_hint_and_solution_and_rejects_a_hint_that_is_the_answer()
    {
        using var catalog = Open();
        catalog.SaveProblem(Problem("Уже была", ["1"], "Математика"));
        const string json = """
            {
              "direction": "Математика",
              "grade": 5,
              "problems": [
                {
                  "statement": "Сколько будет 2+2?",
                  "answers": ["4"],
                  "hint": "Сложи одинаковые слагаемые.",
                  "solution": "Сложить 2 и 2. Получится 4."
                },
                {
                  "statement": "Сколько будет 3+3?",
                  "answers": ["6"],
                  "hint": "6"
                },
                {
                  "text": "Уже была",
                  "answer": "9",
                  "hint": "Вспомни прошлый ответ.",
                  "solutionComment": "Оставить число 9."
                }
              ]
            }
            """;

        var preview = catalog.PreviewImport(json, new ImportDefaults());
        Assert.Equal(1, preview.Ready);
        Assert.Equal(1, preview.Duplicates);
        Assert.Equal(1, preview.Errors);
        Assert.Equal("есть", preview.Rows[0].HintMark);
        Assert.Equal("есть", preview.Rows[0].SolutionMark);
        Assert.Contains("Подсказка", preview.Rows[1].Message);

        var updated = catalog.CommitImport(json, new ImportDefaults(), updateDuplicates: true);
        Assert.Equal(1, updated.Added);
        Assert.Equal(1, updated.Updated);
        var created = catalog.ListProblems().Single(problem => problem.Statement == "Сколько будет 2+2?");
        Assert.Equal("Сложи одинаковые слагаемые.", created.Hint);
        Assert.Equal("Сложить 2 и 2. Получится 4.", created.Solution);
        var old = catalog.ListProblems().Single(problem => problem.Statement == "Уже была");
        Assert.Equal(["9"], old.Answers);
        Assert.Equal("Вспомни прошлый ответ.", old.Hint);
        Assert.Equal("Оставить число 9.", old.Solution);
    }

    [Fact]
    public void Older_bank_gains_hint_and_solution_columns()
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _database }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Problems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DirectionKey TEXT NOT NULL,
                    Direction TEXT NOT NULL,
                    Grade INTEGER NOT NULL,
                    OlympiadKey TEXT NOT NULL,
                    Olympiad TEXT NOT NULL,
                    Statement TEXT NOT NULL,
                    StatementKey TEXT NOT NULL,
                    AnswersJson TEXT NOT NULL,
                    Note TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                INSERT INTO Problems(DirectionKey, Direction, Grade, OlympiadKey, Olympiad, Statement, StatementKey, AnswersJson, Note, CreatedAt)
                VALUES('математика', 'Математика', 5, '', '', 'Старая', 'старая', '["1"]', '', '2026-10-05T08:00:00');
                """;
            command.ExecuteNonQuery();
        }

        using var catalog = Open();
        var problem = catalog.ListProblems().Single();
        Assert.Equal("Старая", problem.Statement);
        Assert.Equal("", problem.Hint);
        Assert.Equal("", problem.Solution);
        catalog.SaveProblem(new SaveProblemRequest
        {
            Id = problem.Id,
            Statement = problem.Statement,
            Answers = problem.Answers,
            Direction = problem.Direction,
            Grade = problem.Grade,
            Hint = "Посмотри на условие ещё раз.",
            Solution = "Ответ уже был единицей."
        });
        var saved = catalog.ListProblems().Single();
        Assert.Equal("Посмотри на условие ещё раз.", saved.Hint);
        Assert.Equal("Ответ уже был единицей.", saved.Solution);
    }

    [Fact]
    public void Invalid_json_is_rejected()
    {
        using var catalog = Open();
        var error = Assert.Throws<GateException>(() => catalog.PreviewImport("{", new ImportDefaults()));
        Assert.Contains("JSON", error.Message);
    }

    [Fact]
    public void Low_stock_uses_two_daily_goals_by_default()
    {
        Assert.Equal(StockLevel.Empty, StockRules.Level(2, 3, 0));
        Assert.Equal(StockLevel.Low, StockRules.Level(5, 3, 0));
        Assert.Equal(StockLevel.Ok, StockRules.Level(6, 3, 0));
    }

    private static string AnswerOf(GateCatalog catalog, long id) =>
        catalog.ListProblems().Single(problem => problem.Id == id).Answers[0];

    private GateCatalog Open() => new(_database, () => _now);

    private static AccountSettings Account(string sid, string name, string direction, int goal) => new()
    {
        Sid = sid,
        Username = name,
        LockEnabled = true,
        Grade = 5,
        Direction = direction,
        DailyGoal = goal
    };

    private static SaveProblemRequest Problem(string statement, IEnumerable<string> answers, string direction) => new()
    {
        Statement = statement,
        Answers = answers.ToList(),
        Direction = direction,
        Grade = 5
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

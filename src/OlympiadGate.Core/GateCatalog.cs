using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OlympiadGate.Core;

public sealed class GateCatalog : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _issued = new(StringComparer.OrdinalIgnoreCase);

    public GateCatalog(string databasePath, Func<DateTime>? now = null)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _now = now ?? (() => DateTime.Now);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode = WAL;");
        Execute("PRAGMA foreign_keys = ON;");
        EnsureSchema();
    }

    public void Dispose() => _connection.Dispose();

    public List<AccountSettings> ListAccounts()
    {
        lock (_gate)
            return ListAccountsCore();
    }

    public void SaveAccount(AccountSettings account)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(account.Sid))
                throw new GateException("Не выбрана учётная запись.");
            if (account.Grade is < 1 or > 11)
                throw new GateException("Класс должен быть от 1 до 11.");
            if (account.DailyGoal is < 1 or > 50)
                throw new GateException("Число задач в день должно быть от 1 до 50.");
            if (account.WarnBelow is < 0 or > 500)
                throw new GateException("Порог предупреждения должен быть от 0 до 500.");

            var direction = TextKey.Collapse(account.Direction);
            var olympiad = TextKey.Collapse(account.Olympiad);
            if (account.LockEnabled && direction.Length == 0)
                throw new GateException("Укажите направление.");
            if (direction.Length > 0)
                direction = Canonical("direction", direction);
            if (olympiad.Length > 0)
                olympiad = Canonical("olympiad", olympiad);

            Execute(
                """
                INSERT INTO Accounts(Sid, Username, LockEnabled, Grade, Direction, Olympiad, DailyGoal, WarnBelow)
                VALUES($sid, $username, $lock, $grade, $direction, $olympiad, $goal, $warn)
                ON CONFLICT(Sid) DO UPDATE SET
                    Username = excluded.Username,
                    LockEnabled = excluded.LockEnabled,
                    Grade = excluded.Grade,
                    Direction = excluded.Direction,
                    Olympiad = excluded.Olympiad,
                    DailyGoal = excluded.DailyGoal,
                    WarnBelow = excluded.WarnBelow
                """,
                ("$sid", account.Sid.Trim()),
                ("$username", string.IsNullOrWhiteSpace(account.Username) ? account.Sid.Trim() : account.Username.Trim()),
                ("$lock", account.LockEnabled ? 1 : 0),
                ("$grade", account.Grade),
                ("$direction", direction),
                ("$olympiad", olympiad),
                ("$goal", account.DailyGoal),
                ("$warn", account.WarnBelow));
        }
    }

    public List<string> ListDirections()
    {
        lock (_gate)
        {
            var list = new List<string>();
            using var command = Command("SELECT Display FROM Names WHERE Kind = 'direction' ORDER BY Display");
            using var reader = command.ExecuteReader();
            while (reader.Read())
                list.Add(reader.GetString(0));
            return list;
        }
    }

    public List<ProblemRecord> ListProblems()
    {
        lock (_gate)
            return ListProblemsCore();
    }

    public long SaveProblem(SaveProblemRequest request)
    {
        lock (_gate)
        {
            var statement = request.Statement?.Trim() ?? "";
            var answers = CleanAnswers(request.Answers);
            var direction = TextKey.Collapse(request.Direction);
            var olympiad = TextKey.Collapse(request.Olympiad);
            var note = request.Note?.Trim() ?? "";
            var hint = request.Hint?.Trim() ?? "";
            var solution = request.Solution?.Trim() ?? "";
            ValidateProblem(statement, answers, direction, request.Grade, hint);
            direction = Canonical("direction", direction);
            if (olympiad.Length > 0)
                olympiad = Canonical("olympiad", olympiad);

            var directionKey = TextKey.NameKey(direction);
            var olympiadKey = TextKey.NameKey(olympiad);
            var statementKey = TextKey.StatementKey(statement);
            var existing = FindProblemId(directionKey, request.Grade, olympiadKey, statementKey);
            if (existing is long other && other != request.Id)
                throw new GateException("Такая задача уже есть в этом направлении.");

            var answersJson = JsonSerializer.Serialize(answers, GateJson.Options);
            if (request.Id > 0)
            {
                var updated = Execute(
                    """
                    UPDATE Problems
                    SET DirectionKey = $directionKey, Direction = $direction, Grade = $grade,
                        OlympiadKey = $olympiadKey, Olympiad = $olympiad, Statement = $statement,
                        StatementKey = $statementKey, AnswersJson = $answers, Note = $note,
                        Hint = $hint, Solution = $solution
                    WHERE Id = $id
                    """,
                    ("$directionKey", directionKey),
                    ("$direction", direction),
                    ("$grade", request.Grade),
                    ("$olympiadKey", olympiadKey),
                    ("$olympiad", olympiad),
                    ("$statement", statement),
                    ("$statementKey", statementKey),
                    ("$answers", answersJson),
                    ("$note", note),
                    ("$hint", hint),
                    ("$solution", solution),
                    ("$id", request.Id));
                if (updated == 0)
                    throw new GateException("Задача не найдена.");
                return request.Id;
            }

            Execute(
                """
                INSERT INTO Problems(DirectionKey, Direction, Grade, OlympiadKey, Olympiad, Statement, StatementKey, AnswersJson, Note, Hint, Solution, CreatedAt)
                VALUES($directionKey, $direction, $grade, $olympiadKey, $olympiad, $statement, $statementKey, $answers, $note, $hint, $solution, $created)
                """,
                ("$directionKey", directionKey),
                ("$direction", direction),
                ("$grade", request.Grade),
                ("$olympiadKey", olympiadKey),
                ("$olympiad", olympiad),
                ("$statement", statement),
                ("$statementKey", statementKey),
                ("$answers", answersJson),
                ("$note", note),
                ("$hint", hint),
                ("$solution", solution),
                ("$created", _now().ToString("o", CultureInfo.InvariantCulture)));
            return LastInsertId();
        }
    }

    public void DeleteProblem(long id)
    {
        lock (_gate)
        {
            Execute("DELETE FROM Solved WHERE ProblemId = $id", ("$id", id));
            Execute("DELETE FROM Problems WHERE Id = $id", ("$id", id));
        }
    }

    public DirectionAdvice Advise(string sid, string direction, int grade, string olympiad)
    {
        lock (_gate)
        {
            var directionKey = TextKey.NameKey(direction);
            if (directionKey.Length == 0)
                return new DirectionAdvice { DirectionEmpty = true };

            var inDirection = CountRemaining(sid, directionKey, grade, "");
            var olympiadKey = TextKey.NameKey(olympiad);
            var withOlympiad = olympiadKey.Length == 0
                ? inDirection
                : CountRemaining(sid, directionKey, grade, olympiadKey);
            return new DirectionAdvice
            {
                RemainingInDirection = inDirection,
                RemainingWithOlympiad = withOlympiad,
                SuggestClearOlympiad = olympiadKey.Length > 0 && withOlympiad == 0 && inDirection > 0
            };
        }
    }

    public List<StockWarning> Warnings()
    {
        lock (_gate)
        {
            var warnings = new List<StockWarning>();
            foreach (var account in ListAccountsCore())
            {
                if (!account.LockEnabled)
                    continue;
                FillStock(account);
                if (account.Stock == nameof(StockLevel.Ok))
                    continue;
                warnings.Add(new StockWarning
                {
                    Sid = account.Sid,
                    Username = account.Username,
                    Direction = account.Direction,
                    Remaining = account.Remaining,
                    DailyGoal = account.DailyGoal,
                    Stock = account.Stock
                });
            }

            return warnings;
        }
    }

    public AccountSettings Decorate(AccountSettings account)
    {
        lock (_gate)
        {
            FillStock(account);
            return account;
        }
    }

    public LockSnapshot GetLockSnapshot(string sid)
    {
        lock (_gate)
            return SnapshotCore(sid, writeUnlock: true);
    }

    public TaskView? NextProblem(string sid)
    {
        lock (_gate)
        {
            var account = FindAccount(sid);
            var snapshot = SnapshotCore(sid, writeUnlock: true);
            if (!snapshot.ShouldLock || account == null)
            {
                _issued.Remove(sid);
                return null;
            }

            return IssueNext(sid, account);
        }
    }

    public SubmitResult Submit(string sid, long problemId, string answer)
    {
        lock (_gate)
        {
            var account = FindAccount(sid);
            if (!_issued.TryGetValue(sid, out var issued) || issued != problemId)
                return Progress(sid, account, correct: false, missing: true, unlocked: false, next: null);

            var problem = FindProblem(problemId);
            if (problem == null)
            {
                _issued.Remove(sid);
                return Progress(sid, account, correct: false, missing: true, unlocked: false, next: null);
            }

            if (!AnswerMatch.Matches(answer, problem.Answers))
                return Progress(sid, account, correct: false, missing: false, unlocked: false, next: null);

            Execute(
                "INSERT OR IGNORE INTO Solved(AccountSid, ProblemId, SolvedOn) VALUES($sid, $id, $day)",
                ("$sid", sid),
                ("$id", problemId),
                ("$day", TodayKey()));
            _issued.Remove(sid);

            var today = CountToday(sid);
            var goal = account?.DailyGoal ?? 0;
            var unlocked = HasUnlock(sid) || (goal > 0 && today >= goal);
            if (unlocked)
                EnsureUnlock(sid);

            TaskView? next = null;
            if (!unlocked && account != null)
                next = IssueNext(sid, account);
            return Progress(sid, account, correct: true, missing: false, unlocked: unlocked, next: next);
        }
    }

    public void UnlockForToday(string sid)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(sid))
                throw new GateException("Не выбрана учётная запись.");
            EnsureUnlock(sid);
        }
    }

    public int ClearProgress(string sid, bool todayOnly)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(sid))
                throw new GateException("Не выбрана учётная запись.");

            var removed = todayOnly
                ? Execute(
                    "DELETE FROM Solved WHERE AccountSid = $sid AND SolvedOn = $day",
                    ("$sid", sid),
                    ("$day", TodayKey()))
                : Execute("DELETE FROM Solved WHERE AccountSid = $sid", ("$sid", sid));
            if (todayOnly)
            {
                Execute(
                    "DELETE FROM DailyUnlock WHERE AccountSid = $sid AND Day = $day",
                    ("$sid", sid),
                    ("$day", TodayKey()));
            }
            else
            {
                Execute("DELETE FROM DailyUnlock WHERE AccountSid = $sid", ("$sid", sid));
            }

            _issued.Remove(sid);
            return removed;
        }
    }

    public ImportPreview PreviewImport(string json, ImportDefaults defaults)
    {
        lock (_gate)
            return BuildPreview(ImportParser.Parse(json, defaults));
    }

    public ImportCommitResult CommitImport(string json, ImportDefaults defaults, bool updateDuplicates)
    {
        lock (_gate)
        {
            var preview = BuildPreview(ImportParser.Parse(json, defaults));
            var result = new ImportCommitResult();
            using var transaction = _connection.BeginTransaction();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in preview.Rows)
            {
                if (row.Status == "error")
                {
                    result.Errors++;
                    continue;
                }

                var key = DedupKey(row.Direction, row.Grade, row.Olympiad, row.Statement);
                if (!seen.Add(key))
                {
                    result.Skipped++;
                    continue;
                }

                var direction = Canonical("direction", row.Direction);
                var olympiad = row.Olympiad.Length == 0 ? "" : Canonical("olympiad", row.Olympiad);
                var directionKey = TextKey.NameKey(direction);
                var olympiadKey = TextKey.NameKey(olympiad);
                var statementKey = TextKey.StatementKey(row.Statement);
                var existing = FindProblemId(directionKey, row.Grade, olympiadKey, statementKey);
                var answersJson = JsonSerializer.Serialize(row.Answers, GateJson.Options);
                if (existing is long id)
                {
                    if (!updateDuplicates)
                    {
                        result.Skipped++;
                        continue;
                    }

                    Execute(
                        "UPDATE Problems SET AnswersJson = $answers, Note = $note, Hint = $hint, Solution = $solution WHERE Id = $id",
                        ("$answers", answersJson),
                        ("$note", row.Note),
                        ("$hint", row.Hint),
                        ("$solution", row.Solution),
                        ("$id", id));
                    result.Updated++;
                    continue;
                }

                Execute(
                    """
                    INSERT INTO Problems(DirectionKey, Direction, Grade, OlympiadKey, Olympiad, Statement, StatementKey, AnswersJson, Note, Hint, Solution, CreatedAt)
                    VALUES($directionKey, $direction, $grade, $olympiadKey, $olympiad, $statement, $statementKey, $answers, $note, $hint, $solution, $created)
                    """,
                    ("$directionKey", directionKey),
                    ("$direction", direction),
                    ("$grade", row.Grade),
                    ("$olympiadKey", olympiadKey),
                    ("$olympiad", olympiad),
                    ("$statement", row.Statement.Trim()),
                    ("$statementKey", statementKey),
                    ("$answers", answersJson),
                    ("$note", row.Note),
                    ("$hint", row.Hint),
                    ("$solution", row.Solution),
                    ("$created", _now().ToString("o", CultureInfo.InvariantCulture)));
                result.AddedIds.Add(LastInsertId());
                result.Added++;
            }

            transaction.Commit();
            return result;
        }
    }

    private ImportPreview BuildPreview(List<ImportRow> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Status == "error")
                continue;

            var key = DedupKey(row.Direction, row.Grade, row.Olympiad, row.Statement);
            if (!seen.Add(key))
            {
                row.Status = "duplicate";
                row.Message = "Повтор в этом файле.";
                continue;
            }

            var existing = FindProblemId(
                TextKey.NameKey(row.Direction),
                row.Grade,
                TextKey.NameKey(row.Olympiad),
                TextKey.StatementKey(row.Statement));
            if (existing != null)
            {
                row.Status = "duplicate";
                row.Message = "Уже есть в банке.";
            }
        }

        return new ImportPreview
        {
            Rows = rows,
            Ready = rows.Count(row => row.Status == "ready"),
            Duplicates = rows.Count(row => row.Status == "duplicate"),
            Errors = rows.Count(row => row.Status == "error")
        };
    }

    private LockSnapshot SnapshotCore(string sid, bool writeUnlock)
    {
        var account = FindAccount(sid);
        if (account == null || !account.LockEnabled)
        {
            return new LockSnapshot
            {
                Direction = account?.Direction ?? "",
                DailyGoal = account?.DailyGoal ?? 0,
                SolvedToday = string.IsNullOrWhiteSpace(sid) ? 0 : CountToday(sid)
            };
        }

        var today = CountToday(sid);
        var unlocked = HasUnlock(sid) || today >= account.DailyGoal;
        if (unlocked && writeUnlock)
            EnsureUnlock(sid);
        var remaining = CountRemaining(sid, TextKey.NameKey(account.Direction), account.Grade, TextKey.NameKey(account.Olympiad));
        return new LockSnapshot
        {
            ShouldLock = !unlocked,
            UnlockedToday = unlocked,
            SolvedToday = today,
            DailyGoal = account.DailyGoal,
            Direction = account.Direction,
            Remaining = remaining,
            Stock = StockRules.Level(remaining, account.DailyGoal, account.WarnBelow).ToString()
        };
    }

    private TaskView? IssueNext(string sid, AccountSettings account)
    {
        using var command = Command(
            """
            SELECT Id, Statement, Hint FROM Problems
            WHERE DirectionKey = $direction AND Grade = $grade
              AND ($olympiad = '' OR OlympiadKey = $olympiad)
              AND NOT EXISTS (
                  SELECT 1 FROM Solved WHERE Solved.ProblemId = Problems.Id AND Solved.AccountSid = $sid)
            ORDER BY RANDOM()
            LIMIT 1
            """);
        Add(command, "$direction", TextKey.NameKey(account.Direction));
        Add(command, "$grade", account.Grade);
        Add(command, "$olympiad", TextKey.NameKey(account.Olympiad));
        Add(command, "$sid", sid);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            _issued.Remove(sid);
            return null;
        }

        var id = reader.GetInt64(0);
        _issued[sid] = id;
        return new TaskView
        {
            Id = id,
            Statement = reader.GetString(1),
            Hint = reader.GetString(2),
            Direction = account.Direction,
            SolvedToday = CountToday(sid),
            DailyGoal = account.DailyGoal
        };
    }

    private SubmitResult Progress(string sid, AccountSettings? account, bool correct, bool missing, bool unlocked, TaskView? next)
    {
        var today = string.IsNullOrWhiteSpace(sid) ? 0 : CountToday(sid);
        return new SubmitResult
        {
            Correct = correct,
            ProblemMissing = missing,
            Unlocked = unlocked || HasUnlock(sid),
            SolvedToday = today,
            DailyGoal = account?.DailyGoal ?? 0,
            Next = next
        };
    }

    private void FillStock(AccountSettings account)
    {
        if (string.IsNullOrWhiteSpace(account.Direction))
        {
            account.Remaining = 0;
            account.Stock = account.LockEnabled ? nameof(StockLevel.Empty) : nameof(StockLevel.Ok);
            return;
        }

        account.Remaining = CountRemaining(
            account.Sid,
            TextKey.NameKey(account.Direction),
            account.Grade,
            TextKey.NameKey(account.Olympiad));
        account.Stock = StockRules.Level(account.Remaining, account.DailyGoal, account.WarnBelow).ToString();
    }

    private int CountRemaining(string sid, string directionKey, int grade, string olympiadKey)
    {
        using var command = Command(
            """
            SELECT COUNT(*) FROM Problems
            WHERE DirectionKey = $direction AND Grade = $grade
              AND ($olympiad = '' OR OlympiadKey = $olympiad)
              AND NOT EXISTS (
                  SELECT 1 FROM Solved WHERE Solved.ProblemId = Problems.Id AND Solved.AccountSid = $sid)
            """);
        Add(command, "$direction", directionKey);
        Add(command, "$grade", grade);
        Add(command, "$olympiad", olympiadKey);
        Add(command, "$sid", sid ?? "");
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private int CountToday(string sid)
    {
        using var command = Command("SELECT COUNT(*) FROM Solved WHERE AccountSid = $sid AND SolvedOn = $day");
        Add(command, "$sid", sid);
        Add(command, "$day", TodayKey());
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private bool HasUnlock(string sid)
    {
        using var command = Command("SELECT 1 FROM DailyUnlock WHERE AccountSid = $sid AND Day = $day");
        Add(command, "$sid", sid);
        Add(command, "$day", TodayKey());
        return command.ExecuteScalar() != null;
    }

    private void EnsureUnlock(string sid)
    {
        Execute(
            "INSERT OR IGNORE INTO DailyUnlock(AccountSid, Day) VALUES($sid, $day)",
            ("$sid", sid),
            ("$day", TodayKey()));
    }

    private List<AccountSettings> ListAccountsCore()
    {
        var list = new List<AccountSettings>();
        using var command = Command("SELECT Sid, Username, LockEnabled, Grade, Direction, Olympiad, DailyGoal, WarnBelow FROM Accounts ORDER BY Username");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AccountSettings
            {
                Sid = reader.GetString(0),
                Username = reader.GetString(1),
                LockEnabled = reader.GetInt32(2) != 0,
                Grade = reader.GetInt32(3),
                Direction = reader.GetString(4),
                Olympiad = reader.GetString(5),
                DailyGoal = reader.GetInt32(6),
                WarnBelow = reader.GetInt32(7)
            });
        }

        return list;
    }

    private AccountSettings? FindAccount(string sid)
    {
        using var command = Command("SELECT Sid, Username, LockEnabled, Grade, Direction, Olympiad, DailyGoal, WarnBelow FROM Accounts WHERE Sid = $sid");
        Add(command, "$sid", sid);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new AccountSettings
        {
            Sid = reader.GetString(0),
            Username = reader.GetString(1),
            LockEnabled = reader.GetInt32(2) != 0,
            Grade = reader.GetInt32(3),
            Direction = reader.GetString(4),
            Olympiad = reader.GetString(5),
            DailyGoal = reader.GetInt32(6),
            WarnBelow = reader.GetInt32(7)
        };
    }

    private List<ProblemRecord> ListProblemsCore()
    {
        var list = new List<ProblemRecord>();
        using var command = Command("SELECT Id, Statement, AnswersJson, Direction, Grade, Olympiad, Note, Hint, Solution FROM Problems ORDER BY Id DESC");
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(ReadProblem(reader));
        return list;
    }

    private ProblemRecord? FindProblem(long id)
    {
        using var command = Command("SELECT Id, Statement, AnswersJson, Direction, Grade, Olympiad, Note, Hint, Solution FROM Problems WHERE Id = $id");
        Add(command, "$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProblem(reader) : null;
    }

    private static ProblemRecord ReadProblem(SqliteDataReader reader)
    {
        var answers = JsonSerializer.Deserialize<List<string>>(reader.GetString(2), GateJson.Options) ?? [];
        return new ProblemRecord
        {
            Id = reader.GetInt64(0),
            Statement = reader.GetString(1),
            Answers = answers,
            Direction = reader.GetString(3),
            Grade = reader.GetInt32(4),
            Olympiad = reader.GetString(5),
            Note = reader.GetString(6),
            Hint = reader.GetString(7),
            Solution = reader.GetString(8)
        };
    }

    private long? FindProblemId(string directionKey, int grade, string olympiadKey, string statementKey)
    {
        using var command = Command(
            """
            SELECT Id FROM Problems
            WHERE DirectionKey = $direction AND Grade = $grade AND OlympiadKey = $olympiad AND StatementKey = $statement
            """);
        Add(command, "$direction", directionKey);
        Add(command, "$grade", grade);
        Add(command, "$olympiad", olympiadKey);
        Add(command, "$statement", statementKey);
        var value = command.ExecuteScalar();
        return value == null || value is DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private string Canonical(string kind, string display)
    {
        var collapsed = TextKey.Collapse(display);
        var key = collapsed.ToLowerInvariant();
        using var select = Command("SELECT Display FROM Names WHERE Kind = $kind AND NameKey = $key");
        Add(select, "$kind", kind);
        Add(select, "$key", key);
        var existing = select.ExecuteScalar() as string;
        if (!string.IsNullOrEmpty(existing))
            return existing;

        Execute(
            "INSERT INTO Names(Kind, NameKey, Display) VALUES($kind, $key, $display)",
            ("$kind", kind),
            ("$key", key),
            ("$display", collapsed));
        return collapsed;
    }

    private string TodayKey() => _now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private long LastInsertId()
    {
        using var command = Command("SELECT last_insert_rowid()");
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<string> CleanAnswers(IEnumerable<string>? answers)
    {
        return (answers ?? [])
            .Select(answer => answer.Trim())
            .Where(answer => answer.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateProblem(string statement, List<string> answers, string direction, int grade, string hint)
    {
        if (statement.Length == 0)
            throw new GateException("Введите условие.");
        if (answers.Count == 0)
            throw new GateException("Укажите хотя бы один ответ.");
        if (direction.Length == 0)
            throw new GateException("Укажите направление.");
        if (grade is < 1 or > 11)
            throw new GateException("Класс должен быть от 1 до 11.");
        if (AnswerMatch.Matches(hint, answers))
            throw new GateException("Подсказка не должна совпадать с ответом.");
    }

    private static string DedupKey(string direction, int grade, string olympiad, string statement) =>
        TextKey.NameKey(direction) + "|" + grade.ToString(CultureInfo.InvariantCulture) + "|" + TextKey.NameKey(olympiad) + "|" + TextKey.StatementKey(statement);

    private void EnsureSchema()
    {
        Execute(
            """
            CREATE TABLE IF NOT EXISTS Names (
                Kind TEXT NOT NULL,
                NameKey TEXT NOT NULL,
                Display TEXT NOT NULL,
                PRIMARY KEY (Kind, NameKey)
            );

            CREATE TABLE IF NOT EXISTS Accounts (
                Sid TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                LockEnabled INTEGER NOT NULL,
                Grade INTEGER NOT NULL,
                Direction TEXT NOT NULL,
                Olympiad TEXT NOT NULL,
                DailyGoal INTEGER NOT NULL,
                WarnBelow INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Problems (
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
                Hint TEXT NOT NULL DEFAULT '',
                Solution TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Problems_Dedup
            ON Problems(DirectionKey, Grade, OlympiadKey, StatementKey);

            CREATE TABLE IF NOT EXISTS Solved (
                AccountSid TEXT NOT NULL,
                ProblemId INTEGER NOT NULL,
                SolvedOn TEXT NOT NULL,
                PRIMARY KEY (AccountSid, ProblemId)
            );

            CREATE INDEX IF NOT EXISTS IX_Solved_Day ON Solved(AccountSid, SolvedOn);

            CREATE TABLE IF NOT EXISTS DailyUnlock (
                AccountSid TEXT NOT NULL,
                Day TEXT NOT NULL,
                PRIMARY KEY (AccountSid, Day)
            );
            """);
        EnsureColumn("Problems", "Hint", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("Problems", "Solution", "TEXT NOT NULL DEFAULT ''");
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        using var command = Command($"PRAGMA table_info({table})");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql);
        foreach (var (name, value) in parameters)
            Add(command, name, value);
        return command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void Add(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}

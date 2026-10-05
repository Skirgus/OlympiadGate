namespace OlympiadGate.Core;

public sealed class AccountSettings
{
    public string Sid { get; set; } = "";
    public string Username { get; set; } = "";
    public bool LockEnabled { get; set; }
    public int Grade { get; set; } = 5;
    public string Direction { get; set; } = "";
    public string Olympiad { get; set; } = "";
    public int DailyGoal { get; set; } = 3;
    public int WarnBelow { get; set; }
    public bool IsAdministrator { get; set; }
    public int Remaining { get; set; }
    public string Stock { get; set; } = nameof(StockLevel.Ok);
}

public sealed class ProblemRecord
{
    public long Id { get; set; }
    public string Statement { get; set; } = "";
    public List<string> Answers { get; set; } = [];
    public string Direction { get; set; } = "";
    public int Grade { get; set; }
    public string Olympiad { get; set; } = "";
    public string Note { get; set; } = "";

    public string AnswersText => string.Join(Environment.NewLine, Answers);

    public string StatementPreview
    {
        get
        {
            var oneLine = TextKey.Collapse(Statement);
            return oneLine.Length <= 90 ? oneLine : oneLine[..90] + "…";
        }
    }
}

public sealed class TaskView
{
    public long Id { get; set; }
    public string Statement { get; set; } = "";
    public string Direction { get; set; } = "";
    public int SolvedToday { get; set; }
    public int DailyGoal { get; set; }
}

public sealed class LockSnapshot
{
    public bool ShouldLock { get; set; }
    public bool UnlockedToday { get; set; }
    public int SolvedToday { get; set; }
    public int DailyGoal { get; set; }
    public string Direction { get; set; } = "";
    public int Remaining { get; set; }
    public string Stock { get; set; } = nameof(StockLevel.Ok);
}

public sealed class SubmitResult
{
    public bool Correct { get; set; }
    public bool ProblemMissing { get; set; }
    public bool Unlocked { get; set; }
    public int SolvedToday { get; set; }
    public int DailyGoal { get; set; }
    public TaskView? Next { get; set; }
}

public sealed class DirectionAdvice
{
    public int RemainingWithOlympiad { get; set; }
    public int RemainingInDirection { get; set; }
    public bool SuggestClearOlympiad { get; set; }
    public bool DirectionEmpty { get; set; }
}

public sealed class ImportDefaults
{
    public string Direction { get; set; } = "";
    public int? Grade { get; set; }
    public string Olympiad { get; set; } = "";
}

public sealed class ImportRow
{
    public int Index { get; set; }
    public string Statement { get; set; } = "";
    public List<string> Answers { get; set; } = [];
    public string Direction { get; set; } = "";
    public int Grade { get; set; }
    public string Olympiad { get; set; } = "";
    public string Note { get; set; } = "";
    public string Status { get; set; } = "ready";
    public string Message { get; set; } = "";

    public string AnswersText => string.Join("; ", Answers);

    public string StatusText => Status switch
    {
        "duplicate" => "Уже есть",
        "error" => "Ошибка",
        _ => "Готова"
    };

    public string StatementPreview
    {
        get
        {
            var oneLine = TextKey.Collapse(Statement);
            return oneLine.Length <= 80 ? oneLine : oneLine[..80] + "…";
        }
    }
}

public sealed class ImportPreview
{
    public List<ImportRow> Rows { get; set; } = [];
    public int Ready { get; set; }
    public int Duplicates { get; set; }
    public int Errors { get; set; }

    public string Summary => $"Будет добавлено: {Ready}. Уже есть: {Duplicates}. Ошибок: {Errors}.";
}

public sealed class ImportCommitResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<long> AddedIds { get; set; } = [];
}

public sealed class StockWarning
{
    public string Sid { get; set; } = "";
    public string Username { get; set; } = "";
    public string Direction { get; set; } = "";
    public int Remaining { get; set; }
    public int DailyGoal { get; set; }
    public string Stock { get; set; } = nameof(StockLevel.Ok);

    public string Text =>
        $"Для учётной записи {Username} осталось {Remaining} нерешённых задач по направлению «{Direction}» (нужно {DailyGoal} в день).";
}

public sealed class SaveProblemRequest
{
    public long Id { get; set; }
    public string Statement { get; set; } = "";
    public List<string> Answers { get; set; } = [];
    public string Direction { get; set; } = "";
    public int Grade { get; set; }
    public string Olympiad { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class SubmitRequest
{
    public long ProblemId { get; set; }
    public string Answer { get; set; } = "";
}

public sealed class SidRequest
{
    public string Sid { get; set; } = "";
}

public sealed class ClearProgressRequest
{
    public string Sid { get; set; } = "";
    public bool TodayOnly { get; set; }
}

public sealed class AdviseRequest
{
    public string Sid { get; set; } = "";
    public string Direction { get; set; } = "";
    public int Grade { get; set; }
    public string Olympiad { get; set; } = "";
}

public sealed class ImportRequest
{
    public string Json { get; set; } = "";
    public ImportDefaults Defaults { get; set; } = new();
    public bool UpdateDuplicates { get; set; }
}

namespace OlympiadGate.Core;

public enum StockLevel
{
    Ok,
    Low,
    Empty
}

public static class StockRules
{
    public static int Threshold(int dailyGoal, int warnBelow)
    {
        var goal = dailyGoal < 1 ? 1 : dailyGoal;
        return warnBelow > 0 ? warnBelow : goal * 2;
    }

    public static StockLevel Level(int remaining, int dailyGoal, int warnBelow)
    {
        var goal = dailyGoal < 1 ? 1 : dailyGoal;
        if (remaining < goal)
            return StockLevel.Empty;
        if (remaining < Threshold(goal, warnBelow))
            return StockLevel.Low;
        return StockLevel.Ok;
    }
}

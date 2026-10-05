namespace OlympiadGate.Core;

public static class AnswerMatch
{
    public static string Normalize(string? value)
    {
        return TextKey.Collapse(value).Replace(',', '.').ToLowerInvariant();
    }

    public static bool Matches(string? given, IEnumerable<string> accepted)
    {
        var left = Normalize(given);
        if (left.Length == 0)
            return false;

        foreach (var answer in accepted)
        {
            if (Normalize(answer) == left)
                return true;
        }

        return false;
    }
}

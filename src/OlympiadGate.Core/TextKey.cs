using System.Text.RegularExpressions;

namespace OlympiadGate.Core;

public static class TextKey
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return Whitespace.Replace(value.Trim(), " ");
    }

    public static string NameKey(string? value) => Collapse(value).ToLowerInvariant();

    public static string StatementKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return Whitespace.Replace(value.Trim(), " ").ToLowerInvariant();
    }
}

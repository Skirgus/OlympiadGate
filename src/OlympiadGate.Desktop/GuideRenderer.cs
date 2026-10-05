using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OlympiadGate.Desktop;

public static class GuideRenderer
{
    public static void Fill(StackPanel panel, string markdown)
    {
        panel.Children.Clear();
        var paragraph = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("# ", StringComparison.Ordinal))
                AddHeading(panel, paragraph, raw[2..], 28);
            else if (raw.StartsWith("## ", StringComparison.Ordinal))
                AddHeading(panel, paragraph, raw[3..], 20);
            else if (raw.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush(panel, paragraph);
                panel.Children.Add(Body("• " + raw[2..], 15, new Thickness(8, 0, 0, 6)));
            }
            else if (string.IsNullOrWhiteSpace(raw))
            {
                Flush(panel, paragraph);
            }
            else
            {
                if (paragraph.Length > 0)
                    paragraph.Append(' ');
                paragraph.Append(raw.Trim());
            }
        }

        Flush(panel, paragraph);
    }

    private static void AddHeading(StackPanel panel, StringBuilder paragraph, string text, double size)
    {
        Flush(panel, paragraph);
        panel.Children.Add(new TextBlock
        {
            Text = text.Trim(),
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(28, 36, 33)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8)
        });
    }

    private static void Flush(StackPanel panel, StringBuilder paragraph)
    {
        var text = paragraph.ToString().Trim();
        paragraph.Clear();
        if (text.Length > 0)
            panel.Children.Add(Body(text, 15, new Thickness(0, 0, 0, 12)));
    }

    private static TextBlock Body(string text, double size, Thickness margin) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin,
        Foreground = new SolidColorBrush(Color.FromRgb(28, 36, 33)),
        LineHeight = 22
    };
}

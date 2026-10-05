using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OlympiadGate.Core;

namespace OlympiadGate.Desktop;

public partial class ImportWindow : Window
{
    private readonly GateClient _client;
    private string _json = "";
    private ImportPreview? _preview;

    public ImportWindow(GateClient client, ImportDefaults defaults)
    {
        InitializeComponent();
        _client = client;
        DirectionBox.Text = defaults.Direction;
        GradeBox.Text = defaults.Grade?.ToString() ?? "";
        OlympiadBox.Text = defaults.Olympiad;
    }

    public List<long> AddedIds { get; private set; } = [];

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            Title = "Файл задач"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        _json = File.ReadAllText(dialog.FileName);
        FileText.Text = dialog.FileName;
        PasteBox.Text = _json;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e) => await PreviewAsync();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_preview == null || _preview.Ready == 0 && !(UpdateBox.IsChecked == true && _preview.Duplicates > 0))
        {
            await PreviewAsync();
            if (_preview == null || _preview.Ready == 0 && !(UpdateBox.IsChecked == true && _preview.Duplicates > 0))
                return;
        }

        try
        {
            var result = await _client.CallAsync<ImportCommitResult>("commitImport", new ImportRequest
            {
                Json = CurrentJson(),
                Defaults = Defaults(),
                UpdateDuplicates = UpdateBox.IsChecked == true
            });
            AddedIds = result?.AddedIds ?? [];
            MessageBox.Show(
                $"Добавлено: {result?.Added ?? 0}. Обновлено: {result?.Updated ?? 0}. Пропущено: {result?.Skipped ?? 0}. Ошибок: {result?.Errors ?? 0}.",
                "OlympiadGate");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task PreviewAsync()
    {
        try
        {
            _preview = await _client.CallAsync<ImportPreview>("previewImport", new ImportRequest
            {
                Json = CurrentJson(),
                Defaults = Defaults()
            });
            Grid.ItemsSource = _preview?.Rows;
            SummaryText.Text = _preview?.Summary ?? "";
            Tabs.SelectedIndex = 2;
            AddButton.IsEnabled = _preview is { Ready: > 0 } || (UpdateBox.IsChecked == true && _preview is { Duplicates: > 0 });
        }
        catch (Exception ex)
        {
            _preview = null;
            Grid.ItemsSource = null;
            SummaryText.Text = ex.Message;
            AddButton.IsEnabled = false;
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Grid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not ImportRow row)
            return;
        e.Row.Background = row.Status switch
        {
            "error" => new SolidColorBrush(Color.FromRgb(255, 230, 226)),
            "duplicate" => new SolidColorBrush(Color.FromRgb(255, 244, 214)),
            _ => Brushes.White
        };
    }

    private string CurrentJson()
    {
        var pasted = PasteBox.Text.Trim();
        return pasted.Length > 0 ? pasted : _json;
    }

    private ImportDefaults Defaults() => new()
    {
        Direction = DirectionBox.Text.Trim(),
        Grade = int.TryParse(GradeBox.Text.Trim(), out var grade) ? grade : null,
        Olympiad = OlympiadBox.Text.Trim()
    };
}

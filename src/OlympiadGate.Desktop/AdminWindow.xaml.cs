using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OlympiadGate.Core;

namespace OlympiadGate.Desktop;

public partial class AdminWindow : Window
{
    private readonly GateClient _client = new();
    private List<AccountSettings> _accounts = [];
    private List<ProblemRecord> _problems = [];
    private AccountSettings? _selected;
    private long _problemId;
    private HashSet<long>? _onlyIds;
    private bool _filling;

    public AdminWindow()
    {
        InitializeComponent();
        var version = ProductInfo.Version;
        Title = "OlympiadGate " + version;
        VersionText.Text = "Версия " + version;
        for (var grade = 1; grade <= 11; grade++)
        {
            GradeBox.Items.Add(grade);
            ProblemGradeBox.Items.Add(grade);
        }

        GradeBox.SelectedItem = 5;
        ProblemGradeBox.SelectedItem = 5;
        GradeFilter.Items.Add("Все");
        for (var grade = 1; grade <= 11; grade++)
            GradeFilter.Items.Add(grade.ToString());
        GradeFilter.SelectedIndex = 0;
        DirectionFilter.Items.Add("Все");
        DirectionFilter.Text = "Все";
        FillHelp();
        SelectPage("accounts");
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void ShowAccounts_Click(object sender, RoutedEventArgs e) => SelectPage("accounts");

    private void ShowBank_Click(object sender, RoutedEventArgs e) => SelectPage("bank");

    private void ShowHelp_Click(object sender, RoutedEventArgs e) => SelectPage("help");

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            SelectPage("help");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && BankPage.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            _ = SaveProblemAsync();
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            AccountsStatus.Text = "Загрузка учётных записей...";
            _accounts = await _client.CallAsync<List<AccountSettings>>("listUsers") ?? [];
            _problems = await _client.CallAsync<List<ProblemRecord>>("listProblems") ?? [];
            var directions = await _client.CallAsync<List<string>>("listDirections") ?? [];
            var warnings = await _client.CallAsync<List<StockWarning>>("warnings") ?? [];
            OfflineBanner.Visibility = Visibility.Collapsed;
            var sid = _selected?.Sid;
            AccountsList.ItemsSource = _accounts;
            if (sid != null)
                AccountsList.SelectedItem = _accounts.FirstOrDefault(account => account.Sid == sid);
            FillDirections(directions);
            ShowWarnings(warnings);
            EmptyBanner.Visibility = _accounts.All(account => !account.LockEnabled) && _problems.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            AccountsStatus.Text = _accounts.Count == 0
                ? "Включённые учётные записи не найдены."
                : "";
            ApplyProblemFilter();
        }
        catch (Exception ex)
        {
            AccountsStatus.Text = ex.Message;
            OfflineBanner.Visibility = Visibility.Visible;
            OfflineText.Text = ex.Message;
        }
    }

    private void FillDirections(List<string> directions)
    {
        var currentAccount = DirectionBox.Text;
        var currentProblem = ProblemDirectionBox.Text;
        var currentFilter = DirectionFilter.Text;
        DirectionBox.Items.Clear();
        ProblemDirectionBox.Items.Clear();
        DirectionFilter.Items.Clear();
        DirectionFilter.Items.Add("Все");
        foreach (var direction in directions)
        {
            DirectionBox.Items.Add(direction);
            ProblemDirectionBox.Items.Add(direction);
            DirectionFilter.Items.Add(direction);
        }

        DirectionBox.Text = currentAccount;
        ProblemDirectionBox.Text = currentProblem;
        DirectionFilter.Text = string.IsNullOrWhiteSpace(currentFilter) ? "Все" : currentFilter;
    }

    private void ShowWarnings(List<StockWarning> warnings)
    {
        WarningsPanel.Children.Clear();
        foreach (var warning in warnings)
        {
            var critical = warning.Stock == nameof(StockLevel.Empty);
            WarningsPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(critical ? Color.FromRgb(248, 224, 216) : Color.FromRgb(248, 231, 192)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock { Text = warning.Text, TextWrapping = TextWrapping.Wrap }
            });
        }
    }

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountSettings account)
            return;
        _selected = account;
        _filling = true;
        AccountTitle.Text = account.Username;
        AdminNote.Visibility = account.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        LockBox.IsChecked = account.LockEnabled;
        LockBox.IsEnabled = !account.IsAdministrator;
        GradeBox.SelectedItem = account.Grade is >= 1 and <= 11 ? account.Grade : 5;
        DirectionBox.Text = account.Direction;
        OlympiadBox.Text = account.Olympiad;
        GoalBox.Text = account.DailyGoal.ToString();
        WarnBox.Text = account.WarnBelow.ToString();
        StockText.Text = string.IsNullOrWhiteSpace(account.Direction)
            ? ""
            : $"Нерешённых задач: {account.Remaining}.";
        _filling = false;
    }

    private async void SaveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;
        var direction = DirectionBox.Text.Trim();
        var olympiad = OlympiadBox.Text.Trim();
        var grade = GradeBox.SelectedItem is int selectedGrade ? selectedGrade : 5;
        var goal = ReadInt(GoalBox.Text, 3);
        var warn = ReadInt(WarnBox.Text, 0);
        var enabled = LockBox.IsChecked == true && !_selected.IsAdministrator;
        var changed = !string.Equals(_selected.Direction, direction, StringComparison.CurrentCultureIgnoreCase)
            || !string.Equals(_selected.Olympiad, olympiad, StringComparison.CurrentCultureIgnoreCase)
            || _selected.Grade != grade
            || (!_selected.LockEnabled && enabled);
        if (changed)
        {
            try
            {
                var advice = await _client.CallAsync<DirectionAdvice>("adviseDirection", new AdviseRequest
                {
                    Sid = _selected.Sid,
                    Direction = direction,
                    Grade = grade,
                    Olympiad = olympiad
                });
                if (advice is { SuggestClearOlympiad: true })
                {
                    var choice = MessageBox.Show(
                        $"В направлении «{direction}» нет задач олимпиады «{olympiad}». Очистить олимпиаду и брать все задачи направления?",
                        "OlympiadGate",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);
                    if (choice == MessageBoxResult.Cancel)
                        return;
                    if (choice == MessageBoxResult.Yes)
                    {
                        olympiad = "";
                        OlympiadBox.Text = "";
                    }
                }
                else if (enabled && advice is { DirectionEmpty: true })
                {
                    MessageBox.Show("Укажите направление.", "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                else if (enabled && advice is { RemainingInDirection: 0 } && direction.Length > 0)
                {
                    var choice = MessageBox.Show(
                        $"В направлении «{direction}» пока нет задач. Замок будет закрыт, пока задачи не появятся. Сохранить?",
                        "OlympiadGate",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes)
                        return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            await _client.CallAsync<AccountSettings>("saveAccount", new AccountSettings
            {
                Sid = _selected.Sid,
                Username = _selected.Username,
                LockEnabled = enabled,
                Grade = grade,
                Direction = direction,
                Olympiad = olympiad,
                DailyGoal = goal,
                WarnBelow = warn
            });
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void UnlockToday_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;
        try
        {
            await _client.CallAsync<bool>("unlockToday", new SidRequest { Sid = _selected.Sid });
            MessageBox.Show($"Доступ для {_selected.Username} открыт до конца сегодняшнего дня.", "OlympiadGate");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ClearToday_Click(object sender, RoutedEventArgs e) =>
        await ClearProgressAsync(todayOnly: true);

    private async void ClearAll_Click(object sender, RoutedEventArgs e) =>
        await ClearProgressAsync(todayOnly: false);

    private async Task ClearProgressAsync(bool todayOnly)
    {
        if (_selected == null)
            return;

        var question = todayOnly
            ? $"Снять сегодняшние отметки у {_selected.Username}? Задачи, решённые сегодня, снова попадут в выдачу, и замок вернётся, пока дневная норма не будет набрана заново."
            : $"Снять все отметки о решении у {_selected.Username}? Любая задача снова может быть выдана, сегодняшний доступ тоже закроется.";
        if (MessageBox.Show(question, "OlympiadGate", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var removed = await _client.CallAsync<int>("clearProgress", new ClearProgressRequest
            {
                Sid = _selected.Sid,
                TodayOnly = todayOnly
            });
            var text = todayOnly
                ? $"Снято сегодняшних отметок: {removed}. Эти задачи снова могут быть выданы."
                : $"Снято отметок: {removed}. Все задачи этой учётки снова могут быть выданы.";
            MessageBox.Show(text, "OlympiadGate");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyIfReady();

    private void TextFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyIfReady();

    private void DirectionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyIfReady();

    private void GradeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyIfReady();

    private void ApplyIfReady()
    {
        if (!IsLoaded || _filling)
            return;
        ApplyProblemFilter();
    }

    private void ApplyProblemFilter()
    {
        IEnumerable<ProblemRecord> query = _problems;
        if (_onlyIds != null)
            query = query.Where(problem => _onlyIds.Contains(problem.Id));
        var direction = DirectionFilter.Text.Trim();
        if (direction.Length > 0 && !direction.Equals("Все", StringComparison.CurrentCultureIgnoreCase))
            query = query.Where(problem => problem.Direction.Contains(direction, StringComparison.CurrentCultureIgnoreCase));
        if (int.TryParse(GradeFilter.SelectedItem as string, out var grade))
            query = query.Where(problem => problem.Grade == grade);
        var olympiad = OlympiadFilter.Text.Trim();
        if (olympiad.Length > 0)
            query = query.Where(problem => problem.Olympiad.Contains(olympiad, StringComparison.CurrentCultureIgnoreCase));
        var text = TextFilter.Text.Trim();
        if (text.Length > 0)
        {
            var numberText = text.TrimStart('№', '#').Trim();
            var byNumber = long.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
            query = query.Where(problem =>
                problem.Statement.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
                (byNumber && problem.Id == number));
        }
        ProblemsList.ItemsSource = query.ToList();
    }

    private void ProblemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is not ProblemRecord problem)
            return;
        _problemId = problem.Id;
        ProblemNumberText.Text = $"Задача № {problem.Id}";
        ProblemNumberText.Visibility = Visibility.Visible;
        StatementBox.Text = problem.Statement;
        HintBox.Text = problem.Hint;
        AnswersBox.Text = problem.AnswersText;
        SolutionBox.Text = problem.Solution;
        ProblemDirectionBox.Text = problem.Direction;
        ProblemGradeBox.SelectedItem = problem.Grade;
        ProblemOlympiadBox.Text = problem.Olympiad;
        NoteBox.Text = problem.Note;
    }

    private async void SaveProblem_Click(object sender, RoutedEventArgs e) => await SaveProblemAsync();

    private async Task SaveProblemAsync()
    {
        try
        {
            var id = await _client.CallAsync<long>("saveProblem", new SaveProblemRequest
            {
                Id = _problemId,
                Statement = StatementBox.Text,
                Answers = SplitAnswers(AnswersBox.Text),
                Direction = ProblemDirectionBox.Text,
                Grade = ProblemGradeBox.SelectedItem is int grade ? grade : 5,
                Olympiad = ProblemOlympiadBox.Text,
                Note = NoteBox.Text,
                Hint = HintBox.Text,
                Solution = SolutionBox.Text
            });
            var direction = ProblemDirectionBox.Text;
            var problemGrade = ProblemGradeBox.SelectedItem;
            var olympiad = ProblemOlympiadBox.Text;
            _problemId = 0;
            ProblemNumberText.Visibility = Visibility.Collapsed;
            StatementBox.Clear();
            HintBox.Clear();
            AnswersBox.Clear();
            SolutionBox.Clear();
            NoteBox.Clear();
            ProblemDirectionBox.Text = direction;
            ProblemGradeBox.SelectedItem = problemGrade;
            ProblemOlympiadBox.Text = olympiad;
            _onlyIds = null;
            ImportedBanner.Visibility = Visibility.Collapsed;
            await ReloadAsync();
            StatementBox.Focus();
            _ = id;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewProblem_Click(object sender, RoutedEventArgs e)
    {
        _problemId = 0;
        ProblemNumberText.Visibility = Visibility.Collapsed;
        StatementBox.Clear();
        HintBox.Clear();
        AnswersBox.Clear();
        SolutionBox.Clear();
        NoteBox.Clear();
        StatementBox.Focus();
    }

    private async void DeleteProblem_Click(object sender, RoutedEventArgs e)
    {
        if (_problemId <= 0)
            return;
        if (MessageBox.Show("Удалить задачу?", "OlympiadGate", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await _client.CallAsync<bool>("deleteProblem", new SaveProblemRequest { Id = _problemId });
            NewProblem_Click(sender, e);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OlympiadGate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new ImportDefaults
        {
            Direction = FirstFilled(ProblemDirectionBox.Text, _selected?.Direction, DirectionFilter.Text),
            Grade = ProblemGradeBox.SelectedItem is int grade ? grade : _selected?.Grade ?? 5,
            Olympiad = FirstFilled(ProblemOlympiadBox.Text, _selected?.Olympiad, OlympiadFilter.Text)
        };
        if (defaults.Direction == "Все")
            defaults.Direction = "";
        var dialog = new ImportWindow(_client, defaults) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _onlyIds = dialog.AddedIds.Count == 0 ? null : dialog.AddedIds.ToHashSet();
        ImportedBanner.Visibility = _onlyIds == null ? Visibility.Collapsed : Visibility.Visible;
        await ReloadAsync();
    }

    private void ShowAllProblems_Click(object sender, RoutedEventArgs e)
    {
        _onlyIds = null;
        ImportedBanner.Visibility = Visibility.Collapsed;
        ApplyProblemFilter();
    }

    private void Prompt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Куда сохранить инструкцию для нейросети" };
        if (dialog.ShowDialog(this) != true)
            return;
        var grade = ProblemGradeBox.SelectedItem is int selected ? selected : _selected?.Grade ?? 5;
        var files = ImportPrompt.Build(
            FirstFilled(ProblemDirectionBox.Text, _selected?.Direction),
            grade,
            FirstFilled(ProblemOlympiadBox.Text, _selected?.Olympiad));
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllText(Path.Combine(dialog.FolderName, "olympiadgate-prompt.txt"), files.Prompt, encoding);
        File.WriteAllText(Path.Combine(dialog.FolderName, "olympiadgate-example.json"), files.ExampleJson, encoding);
        MessageBox.Show("Сохранены olympiadgate-prompt.txt и olympiadgate-example.json.", "OlympiadGate");
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(uninstaller))
        {
            MessageBox.Show(
                "Удаление доступно после установки через OlympiadGate-Setup.exe.",
                "OlympiadGate",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uninstaller) { UseShellExecute = true });
    }

    private void SelectPage(string page)
    {
        AccountsPage.Visibility = page == "accounts" ? Visibility.Visible : Visibility.Collapsed;
        BankPage.Visibility = page == "bank" ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = page == "help" ? Visibility.Visible : Visibility.Collapsed;
        PaintNav(AccountsButton, page == "accounts");
        PaintNav(BankButton, page == "bank");
        PaintNav(HelpButton, page == "help");
    }

    private static void PaintNav(Button button, bool selected)
    {
        button.Background = new SolidColorBrush(selected ? Color.FromRgb(201, 107, 60) : Color.FromRgb(28, 51, 48));
        button.Foreground = Brushes.White;
    }

    private void FillHelp()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().FirstOrDefault(resource => resource.EndsWith("guide.md", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            return;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            return;
        using var reader = new StreamReader(stream);
        GuideRenderer.Fill(HelpHost, reader.ReadToEnd());
    }

    private static int ReadInt(string text, int fallback) =>
        int.TryParse(text.Trim(), out var value) ? value : fallback;

    private static List<string> SplitAnswers(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();

    private static string FirstFilled(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "Все")
                return value.Trim();
        }

        return "";
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OlympiadGate.Core;

namespace OlympiadGate.Desktop;

public partial class LockWindow : Window
{
    private readonly GateClient _client = new();
    private readonly DispatcherTimer _timer;
    private KeyboardBlock? _keyboard;
    private Mutex? _mutex;
    private TaskView? _current;
    private bool _allowClose;
    private bool _busy;

    public LockWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Maintain();
    }

    public bool TakeOver()
    {
        try
        {
            var name = $@"Global\OlympiadGate.Lock.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
            _mutex = new Mutex(true, name, out var created);
            if (created)
                return true;
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, -20);
        var value = style.ToInt64();
        value |= 0x00000080;
        value &= ~0x00040000;
        SetWindowLongPtr(hwnd, -20, new IntPtr(value));
        _keyboard = new KeyboardBlock();
        CoverScreens();
        _timer.Start();
        Loaded += async (_, _) => await RefreshAsync();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _keyboard?.Dispose();
        _mutex?.Dispose();
        base.OnClosed(e);
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async void AnswerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_busy)
            return;
        try
        {
            var snapshot = await _client.CallAsync<LockSnapshot>("lockSnapshot");
            if (snapshot is not { ShouldLock: true })
            {
                Release();
                return;
            }

            ShowProgress(snapshot.Direction, snapshot.SolvedToday, snapshot.DailyGoal);
            if (_current != null)
                return;

            var task = await _client.CallAsync<TaskView>("nextProblem");
            ShowTask(task, snapshot.Direction, snapshot.SolvedToday, snapshot.DailyGoal);
        }
        catch (Exception ex)
        {
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = ex.Message + " Позовите администратора.";
            TaskPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SubmitAsync()
    {
        if (_busy || _current == null)
            return;
        _busy = true;
        SubmitButton.IsEnabled = false;
        try
        {
            var result = await _client.CallAsync<SubmitResult>("submitAnswer", new SubmitRequest
            {
                ProblemId = _current.Id,
                Answer = AnswerBox.Text
            });
            if (result == null)
                return;
            if (result.Unlocked)
            {
                Release();
                return;
            }

            if (result.ProblemMissing)
            {
                _current = null;
                await RefreshAsync();
                return;
            }

            if (!result.Correct)
            {
                ErrorText.Text = "Неверно, попробуйте ещё раз.";
                AnswerBox.SelectAll();
                AnswerBox.Focus();
                return;
            }

            ErrorText.Text = "";
            ShowTask(result.Next, result.Next?.Direction ?? DirectionText.Text, result.SolvedToday, result.DailyGoal);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            SubmitButton.IsEnabled = true;
        }
    }

    private void ShowTask(TaskView? task, string direction, int solved, int goal)
    {
        ShowProgress(direction, solved, goal);
        _current = task;
        if (task == null)
        {
            TaskPanel.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = string.IsNullOrWhiteSpace(direction)
                ? "Задачи закончились. Позовите администратора."
                : $"Задачи по направлению «{direction}» закончились. Позовите администратора.";
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        TaskPanel.Visibility = Visibility.Visible;
        StatementText.Text = task.Statement;
        AnswerBox.Text = "";
        ErrorText.Text = "";
        AnswerBox.Focus();
    }

    private void ShowProgress(string direction, int solved, int goal)
    {
        DirectionText.Text = string.IsNullOrWhiteSpace(direction) ? "Задачи" : direction;
        ProgressText.Text = goal > 0 ? $"Решено {solved} из {goal}" : "";
    }

    private void Release()
    {
        if (_allowClose)
            return;
        _allowClose = true;
        Close();
    }

    private int _ticks;

    private void Maintain()
    {
        CoverScreens();
        if (!IsActive)
            Activate();
        _ticks++;
        if (_ticks % 12 == 0)
            _ = RefreshAsync();
    }

    private void CoverScreens()
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            return;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var origin = transform.Transform(new Point(left, top));
        var far = transform.Transform(new Point(left + width, top + height));
        Left = origin.X;
        Top = origin.Y;
        Width = Math.Max(320, far.X - origin.X);
        Height = Math.Max(240, far.Y - origin.Y);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}

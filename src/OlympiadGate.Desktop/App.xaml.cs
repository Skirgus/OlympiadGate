using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using OlympiadGate.Core;

namespace OlympiadGate.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var args = e.Args ?? [];

        if (args.Any(arg => arg.Equals("--cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            LockPolicies.ClearAll();
            Shutdown();
            return;
        }

        if (args.Any(arg => arg.Equals("--lock", StringComparison.OrdinalIgnoreCase)))
        {
            var window = new LockWindow();
            if (!window.TakeOver())
            {
                Shutdown();
                return;
            }

            MainWindow = window;
            window.Closed += (_, _) => Shutdown();
            window.Show();
            return;
        }

        if (!IsElevated())
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--admin"
                });
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    "Панель администратора нужно открыть с правами администратора.",
                    "OlympiadGate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        var admin = new AdminWindow();
        MainWindow = admin;
        admin.Closed += (_, _) => Shutdown();
        admin.Show();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

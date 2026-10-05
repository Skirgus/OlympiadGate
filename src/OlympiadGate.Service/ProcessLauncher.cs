using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OlympiadGate.Service;

public sealed class ProcessLauncher
{
    private readonly Dictionary<int, int> _pids = new();
    private readonly object _gate = new();

    public bool IsRunning(int sessionId)
    {
        if (MutexExists(sessionId))
            return true;

        lock (_gate)
        {
            if (!_pids.TryGetValue(sessionId, out var pid))
                return false;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (!process.HasExited && process.SessionId == sessionId)
                    return true;
            }
            catch (ArgumentException)
            {
                // Процесс уже завершился.
            }
            catch (Win32Exception)
            {
            }

            _pids.Remove(sessionId);
            return false;
        }
    }

    public int Launch(int sessionId, string desktopExe)
    {
        var pid = StartInSession(sessionId, desktopExe);
        lock (_gate)
            _pids[sessionId] = pid;
        return pid;
    }

    public static string? FindDesktopExe()
    {
        var overridePath = Environment.GetEnvironmentVariable("OLYMPIADGATE_DESKTOP");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var beside = Path.Combine(AppContext.BaseDirectory, "OlympiadGate.Desktop.exe");
        if (File.Exists(beside))
            return beside;

        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(
                sourceRoot,
                "OlympiadGate.Desktop",
                "bin",
                configuration,
                "net8.0-windows",
                "OlympiadGate.Desktop.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool MutexExists(int sessionId)
    {
        try
        {
            using var mutex = Mutex.OpenExisting($@"Global\OlympiadGate.Lock.{sessionId}");
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static int StartInSession(int sessionId, string desktopExe)
    {
        if (!WtsQueryUserToken(sessionId, out var userToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось получить маркер сессии.");

        var primary = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(userToken, 0x02000000, IntPtr.Zero, 2, 1, out primary))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось подготовить маркер сессии.");

            var hasEnvironment = CreateEnvironmentBlock(out environment, primary, false);
            var startup = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = "winsta0\\default"
            };
            var command = new StringBuilder(1024);
            command.Append('"').Append(desktopExe).Append("\" --lock");
            var created = CreateProcessAsUser(
                primary,
                desktopExe,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                hasEnvironment ? 0x00000400u : 0,
                hasEnvironment ? environment : IntPtr.Zero,
                Path.GetDirectoryName(desktopExe),
                ref startup,
                out var information);
            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось запустить замок.");

            CloseHandle(information.hThread);
            CloseHandle(information.hProcess);
            return information.dwProcessId;
        }
        finally
        {
            if (environment != IntPtr.Zero)
                DestroyEnvironmentBlock(environment);
            if (primary != IntPtr.Zero)
                CloseHandle(primary);
            CloseHandle(userToken);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQueryUserToken", SetLastError = true)]
    private static extern bool WtsQueryUserToken(int sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);
}

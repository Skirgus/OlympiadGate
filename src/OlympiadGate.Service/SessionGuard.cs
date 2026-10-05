using System.Runtime.InteropServices;
using System.Security.Principal;
using OlympiadGate.Core;

namespace OlympiadGate.Service;

public sealed class SessionGuard
{
    private readonly GateCatalog _catalog;
    private readonly ProcessLauncher _launcher;
    private readonly FileLog _log;

    public SessionGuard(GateCatalog catalog, ProcessLauncher launcher, FileLog log)
    {
        _catalog = catalog;
        _launcher = launcher;
        _log = log;
    }

    public void Tick()
    {
        var desktop = ProcessLauncher.FindDesktopExe();
        foreach (var sessionId in SessionList.Active())
        {
            string? sid;
            try
            {
                sid = SessionList.UserSid(sessionId);
            }
            catch (Exception ex)
            {
                _log.Error("Не удалось определить учётку сессии " + sessionId, ex);
                continue;
            }

            if (string.IsNullOrWhiteSpace(sid))
                continue;
            if (LocalAccounts.IsAdministrator(sid))
            {
                LockPolicies.Clear(sid);
                continue;
            }

            var snapshot = _catalog.GetLockSnapshot(sid);
            if (!snapshot.ShouldLock)
            {
                LockPolicies.Clear(sid);
                continue;
            }

            LockPolicies.Apply(sid);
            if (desktop == null)
            {
                _log.Error("Не найден OlympiadGate.Desktop.exe.");
                continue;
            }

            if (_launcher.IsRunning(sessionId))
                continue;

            try
            {
                var pid = _launcher.Launch(sessionId, desktop);
                _log.Info($"Замок запущен в сессии {sessionId}, процесс {pid}.");
            }
            catch (Exception ex)
            {
                _log.Error("Не удалось запустить замок в сессии " + sessionId, ex);
            }
        }
    }
}

internal static class SessionList
{
    public static List<int> Active()
    {
        var sessions = new List<int>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count) || buffer == IntPtr.Zero)
            return sessions;

        try
        {
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(buffer, index * size));
                if (info.State == 0 && info.SessionId != 0)
                    sessions.Add(info.SessionId);
            }
        }
        finally
        {
            WTSFreeMemory(buffer);
        }

        return sessions;
    }

    public static string? UserSid(int sessionId)
    {
        var user = QueryString(sessionId, 5);
        var domain = QueryString(sessionId, 7);
        if (string.IsNullOrWhiteSpace(user))
            return null;

        var account = string.IsNullOrWhiteSpace(domain) ? user : domain + "\\" + user;
        try
        {
            return ((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
    }

    private static string? QueryString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _) || buffer == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr StationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}

using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace OlympiadGate.Core;

public sealed class PolicyMark
{
    public string Sid { get; set; } = "";
    public bool TaskMgrExisted { get; set; }
    public int TaskMgrValue { get; set; }
    public bool NoRunExisted { get; set; }
    public int NoRunValue { get; set; }
}

public static class LockPolicies
{
    private const string TaskMgrKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string NoRunKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private static readonly IntPtr HkeyUsers = new(unchecked((int)0x80000003));
    private static readonly object Gate = new();

    public static void Apply(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return;

        lock (Gate)
        {
            var marks = Load();
            var mark = marks.FirstOrDefault(item => string.Equals(item.Sid, sid, StringComparison.OrdinalIgnoreCase));
            using var root = Registry.Users.OpenSubKey(sid, writable: true);
            if (root == null)
            {
                Log($"Не удалось открыть реестр учётки {sid} для запрета диспетчера задач.");
                return;
            }

            if (mark == null)
            {
                mark = new PolicyMark { Sid = sid };
                (mark.TaskMgrExisted, mark.TaskMgrValue) = ReadValue(root, TaskMgrKey, "DisableTaskMgr");
                (mark.NoRunExisted, mark.NoRunValue) = ReadValue(root, NoRunKey, "NoRun");
                marks.Add(mark);
            }

            WriteDword(root, TaskMgrKey, "DisableTaskMgr", 1);
            WriteDword(root, NoRunKey, "NoRun", 1);
            Save(marks);
        }
    }

    public static void Clear(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return;

        lock (Gate)
        {
            var marks = Load();
            var mark = marks.FirstOrDefault(item => string.Equals(item.Sid, sid, StringComparison.OrdinalIgnoreCase));
            if (mark == null)
                return;
            if (!Restore(mark))
                return;
            marks.Remove(mark);
            Save(marks);
        }
    }

    public static void ClearAll()
    {
        lock (Gate)
        {
            var marks = Load();
            var pending = new List<PolicyMark>();
            foreach (var mark in marks)
            {
                if (!Restore(mark))
                    pending.Add(mark);
            }

            Save(pending);
        }
    }

    private static bool Restore(PolicyMark mark)
    {
        var loaded = false;
        var tempName = "OG" + mark.Sid.Replace("-", "");
        RegistryKey? root = null;
        try
        {
            root = Registry.Users.OpenSubKey(mark.Sid, writable: true);
            if (root == null)
            {
                if (!TryLoadHive(mark.Sid, tempName))
                    return false;
                loaded = true;
                root = Registry.Users.OpenSubKey(tempName, writable: true);
            }

            if (root == null)
                return false;

            RestoreValue(root, TaskMgrKey, "DisableTaskMgr", mark.TaskMgrExisted, mark.TaskMgrValue);
            RestoreValue(root, NoRunKey, "NoRun", mark.NoRunExisted, mark.NoRunValue);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Не удалось снять ограничения для {mark.Sid}: {ex.Message}");
            return false;
        }
        finally
        {
            root?.Dispose();
            if (loaded)
                RegUnLoadKey(HkeyUsers, tempName);
        }
    }

    private static bool TryLoadHive(string sid, string tempName)
    {
        try
        {
            EnablePrivilege("SeBackupPrivilege");
            EnablePrivilege("SeRestorePrivilege");
            using var profile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
            var path = profile?.GetValue("ProfileImagePath") as string;
            if (string.IsNullOrWhiteSpace(path))
                return false;
            path = Environment.ExpandEnvironmentVariables(path);
            var file = Path.Combine(path, "NTUSER.DAT");
            if (!File.Exists(file))
                return false;
            var error = RegLoadKey(HkeyUsers, tempName, file);
            if (error != 0)
            {
                Log($"RegLoadKey для {sid} вернул {error}.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            return false;
        }
    }

    private static (bool Exists, int Value) ReadValue(RegistryKey root, string subKey, string name)
    {
        using var key = root.OpenSubKey(subKey);
        var raw = key?.GetValue(name);
        return raw == null ? (false, 0) : (true, Convert.ToInt32(raw));
    }

    private static void WriteDword(RegistryKey root, string subKey, string name, int value)
    {
        using var key = root.CreateSubKey(subKey);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void RestoreValue(RegistryKey root, string subKey, string name, bool existed, int value)
    {
        using var key = root.CreateSubKey(subKey);
        if (key == null)
            return;
        if (!existed)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static List<PolicyMark> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.PolicyPath))
                return [];
            var json = File.ReadAllText(AppPaths.PolicyPath);
            return JsonSerializer.Deserialize<List<PolicyMark>>(json, GateJson.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void Save(List<PolicyMark> marks)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(marks, GateJson.Options);
        File.WriteAllText(AppPaths.PolicyPath, json);
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} POLICY {message}{Environment.NewLine}");
        }
        catch
        {
            // Журнал не должен срывать замок или удаление.
        }
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token))
            return;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                return;
            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = 0x00000002
            };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public uint LuidLow;
        public int LuidHigh;
        public uint Attributes;

        public Luid Luid
        {
            get => new() { Low = LuidLow, High = LuidHigh };
            set
            {
                LuidLow = value.Low;
                LuidHigh = value.High;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegLoadKey(IntPtr hive, string subKey, string file);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegUnLoadKey(IntPtr hive, string subKey);
}

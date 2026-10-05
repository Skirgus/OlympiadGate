using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OlympiadGate.Service;

public sealed class LocalUser
{
    public required string Sid { get; init; }
    public required string Name { get; init; }
    public bool IsAdministrator { get; init; }
}

public static class LocalAccounts
{
    private const int FilterNormalAccount = 0x0002;
    private const int AccountDisabled = 0x0002;
    private const int PrivilegeGuest = 0;
    private const int PrivilegeAdmin = 2;

    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Guest",
        "Гость",
        "DefaultAccount",
        "WDAGUtilityAccount"
    };

    private static readonly object Gate = new();
    private static DateTime _cachedAt = DateTime.MinValue;
    private static IReadOnlyList<LocalUser> _cache = [];

    public static IReadOnlyList<LocalUser> List()
    {
        lock (Gate)
        {
            if (_cache.Count > 0 && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(15))
                return _cache;

            _cache = Load();
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
    }

    public static bool IsAdministrator(string sid) =>
        List().Any(user => string.Equals(user.Sid, sid, StringComparison.OrdinalIgnoreCase) && user.IsAdministrator);

    private static List<LocalUser> Load()
    {
        var users = new List<LocalUser>();
        var resume = 0;
        while (true)
        {
            var status = NetUserEnum(null, 1, FilterNormalAccount, out var buffer, -1, out var read, out _, ref resume);
            try
            {
                if (status != 0 && status != 234)
                    throw new InvalidOperationException("Не удалось прочитать список учётных записей. Код " + status);

                var size = Marshal.SizeOf<UserInfo1>();
                for (var index = 0; index < read; index++)
                {
                    var info = Marshal.PtrToStructure<UserInfo1>(IntPtr.Add(buffer, index * size));
                    if (string.IsNullOrWhiteSpace(info.Name) || SkipNames.Contains(info.Name) || info.Name.EndsWith('$'))
                        continue;
                    if ((info.Flags & AccountDisabled) != 0 || info.Privilege == PrivilegeGuest)
                        continue;
                    if (!TrySid(info.Name, out var sid))
                        continue;
                    users.Add(new LocalUser
                    {
                        Sid = sid,
                        Name = info.Name,
                        IsAdministrator = info.Privilege == PrivilegeAdmin
                    });
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    NetApiBufferFree(buffer);
            }

            if (status != 234)
                break;
        }

        return users.OrderBy(user => user.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool TrySid(string name, out string sid)
    {
        try
        {
            sid = ((SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier))).Value;
            return true;
        }
        catch (IdentityNotMappedException)
        {
            sid = "";
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1
    {
        public string Name;
        public string Password;
        public int PasswordAge;
        public int Privilege;
        public string HomeDirectory;
        public string Comment;
        public int Flags;
        public string ScriptPath;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserEnum(
        string? server,
        int level,
        int filter,
        out IntPtr buffer,
        int preferredMaxLength,
        out int entriesRead,
        out int totalEntries,
        ref int resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}

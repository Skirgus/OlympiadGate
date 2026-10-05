using System.Security.AccessControl;
using System.Security.Principal;
using OlympiadGate.Core;

namespace OlympiadGate.Service;

public static class DataDirectory
{
    public static void EnsurePrivate()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != "S-1-5-18")
            return;
        var info = new DirectoryInfo(AppPaths.DataDirectory);
        var security = info.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var existing = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        foreach (var rule in existing)
            security.RemoveAccessRule(rule);

        void Allow(WellKnownSidType sid)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        Allow(WellKnownSidType.LocalSystemSid);
        Allow(WellKnownSidType.BuiltinAdministratorsSid);
        info.SetAccessControl(security);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OlympiadGate.Desktop;

public sealed class KeyboardBlock : IDisposable
{
    private readonly HookProc _callback;
    private IntPtr _hook;

    public KeyboardBlock()
    {
        _callback = Hook;
        var module = Process.GetCurrentProcess().MainModule?.ModuleName;
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(module), 0);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            if (message is 0x0100 or 0x0101 or 0x0104 or 0x0105)
            {
                var data = Marshal.PtrToStructure<KbdLl>(lParam);
                if (Blocked(data.vkCode))
                    return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool Blocked(uint vk)
    {
        if (vk is 0x5B or 0x5C)
            return true;
        if (IsDown(0x5B) || IsDown(0x5C))
            return true;
        var alt = IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5);
        var ctrl = IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3);
        var shift = IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1);
        if (alt && vk is 0x09 or 0x73 or 0x1B or 0x20)
            return true;
        if (ctrl && vk == 0x1B)
            return true;
        if (ctrl && shift && vk == 0x1B)
            return true;
        return false;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLl
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

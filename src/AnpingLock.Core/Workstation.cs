using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AnpingLock;

public static class Workstation
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo information);

    public static System.TimeSpan? IdleTime()
    {
        var information = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref information)) return null;
        // LASTINPUTINFO uses a 32-bit tick count, which wraps about every 49 days.
        int milliseconds = unchecked((int)((uint)System.Environment.TickCount64 - information.Time));
        // Some input sources report a slightly newer timestamp. Treat that as activity.
        return System.TimeSpan.FromMilliseconds(System.Math.Max(0, milliseconds));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public Luid TokenId, AuthenticationId;
        public long ExpirationTime;
        public int TokenType, ImpersonationLevel;
        public uint DynamicCharged, DynamicAvailable, GroupCount, PrivilegeCount;
        public Luid ModifiedId;
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out TokenStatistics statistics, int length, out int returned);

    public static string LogonSessionId()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return GetTokenInformation(identity.AccessToken, 10, out TokenStatistics statistics, Marshal.SizeOf<TokenStatistics>(), out _)
            ? statistics.AuthenticationId.High.ToString("X8") + statistics.AuthenticationId.Low.ToString("X8")
            : "";
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern System.IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(System.IntPtr desktop);

    public static bool IsSessionLocked()
    {
        System.IntPtr desktop = OpenInputDesktop(0, false, 1);
        if (desktop == System.IntPtr.Zero) return true;
        CloseDesktop(desktop);
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();
}

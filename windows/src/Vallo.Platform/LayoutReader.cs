using System.Runtime.InteropServices;
using System.Text;

namespace Vallo.Platform;

public readonly record struct LayoutEntry(int KeyCode, bool Shift, bool CapsLock, string Char);

/// <summary>Reads a Windows keyboard layout table via ToUnicodeEx (Windows only).</summary>
public static class LayoutReader
{
    private const uint KLF_NOTELLSHELL = 0x00000080;
    private const uint ToUnicodeNoStateChange = 4; // wFlags bit 2: do not change keyboard state

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnloadKeyboardLayout(IntPtr hkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 8)] StringBuilder pwszBuff,
        int cchBuff, uint wFlags, IntPtr dwhkl);

    private const uint MAPVK_VK_TO_VSC = 0;
    private const byte VK_SHIFT = 0x10, VK_CAPITAL = 0x14;

    /// <param name="klid">e.g. "00000409" (US), "00000419" (Russian)</param>
    public static List<LayoutEntry> Read(string klid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LayoutReader works on Windows only.");

        IntPtr hkl = LoadKeyboardLayout(klid, KLF_NOTELLSHELL);
        if (hkl == IntPtr.Zero)
            throw new InvalidOperationException($"LoadKeyboardLayout({klid}) failed: {Marshal.GetLastWin32Error()}");

        var result = new List<LayoutEntry>();
        var sb = new StringBuilder(8);
        for (uint vk = 0x20; vk <= 0xDF; vk++)
        {
            uint scan = MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, hkl);
            foreach (bool shift in new[] { false, true })
            foreach (bool caps in new[] { false, true })
            {
                var state = new byte[256];
                if (shift) state[VK_SHIFT] = 0x80;
                if (caps) state[VK_CAPITAL] = 0x01; // toggled bit

                sb.Clear();
                int n = ToUnicodeEx(vk, scan, state, sb, sb.Capacity, ToUnicodeNoStateChange, hkl);
                if (n < 0)
                {
                    // dead key: it leaves state in the layout; flush it with a second call on space.
                    sb.Clear();
                    ToUnicodeEx(0x20, MapVirtualKeyEx(0x20, MAPVK_VK_TO_VSC, hkl), new byte[256], sb, sb.Capacity, 0, hkl);
                    continue;
                }
                if (n == 0) continue;
                string s = sb.ToString(0, n);
                if (s.Length == 1 && char.IsControl(s[0])) continue;
                result.Add(new LayoutEntry((int)vk, shift, caps, s));
            }
        }
        return result;
    }
}

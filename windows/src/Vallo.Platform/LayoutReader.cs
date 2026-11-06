using System.Runtime.InteropServices;
using System.Text;

namespace Vallo.Platform;

public readonly record struct LayoutEntry(int KeyCode, bool Shift, bool CapsLock, string Char);

/// <summary>A dead key («US-International» apostrophe, quote, ^, ~, `): its own sign and what it gives with each next key.</summary>
public sealed record DeadEntry(int KeyCode, bool Shift, bool CapsLock, string Spacing, List<LayoutEntry> Compose);

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

    /// <summary>Loads a layout by KLID without making it active (for tools).</summary>
    public static IntPtr Load(string klid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LayoutReader works on Windows only.");
        IntPtr hkl = LoadKeyboardLayout(klid, KLF_NOTELLSHELL);
        if (hkl == IntPtr.Zero)
            throw new InvalidOperationException($"LoadKeyboardLayout({klid}) failed: {Marshal.GetLastWin32Error()}");
        return hkl;
    }

    /// <param name="klid">e.g. "00000409" (US), "00000419" (Russian)</param>
    public static List<LayoutEntry> Read(string klid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LayoutReader works on Windows only.");

        IntPtr hkl = LoadKeyboardLayout(klid, KLF_NOTELLSHELL);
        if (hkl == IntPtr.Zero)
            throw new InvalidOperationException($"LoadKeyboardLayout({klid}) failed: {Marshal.GetLastWin32Error()}");
        return Read(hkl);
    }

    /// <summary>
    /// Dead keys of the layout and what Windows prints for «dead key + next key», asked from ToUnicodeEx itself
    /// (so «'» + «c» → «ç», «'» + «[» → «'['»). Uses the calling thread's keyboard state and clears it afterwards.
    /// </summary>
    public static List<DeadEntry> ReadDead(IntPtr hkl)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LayoutReader works on Windows only.");
        var keys = new List<(uint Vk, uint Scan, bool Shift, bool Caps, byte[] State)>();
        for (uint vk = 0x20; vk <= 0xDF; vk++)
        {
            uint scan = MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, hkl);
            foreach (bool shift in new[] { false, true })
            foreach (bool caps in new[] { false, true })
            {
                var state = new byte[256];
                if (shift) state[VK_SHIFT] = 0x80;
                if (caps) state[VK_CAPITAL] = 0x01;
                keys.Add((vk, scan, shift, caps, state));
            }
        }

        var result = new List<DeadEntry>();
        var sb = new StringBuilder(8);
        foreach (var d in keys)
        {
            sb.Clear();
            if (ToUnicodeEx(d.Vk, d.Scan, d.State, sb, sb.Capacity, ToUnicodeNoStateChange, hkl) >= 0) continue;
            var spacing = sb.Length > 0 ? sb.ToString(0, 1) : "";
            Flush(hkl, sb);
            var compose = new List<LayoutEntry>();
            foreach (var k in keys)
            {
                sb.Clear();
                ToUnicodeEx(d.Vk, d.Scan, d.State, sb, sb.Capacity, 0, hkl);   // dead key: remembered in the thread's state
                sb.Clear();
                int n = ToUnicodeEx(k.Vk, k.Scan, k.State, sb, sb.Capacity, 0, hkl);
                if (n > 0)
                {
                    var s = sb.ToString(0, n);
                    if (!(s.Length == 1 && char.IsControl(s[0]))) compose.Add(new LayoutEntry((int)k.Vk, k.Shift, k.Caps, s));
                }
                else Flush(hkl, sb);   // dead + dead: the second one is pending now
            }
            result.Add(new DeadEntry((int)d.Vk, d.Shift, d.Caps, spacing, compose));
        }
        return result;
    }

    /// <summary>Clears a pending dead key from the thread's keyboard state.</summary>
    static void Flush(IntPtr hkl, StringBuilder sb)
    {
        for (int i = 0; i < 2; i++)
        {
            sb.Clear();
            ToUnicodeEx(0x20, MapVirtualKeyEx(0x20, MAPVK_VK_TO_VSC, hkl), new byte[256], sb, sb.Capacity, 0, hkl);
        }
    }

    /// <summary>Same table for an already loaded layout (e.g. one from GetKeyboardLayoutList).</summary>
    public static List<LayoutEntry> Read(IntPtr hkl)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LayoutReader works on Windows only.");

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

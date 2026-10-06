using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace VkSim
{
    // ============ 模型 ============
    public class KeyStep
    {
        public int Id;
        public string KeyName;   // 如 "a" 或 "ctrl+c"
        public int PressTime;    // 绝对毫秒
        public int ReleaseTime;  // 绝对毫秒

        public KeyStep() { }
        public KeyStep(int id, string name, int press, int release)
        { Id = id; KeyName = name; PressTime = press; ReleaseTime = release; }

        public int Duration { get { return ReleaseTime - PressTime; } }
        public string[] KeyTokens
        {
            get
            {
                if (KeyName == null) return new string[0];
                return KeyName.Split('+');
            }
        }
        public override string ToString()
        { return KeyName + " " + PressTime + "ms-" + ReleaseTime + "ms"; }
    }

    // ============ 按键定义 ============
    public class KeyDef
    {
        public string Label;
        public string Name;
        public KeyDef(string label, string name) { Label = label; Name = name; }
    }

    public class KeySlot
    {
        public KeyDef Def;
        public System.Drawing.RectangleF Rect;
        public KeySlot(KeyDef d, System.Drawing.RectangleF r) { Def = d; Rect = r; }
    }

    public static class KeyMap
    {
        public static Dictionary<string, int> NameToVk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<int, string> VkToName = new Dictionary<int, string>();
        public static HashSet<int> Extended = new HashSet<int>();

        private static void Add(string name, int vk, bool extended)
        {
            NameToVk[name] = vk;
            if (!VkToName.ContainsKey(vk)) VkToName[vk] = name;
            if (extended) Extended.Add(vk);
        }

        static KeyMap()
        {
            Add("escape", 0x1B, false);
            Add("esc", 0x1B, false);
            for (int i = 1; i <= 12; i++) Add("f" + i, 0x6F + i, false);
            Add("printscreen", 0x2C, false);
            Add("scrolllock", 0x91, false);
            Add("pause", 0x13, false);
            Add("`", 0xC0, false);
            Add("-", 0xBD, false);
            Add("=", 0xBB, false);
            Add("backspace", 0x08, false);
            Add("tab", 0x09, false);
            Add("[", 0xDB, false);
            Add("]", 0xDD, false);
            Add("\\", 0xDC, false);
            Add("capslock", 0x14, false);
            Add(";", 0xBA, false);
            Add("'", 0xDE, false);
            Add("enter", 0x0D, false);
            Add("shift", 0xA0, false);
            Add("leftshift", 0xA0, false);
            Add("rightshift", 0xA1, false);
            Add(",", 0xBC, false);
            Add(".", 0xBE, false);
            Add("/", 0xBF, false);
            Add("ctrl", 0xA2, false);
            Add("leftctrl", 0xA2, false);
            Add("rightctrl", 0xA3, true);
            Add("win", 0x5B, true);
            Add("leftwin", 0x5B, true);
            Add("rightwin", 0x5C, true);
            Add("alt", 0xA4, false);
            Add("leftalt", 0xA4, false);
            Add("rightalt", 0xA5, true);
            Add("space", 0x20, false);
            Add("menu", 0x5D, true);
            Add("insert", 0x2D, true);
            Add("ins", 0x2D, true);
            Add("home", 0x24, true);
            Add("pageup", 0x21, true);
            Add("pgup", 0x21, true);
            Add("delete", 0x2E, true);
            Add("del", 0x2E, true);
            Add("end", 0x23, true);
            Add("pagedown", 0x22, true);
            Add("pgdn", 0x22, true);
            Add("up", 0x26, true);
            Add("down", 0x28, true);
            Add("left", 0x25, true);
            Add("right", 0x27, true);
            Add("numlock", 0x90, false);
            Add("num/", 0x6F, false);
            Add("num*", 0x6A, false);
            Add("num-", 0x6D, false);
            Add("num+", 0x6B, false);
            Add("numenter", 0x0D, true);
            Add("num0", 0x60, false); Add("num1", 0x61, false); Add("num2", 0x62, false);
            Add("num3", 0x63, false); Add("num4", 0x64, false); Add("num5", 0x65, false);
            Add("num6", 0x66, false); Add("num7", 0x67, false); Add("num8", 0x68, false);
            Add("num9", 0x69, false);
            Add("num.", 0x6E, false);

            // 数字（主键区上方 1~0，vk = '0'..'9'，即 0x30..0x39）
            for (int i = 0; i <= 9; i++) Add(i.ToString(), 0x30 + i, false);

            // 字母
            for (int i = 0; i < 26; i++)
            {
                char c = (char)('a' + i);
                Add(c.ToString(), (int)char.ToUpperInvariant(c), false);
            }
        }

        public static bool IsValid(string name)
        { return NameToVk.ContainsKey(name); }

        public static int VkOf(string name)
        { int v; if (NameToVk.TryGetValue(name, out v)) return v; return 0; }

        public static bool IsExtended(int vk) { return Extended.Contains(vk); }

        // 从 vk 反查显示名（录制用）
        public static string NameOf(int vk)
        { string s; if (VkToName.TryGetValue(vk, out s)) return s; return null; }
    }

    // ============ 原生按键注入（keybd_event + 扫描码，x64 安全） ============
    public static class Native
    {
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uMilliseconds);
        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uMilliseconds);

        private static void Send(bool down, int vk)
        {
            uint flags = 0;
            if (KeyMap.IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= KEYEVENTF_KEYUP;
            keybd_event((byte)vk, (byte)MapVirtualKey((uint)vk, 0), flags, UIntPtr.Zero);
        }

        public static void KeyDown(int vk) { Send(true, vk); }
        public static void KeyUp(int vk) { Send(false, vk); }

        // 组合键整体按下/松开
        public static void PressCombo(string[] tokens)
        {
            foreach (string t in tokens) KeyDown(KeyMap.VkOf(t.Trim()));
        }
        public static void ReleaseCombo(string[] tokens)
        {
            for (int i = tokens.Length - 1; i >= 0; i--) KeyUp(KeyMap.VkOf(tokens[i].Trim()));
        }
    }

    // ============ 毫秒级时钟/调度 ============
    public class PreciseClock
    {
        private Stopwatch sw;
        public PreciseClock() { sw = Stopwatch.StartNew(); }
        public double ElapsedMs { get { return sw.Elapsed.TotalMilliseconds; } }
        public void SleepUntil(double targetMs)
        {
            while (sw.Elapsed.TotalMilliseconds < targetMs - 1.2) Thread.Sleep(1);
            while (sw.Elapsed.TotalMilliseconds < targetMs) { /* 忙等补齐毫秒 */ }
        }
    }

    // ============ 全局键盘录制钩子 ============
    public class KeyRecorder : IDisposable
    {
        public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private IntPtr hook = IntPtr.Zero;
        private LowLevelHookProc proc;
        public event Action<int, bool> KeyEvent;

        public bool Install()
        {
            proc = new LowLevelHookProc(HookCallback);
            using (Process cur = Process.GetCurrentProcess())
            using (ProcessModule mod = cur.MainModule)
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(mod.ModuleName), 0);
            return hook != IntPtr.Zero;
        }

        public void Uninstall()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int msg = wParam.ToInt32();
                bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool isUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);
                if (isDown || isUp)
                {
                    if (KeyEvent != null) KeyEvent((int)k.vkCode, isDown);
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose() { Uninstall(); }
    }
}

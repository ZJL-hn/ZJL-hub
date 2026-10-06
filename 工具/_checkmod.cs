// 快捷输入模组 校验工具（无界面）：验证 QuickPlanner 计划逻辑 + 模组 dll 可加载。
// 编译（在 工具 目录）：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /out:_checkmod.exe ^
//     /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
//     /r:"..\虚拟键盘模拟器.exe" /r:"..\mods\QuickInputMod.dll" _checkmod.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using VkSim;
using VkSim.ModQuick;

class CheckMod
{
    // 运行时从 工具 目录启动，exe/dll 不在本目录：先装好程序集解析器
    static CheckMod()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name;
            string[] cands = { @"..\虚拟键盘模拟器.exe", @"..\mods\QuickInputMod.dll" };
            foreach (string c in cands)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full))
                {
                    AssemblyName an = AssemblyName.GetAssemblyName(full);
                    if (string.Equals(an.Name, name, StringComparison.OrdinalIgnoreCase))
                        return Assembly.LoadFrom(full);
                }
            }
            return null;
        };
    }

    static int _fails = 0;
    static void Eq(string name, object got, object want)
    {
        bool ok = object.Equals(got, want);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " -> got=" + got + (ok ? "" : "  want=" + want));
        if (!ok) _fails++;
    }
    static void True(string name, bool cond, string detail)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond ? "" : "  " + detail));
        if (!cond) _fails++;
    }

    static int Main()
    {
        try { return Run(); }
        catch (Exception ex)
        {
            File.WriteAllText("_checkmod.err.txt", ex.ToString());
            Console.WriteLine("EXCEPTION: " + ex.Message);
            return 2;
        }
    }

    static int Run()
    {
        // 1) 字符映射
        Eq("KeyFromChar('a')", QuickPlanner.KeyFromChar('a'), "a");
        Eq("KeyFromChar('A')", QuickPlanner.KeyFromChar('A'), "shift+a");
        Eq("KeyFromChar('9')", QuickPlanner.KeyFromChar('9'), "9");
        Eq("KeyFromChar(' ') ", QuickPlanner.KeyFromChar(' '), "space");
        Eq("KeyFromChar('\\n')", QuickPlanner.KeyFromChar('\n'), "enter");
        Eq("KeyFromChar('!') ", QuickPlanner.KeyFromChar('!'), "shift+1");
        Eq("KeyFromChar('中') ", QuickPlanner.KeyFromChar('中'), null);

        // 2) 定时输入：吸附背靠背，总时间 1000ms，无每键时长
        int skip; string note;
        List<KeyStepSpec> s1 = QuickPlanner.PlanTimed("Hello 123", 1000, false, 0, true, out skip, out note);
        Eq("PlanTimed[Hello 123].count", s1.Count, 9);
        Eq("PlanTimed.skip", skip, 0);
        int span1 = s1[s1.Count - 1].Release;
        True("PlanTimed.span<=1000", span1 <= 1000, "span=" + span1);
        True("PlanTimed.sequential", IsSequential(s1), "overlap detected");
        Eq("PlanTimed.firstKey", s1[0].Key, "shift+h");
        True("PlanTimed.dur=floor(T/N)", (s1[1].Release - s1[1].Press) == 111, "dur=" + (s1[1].Release - s1[1].Press));

        // 3) 定时输入：指定每键时长超出容量 -> 压缩到 总时间/N
        List<KeyStepSpec> s2 = QuickPlanner.PlanTimed("abc", 300, true, 200, true, out skip, out note);
        True("PlanTimed.clamp.dur==100", (s2[0].Release - s2[0].Press) == 100, "dur=" + (s2[0].Release - s2[0].Press));
        True("PlanTimed.clamp.span<=300", s2[2].Release <= 300, "span=" + s2[2].Release);
        True("PlanTimed.clamp.note", note != "", "note empty");
        Eq("PlanTimed.clamp.count", s2.Count, 3);

        // 4) 定时输入：非吸附（分散到总时间）
        List<KeyStepSpec> s3 = QuickPlanner.PlanTimed("ab", 1000, true, 100, false, out skip, out note);
        Eq("PlanTimed.spread.span", s3[1].Release, 1000);
        True("PlanTimed.spread.sequential", IsSequential(s3), "overlap");
        Eq("PlanTimed.spread[1].press", s3[1].Press, 900);

        // 5) 快速编排：混合吸附/等待/和弦
        List<ArrItem> rows = new List<ArrItem>();
        rows.Add(new ArrItem("a", 100, true, 0));        // 0-100
        rows.Add(new ArrItem("s", 100, true, 0));        // 100-200
        rows.Add(new ArrItem("ctrl+shift+a", 120, true, 0)); // 200-320（和弦）
        rows.Add(new ArrItem("enter", 100, false, 200)); // 非吸附+等待200 -> 520-620
        List<KeyStepSpec> s4 = QuickPlanner.PlanArrangement(rows);
        Eq("Arr.count", s4.Count, 4);
        Eq("Arr[0] ", s4[0].Press, 0);  Eq("Arr[0].rel", s4[0].Release, 100);
        Eq("Arr[1].press", s4[1].Press, 100);
        Eq("Arr[2].key", s4[2].Key, "ctrl+shift+a"); Eq("Arr[2].press", s4[2].Press, 200); Eq("Arr[2].rel", s4[2].Release, 320);
        Eq("Arr[3].press", s4[3].Press, 520); Eq("Arr[3].rel", s4[3].Release, 620);
        True("Arr.sequential", IsSequential(s4), "overlap");

        // 6) 非吸附编排：等待间隙
        List<ArrItem> rows2 = new List<ArrItem>();
        rows2.Add(new ArrItem("a", 80, false, 50));  // 50-130
        rows2.Add(new ArrItem("b", 80, false, 30));  // 160-240
        List<KeyStepSpec> s5 = QuickPlanner.PlanArrangement(rows2);
        Eq("Arr2[0].press", s5[0].Press, 50);
        Eq("Arr2[1].press", s5[1].Press, 160);

        // 7) 模组 dll 可加载 & 实现 IVkMod
        Assembly asm = Assembly.LoadFrom(@"..\mods\QuickInputMod.dll");
        int found = 0;
        foreach (Type t in asm.GetTypes())
        {
            if (t.IsClass && !t.IsAbstract && typeof(IVkMod).IsAssignableFrom(t))
            {
                IVkMod m = (IVkMod)Activator.CreateInstance(t);
                Eq("Mod.Name", m.Name, "快捷输入模组");
                Eq("Mod implements ICommandMod", (m is ICommandMod), true);
                Eq("Mod implements IUiMod", (m is IUiMod), true);
                m.Initialize(null);
                if (m is ICommandMod) ((ICommandMod)m).OnCommand(null);
                IUiMod u = m as IUiMod;
                Eq("UiTitle", u == null ? "" : u.UiTitle, "快捷输入");
                True("UiContent!=null after Initialize", u != null && u.UiContent != null, "ui content null");
                m.Dispose();
                found++;
            }
        }
        Eq("Mod.impl count", found, 1);

        // 8) 注入面板能真实构造（STA 线程），且结构正确：2 个标签页 + “吸附”是复选框列
        bool uiConstructs = false;
        bool uiHas2Tabs = false, uiHasCheckbox = false;
        Thread uiThread = new Thread(delegate()
        {
            try
            {
                QuickInputPanel pan = new QuickInputPanel(null);
                uiConstructs = true;
                TabControl tc = FindChild<TabControl>(pan);
                uiHas2Tabs = tc != null && tc.TabPages.Count == 2;
                DataGridView dg = FindChild<DataGridView>(pan);
                uiHasCheckbox = dg != null && dg.Columns.Count >= 4 && dg.Columns[2] is DataGridViewCheckBoxColumn;
                pan.Dispose();
            }
            catch (Exception ex) { Console.WriteLine("PANEL CONSTRUCT FAIL: " + ex.Message); }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start(); uiThread.Join();
        True("Panel.constructs(STA)", uiConstructs, "panel construction threw");
        True("Panel.has 2 tabs", uiHas2Tabs, "tab count wrong");
        True("Panel.snap col is checkbox", uiHasCheckbox, "snap column not checkbox");

        // 主程序模组注入接口已存在（AddModUi / RemoveModUi）
        Type ic = typeof(IVkContext);
        True("IVkContext.AddModUi", ic.GetMethod("AddModUi") != null, "missing AddModUi");
        True("IVkContext.RemoveModUi", ic.GetMethod("RemoveModUi") != null, "missing RemoveModUi");
        Type mf = typeof(MainForm);
        System.Reflection.BindingFlags fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        True("MainForm.ModAddUi", mf.GetMethod("ModAddUi", fl) != null, "missing ModAddUi");
        True("MainForm.ModRemoveUi", mf.GetMethod("ModRemoveUi", fl) != null, "missing ModRemoveUi");

        // 9) 主窗口可构造（含新的右栏模组面板布局路径）
        bool appConstructs = false;
        Thread appThread = new Thread(delegate()
        {
            try
            {
                using (MainForm f = new MainForm()) { appConstructs = f.ClientSize.Width > 0; }
            }
            catch (Exception ex) { Console.WriteLine("MAINFORM CONSTRUCT FAIL: " + ex.Message); }
        });
        appThread.SetApartmentState(ApartmentState.STA);
        appThread.Start(); appThread.Join();
        True("MainForm.constructs(STA)", appConstructs, "main form construction threw");

        // 10) 端到端注入：ModLoader.LoadOne 载入真实 dll → 自动注入“快捷输入”页；卸载 → 移除并隐藏面板
        bool injectOk = false, removeOk = false, toggleOk = false;
        Thread injThread = new Thread(delegate()
        {
            try
            {
                using (MainForm f = new MainForm())
                {
                    ModContext mc = new ModContext(f, ".");
                    IVkMod m = ModLoader.LoadOne(Path.GetFullPath(@"..\mods\QuickInputMod.dll"), mc, null);
                    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    TabControl tabs = typeof(MainForm).GetField("modTabs", bf).GetValue(f) as TabControl;
                    // 窗体未显示时 Control.Visible 恒为 false，用内部 state 的 STATE_VISIBLE 位判断布局是否已显示面板
                    bool vis = StateVisible(tabs);
                    injectOk = m != null && tabs != null && vis && CountPagesByTitle(tabs, "快捷输入") == 1;
                    // 缩起/展开模组栏按钮与切换
                    object toggleBtn = typeof(MainForm).GetField("btnModToggle", bf).GetValue(f);
                    var tm = typeof(MainForm).GetMethod("ToggleModPanel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    tm.Invoke(f, null);                 // 缩起
                    bool coll1 = !StateVisible(tabs);
                    tm.Invoke(f, null);                 // 展开
                    bool coll2 = StateVisible(tabs);
                    toggleOk = toggleBtn != null && coll1 && coll2;
                    IUiMod u = m as IUiMod;
                    if (u != null) mc.RemoveModUi(u.UiContent);
                    if (m != null) m.Dispose();
                    removeOk = tabs != null && CountPagesByTitle(tabs, "快捷输入") == 0 && !StateVisible(tabs);
                }
            }
            catch (Exception ex) { Console.WriteLine("INJECT TEST FAIL: " + ex.Message); }
        });
        injThread.SetApartmentState(ApartmentState.STA);
        injThread.Start(); injThread.Join();
        True("LoadOne auto-injects tab", injectOk, "auto injection failed");
        True("ModToggle button collapses/expands", toggleOk, "toggle failed");
        True("RemoveModUi hides panel", removeOk, "removal failed");

        Console.WriteLine(_fails == 0 ? "== ALL PASS ==" : "== " + _fails + " FAILED ==");
        return _fails == 0 ? 0 : 1;
    }

    static bool IsSequential(List<KeyStepSpec> s)
    {
        for (int i = 1; i < s.Count; i++)
            if (s[i].Press < s[i - 1].Release) return false;
        return true;
    }

    static T FindChild<T>(Control root) where T : class
    {
        if (root is T) return root as T;
        foreach (Control c in root.Controls)
        {
            T r = FindChild<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    static int CountPagesByTitle(TabControl tabs, string title)
    {
        int n = 0;
        foreach (TabPage p in tabs.TabPages) if (p.Text == title) n++;
        return n;
    }
    static bool StateVisible(Control c)
    {
        if (c == null) return false;
        var fi = typeof(Control).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (fi == null) return c.Visible;
        int s = (int)fi.GetValue(c);
        return (s & 0x2) != 0;   // STATE_VISIBLE
    }
}

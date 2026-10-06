using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace VkSim
{
    // ============ 模组接口（拓展文件需实现本接口） ============
    // 载入后应用会实例化并调用 Initialize，把上下文注入模组；模组通过上下文读写步骤、注入按键、订阅事件。
    public interface IVkMod
    {
        string Name { get; }          // 模组名（显示用）
        string Description { get; }   // 模组说明
        void Initialize(IVkContext ctx);
        void Dispose();
    }

    // ============ 可操作模组（可选）：在“模组管理”窗口提供一个“操作”按钮入口 ============
    // 实现本接口的模组，会在模组管理窗口出现“操作/导入…”按钮，点击时调用 OnCommand。
    public interface ICommandMod : IVkMod
    {
        void OnCommand(IVkContext ctx);   // 用户点击“操作”按钮时触发（可弹文件选择框等）
    }

    // ============ UI 注入模组（可选）：把模组界面直接挂到主程序右侧面板 ============
    // 实现本接口的模组无需点“操作/导入…”弹窗：Initialize 里建好控件后，
    // 加载器会自动把它作为新标签页注入主程序主界面；卸载时自动移除。
    public interface IUiMod : IVkMod
    {
        string UiTitle { get; }       // 注入界面的标签页标题
        Control UiContent { get; }    // 要注入主程序右侧面板的控件（Initialize 中创建，非 null）
    }

    // ============ 上下文（模组与应用交互的唯一通道） ============
    public interface IVkContext
    {
        string ModDir { get; }                    // 模组所在目录（exe 同目录 \mods）
        IList<KeyStep> Steps { get; }             // 步骤列表（可遍历/修改）
        void AddStep(string key, int press, int release);   // 追加一步（自动分配 Id 并排序刷新）
        void RemoveStep(KeyStep st);              // 删除指定步骤
        void ClearSteps();                        // 清空全部步骤
        void InjectKeys(string combo);            // 立即按下再松开组合键（如 "ctrl+c"）
        void SetStatus(string msg);               // 设置状态栏文字
        void SetKeyNameMapper(Func<string, string> mapper); // 设置键名映射器：点击/录制时转换键名（返回原键名表示不变；返回 null 保持原键）
        double SpeedPercent { get; set; }         // 读写主程序播放速度%（100 = 原速）；音乐模组 BPM 联动用
        event Action<KeyStep> StepAdded;          // 有步骤被添加
        event Action<KeyStep> StepRemoved;        // 有步骤被删除
        event Action PlayStarted;                 // 播放开始
        event Action PlayEnded;                   // 播放结束（含急停）
        // 注意：新成员只能追加到接口末尾，不可插到中间，否则已编译模组的接口方法槽位会移位
        Control AddModUi(string title, Control content);   // 把模组界面作为标签页注入主窗口右侧（无需弹窗）；返回承载标签页
        void RemoveModUi(Control content);                // 卸载时移除注入的界面
    }

    // ============ 上下文实现 ============
    public class ModContext : IVkContext
    {
        private MainForm _f;
        private Func<string, string> _mapper;
        public string ModDir { get; private set; }
        public IList<KeyStep> Steps { get { return _f.ModSteps; } }
        public event Action<KeyStep> StepAdded;
        public event Action<KeyStep> StepRemoved;
        public event Action PlayStarted;
        public event Action PlayEnded;

        public ModContext(MainForm f, string dir) { _f = f; ModDir = dir; }

        public void AddStep(string key, int press, int release) { _f.ModAddStep(key, press, release); }
        public void RemoveStep(KeyStep st) { _f.ModRemoveStep(st); }
        public void ClearSteps() { _f.ModClear(); }
        public void InjectKeys(string combo) { _f.ModInject(combo); }
        public void SetStatus(string msg) { _f.ModSetStatus(msg); }
        public void SetKeyNameMapper(Func<string, string> mapper) { _mapper = mapper; }
        public Control AddModUi(string title, Control content) { return _f.ModAddUi(title, content); }
        public void RemoveModUi(Control content) { _f.ModRemoveUi(content); }
        public double SpeedPercent { get { return _f.ModGetSpeed(); } set { _f.ModSetSpeed(value); } }

        // 应用在点击/录制时调用：对键名应用映射（映射返回 null 时保持原键）
        internal string Map(string name)
        { return _mapper == null ? name : (_mapper(name) ?? name); }

        public void RaiseStepAdded(KeyStep st) { if (StepAdded != null) StepAdded(st); }
        public void RaiseStepRemoved(KeyStep st) { if (StepRemoved != null) StepRemoved(st); }
        public void RaisePlayStarted() { if (PlayStarted != null) PlayStarted(); }
        public void RaisePlayEnded() { if (PlayEnded != null) PlayEnded(); }
    }

    // ============ 模组加载器 ============
    public static class ModLoader
    {
        // 扫描目录下所有 *.dll，加载其中实现了 IVkMod 的类
        public static List<IVkMod> LoadAll(string dir, IVkContext ctx, Action<string> log)
        {
            List<IVkMod> mods = new List<IVkMod>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return mods;
            foreach (string dll in Directory.GetFiles(dir, "*.dll"))
            {
                IVkMod m = LoadOne(dll, ctx, log);
                if (m != null) mods.Add(m);
            }
            return mods;
        }

        public static IVkMod LoadOne(string dll, IVkContext ctx, Action<string> log)
        {
            try
            {
                Assembly asm = Assembly.LoadFrom(dll);
                foreach (Type t in asm.GetTypes())
                {
                    if (t.IsClass && !t.IsAbstract && typeof(IVkMod).IsAssignableFrom(t))
                    {
                        IVkMod m = (IVkMod)Activator.CreateInstance(t);
                        m.Initialize(ctx);
                        // UI 注入模组：Initialize 之后自动把界面挂到主程序右侧面板，无需“操作/导入…”弹窗
                        IUiMod u = m as IUiMod;
                        if (u != null && ctx != null && u.UiContent != null)
                        {
                            try
                            {
                                ctx.AddModUi(u.UiTitle, u.UiContent);
                                if (log != null) log("模组界面已注入: " + m.Name);
                            }
                            catch (Exception ex) { if (log != null) log("模组界面注入失败 " + m.Name + ": " + ex.Message); }
                        }
                        if (log != null) log("已载入模组: " + m.Name);
                        return m;
                    }
                }
                if (log != null) log("该文件未包含模组实现: " + Path.GetFileName(dll));
            }
            catch (Exception ex) { if (log != null) log("模组加载失败 " + Path.GetFileName(dll) + ": " + ex.Message); }
            return null;
        }
    }

    // ============ 模组管理窗体 ============
    public class ModListForm : Form
    {
        private ListBox list;
        private List<IVkMod> _mods;
        private IVkContext _ctx;
        private Action<string> _log;
        private float _scale = 1f;

        private int S(float v) { return (int)(v * _scale + 0.5f); }

        public ModListForm(List<IVkMod> mods, IVkContext ctx, Action<string> log)
        {
            _mods = mods; _ctx = ctx; _log = log;
            // 进程 DPI-aware：9pt 字体已按系统 DPI 自动放大，控件布局须按同一比例缩放，否则文字溢出/重叠
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                _scale = g.DpiX / 96f;
            if (_scale < 1f) _scale = 1f;

            Text = "模组管理";
            ClientSize = new Size(S(440), S(312));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(38, 42, 50);
            Font = new Font("Segoe UI", 9f);

            Label tip = new Label(); tip.Text = "模组目录: " + (ctx == null ? "" : ctx.ModDir) + "（启动时自动扫描）";
            tip.ForeColor = Color.FromArgb(170, 233, 236, 241); tip.BackColor = Color.Transparent;
            tip.Location = new Point(S(10), S(8)); tip.Size = new Size(S(420), S(20)); tip.AutoSize = false;
            Controls.Add(tip);

            list = new ListBox();
            list.Location = new Point(S(10), S(32)); list.Size = new Size(S(420), S(208));
            list.BorderStyle = BorderStyle.FixedSingle;
            list.BackColor = Color.FromArgb(40, 45, 54); list.ForeColor = Color.FromArgb(233, 236, 241);
            Controls.Add(list);

            Button btnLoad = new Button(); btnLoad.Text = "载入扩展…";
            btnLoad.Location = new Point(S(10), S(250)); btnLoad.Size = new Size(S(110), S(32));
            btnLoad.FlatStyle = FlatStyle.Flat; btnLoad.FlatAppearance.BorderSize = 0;
            btnLoad.BackColor = Color.FromArgb(0, 120, 212); btnLoad.ForeColor = Color.White;
            btnLoad.Cursor = Cursors.Hand;
            btnLoad.Click += (s, e) => LoadExt();
            Controls.Add(btnLoad);

            Button btnUnload = new Button(); btnUnload.Text = "卸载全部";
            btnUnload.Location = new Point(S(130), S(250)); btnUnload.Size = new Size(S(100), S(32));
            Style(btnUnload);
            btnUnload.Click += (s, e) =>
            {
                foreach (IVkMod m in _mods)
                {
                    // 先移除注入界面，再释放模组
                    IUiMod u = m as IUiMod;
                    if (u != null && _ctx != null) try { _ctx.RemoveModUi(u.UiContent); } catch { }
                    try { m.Dispose(); } catch { }
                }
                _mods.Clear();
                if (_ctx != null) _ctx.SetKeyNameMapper(null);   // 卸载后清除键名映射器，点击/录制恢复原始键
                Refresh(); RefreshList();
                if (_log != null) _log("已卸载全部模组，键名映射已重置（录制/点击恢复原始键）");
            };
            Controls.Add(btnUnload);

            // “操作”按钮：对实现 ICommandMod 的模组开放（如音乐模组导入 MIDI）
            Button btnCmd = new Button(); btnCmd.Text = "操作/导入…";
            btnCmd.Location = new Point(S(240), S(250)); btnCmd.Size = new Size(S(100), S(32));
            Style(btnCmd);
            btnCmd.Click += (s, e) =>
            {
                IVkMod sel = (list.SelectedIndex >= 0 && list.SelectedIndex < _mods.Count) ? _mods[list.SelectedIndex] : null;
                if (sel is ICommandMod) { try { ((ICommandMod)sel).OnCommand(_ctx); } catch (Exception ex) { _log(ex.Message); } }
                else { _log("请先选中一个支持“操作”的模组"); }
            };
            Controls.Add(btnCmd);

            Button btnClose = new Button(); btnClose.Text = "关闭";
            btnClose.Location = new Point(S(350), S(250)); btnClose.Size = new Size(S(80), S(32));
            Style(btnClose);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            RefreshList();
        }

        private void Style(Button b)
        { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = Color.FromArgb(60, 66, 78); b.ForeColor = Color.FromArgb(233, 236, 241); b.Cursor = Cursors.Hand; }

        private void RefreshList()
        {
            list.Items.Clear();
            foreach (IVkMod m in _mods)
                list.Items.Add(m.Name + " — " + m.Description);
            if (_mods.Count == 0) list.Items.Add("（当前未加载任何模组）");
        }

        private void LoadExt()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "拓展模组 (*.dll)|*.dll|所有文件 (*.*)|*.*";
                d.Title = "载入拓展模组";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                IVkMod m = ModLoader.LoadOne(d.FileName, _ctx, _log);
                if (m != null) { _mods.Add(m); RefreshList(); }
            }
        }
    }
}

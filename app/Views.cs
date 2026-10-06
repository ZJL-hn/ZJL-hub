using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VkSim
{
    // ============ 圆角/玻璃绘制助手 ============
    public static class R
    {
        public static GraphicsPath Path(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0) { p.AddRectangle(r); return p; }
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static void Fill(Graphics g, Brush b, RectangleF r, float rad)
        {
            using (GraphicsPath p = Path(r, rad)) g.FillPath(b, p);
        }
        public static void Stroke(Graphics g, Pen pen, RectangleF r, float rad)
        {
            using (GraphicsPath p = Path(r, rad)) g.DrawPath(pen, p);
        }
        public static void Center(Graphics g, string s, Font f, Brush b, RectangleF r)
        {
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), (Color)(b is SolidBrush ? ((SolidBrush)b).Color : Color.White), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // ============ 键盘视图 ============
    public class KeyboardView : Panel
    {
        public List<KeySlot> Slots = new List<KeySlot>();
        public HashSet<string> DownKeys = new HashSet<string>();
        private int hover = -1;
        public event Action<string, bool> KeyClicked;

        private const float KWB = 44, KHB = 44, GAPB = 6;   // 基准尺寸
        private float _scale = 1f;
        private float KW { get { return KWB * _scale; } }
        private float KH { get { return KHB * _scale; } }
        private float GAP { get { return GAPB * _scale; } }

        public KeyboardView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            BuildLayout();
            MouseMove += (s, e) => { int idx = Hit(e.X, e.Y); if (idx != hover) { hover = idx; Invalidate(); } };
            MouseLeave += (s, e) => { if (hover != -1) { hover = -1; Invalidate(); } };
            MouseDown += (s, e) =>
            {
                int idx = Hit(e.X, e.Y);
                if (idx >= 0 && KeyClicked != null)
                    KeyClicked(Slots[idx].Def.Name, (Control.ModifierKeys & Keys.Control) != 0);
            };
        }

        public void SetScale(float s)
        {
            if (Math.Abs(s - _scale) < 0.001f) return;
            _scale = s;
            Slots.Clear();
            BuildLayout();
            Invalidate();
        }

        private void Slot(string label, string name, ref float x, ref float y, float w, float h)
        {
            Slots.Add(new KeySlot(new KeyDef(label, name), new RectangleF(x, y, w, h)));
            x += w + GAP;
        }

        private void BuildLayout()
        {
            float x = 8, y = 8;

            // ---- 主键区 6 行 ----
            float[] row1 = new float[16];
            string[] fkeys = new string[] { "Esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PrtSc", "ScrLk", "Pause" };
            string[] fnames = new string[] { "escape", "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12", "printscreen", "scrolllock", "pause" };
            x = 8;
            for (int i = 0; i < 16; i++) Slot(fkeys[i], fnames[i], ref x, ref y, KW, KH);
            x = 8; y += KH + GAP;

            string[] r1l = new string[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "Bksp" };
            string[] r1n = new string[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "backspace" };
            for (int i = 0; i < 13; i++) Slot(r1l[i], r1n[i], ref x, ref y, KW, KH);
            Slot("Bksp", "backspace", ref x, ref y, KW * 2 + GAP, KH);
            x = 8; y += KH + GAP;

            Slot("Tab", "tab", ref x, ref y, KW * 1.5f + GAP / 2, KH);
            string q = "qwertyuiop";
            for (int i = 0; i < q.Length; i++) Slot(q[i].ToString().ToUpper(), q[i].ToString(), ref x, ref y, KW, KH);
            Slot("[", "[", ref x, ref y, KW, KH);
            Slot("]", "]", ref x, ref y, KW, KH);
            Slot("\\", "\\", ref x, ref y, KW, KH);
            x = 8; y += KH + GAP;

            Slot("Caps", "capslock", ref x, ref y, KW * 1.75f + GAP * 0.75f, KH);
            string a = "asdfghjkl";
            for (int i = 0; i < a.Length; i++) Slot(a[i].ToString().ToUpper(), a[i].ToString(), ref x, ref y, KW, KH);
            Slot(";", ";", ref x, ref y, KW, KH);
            Slot("'", "'", ref x, ref y, KW, KH);
            Slot("Enter", "enter", ref x, ref y, KW * 2.25f + GAP * 1.25f, KH);
            x = 8; y += KH + GAP;

            Slot("Shift", "shift", ref x, ref y, KW * 2.25f + GAP * 1.25f, KH);
            string z = "zxcvbnm";
            for (int i = 0; i < z.Length; i++) Slot(z[i].ToString().ToUpper(), z[i].ToString(), ref x, ref y, KW, KH);
            Slot(",", ",", ref x, ref y, KW, KH);
            Slot(".", ".", ref x, ref y, KW, KH);
            Slot("/", "/", ref x, ref y, KW, KH);
            Slot("Shift", "shift", ref x, ref y, KW * 2.75f + GAP * 1.75f, KH);
            x = 8; y += KH + GAP;

            float[] bottomW = new float[] { 1.25f, 1.25f, 1.25f, 6.25f, 1.25f, 1.25f, 1.25f, 1.25f };
            string[] bl = new string[] { "Ctrl", "Win", "Alt", "Space", "Alt", "Win", "Menu", "Ctrl" };
            string[] bn = new string[] { "ctrl", "win", "alt", "space", "alt", "win", "menu", "ctrl" };
            float acc = 0;
            for (int i = 0; i < 8; i++) { Slot(bl[i], bn[i], ref x, ref y, KW * bottomW[i] + GAP * (bottomW[i] - 1), KH); acc = bottomW[i]; }
            x = 8; y += KH + GAP;

            float mainBottom = y;

            // ---- 编辑区（右侧三列） ----
            float ex = x + RowsMaxWidth() + 14;
            float ey = 8;
            SlotEx("Ins", "insert", ex, ey, 0, 0, KW, KH);
            SlotEx("Home", "home", ex, ey, 0, 1, KW, KH);
            SlotEx("PgUp", "pageup", ex, ey, 0, 2, KW, KH);
            SlotEx("Del", "delete", ex, ey, 1, 0, KW, KH);
            SlotEx("End", "end", ex, ey, 1, 1, KW, KH);
            SlotEx("PgDn", "pagedown", ex, ey, 1, 2, KW, KH);
            SlotEx("", "", ex, ey, 2, 0, KW, KH);
            SlotEx("↑", "up", ex, ey, 2, 1, KW, KH);
            SlotEx("←", "left", ex, ey, 3, 0, KW, KH);
            SlotEx("↓", "down", ex, ey, 3, 1, KW, KH);
            SlotEx("→", "right", ex, ey, 3, 2, KW, KH);

            // ---- 数字小键盘 ----
            float nx = ex + 3 * KW + 2 * GAP + 14;
            float ny = 8;
            NP("Num", "numlock", nx, ny, 0, 0, 1, 1);
            NP("Num/", "num/", nx, ny, 0, 1, 1, 1);
            NP("Num*", "num*", nx, ny, 0, 2, 1, 1);
            NP("Num-", "num-", nx, ny, 0, 3, 1, 1);
            NP("7", "num7", nx, ny, 1, 0, 1, 1);
            NP("8", "num8", nx, ny, 1, 1, 1, 1);
            NP("9", "num9", nx, ny, 1, 2, 1, 1);
            NP("+", "num+", nx, ny, 1, 3, 2, 1);
            NP("4", "num4", nx, ny, 2, 0, 1, 1);
            NP("5", "num5", nx, ny, 2, 1, 1, 1);
            NP("6", "num6", nx, ny, 2, 2, 1, 1);
            NP("1", "num1", nx, ny, 3, 0, 1, 1);
            NP("2", "num2", nx, ny, 3, 1, 1, 1);
            NP("3", "num3", nx, ny, 3, 2, 1, 1);
            NP("Enter", "numenter", nx, ny, 3, 3, 2, 1);
            NP("0", "num0", nx, ny, 4, 0, 1, 2);
            NP(".", "num.", nx, ny, 4, 2, 1, 1);
        }

        private void SlotEx(string label, string name, float ox, float oy, int row, int col, float w, float h)
        {
            Slots.Add(new KeySlot(new KeyDef(label, name), new RectangleF(ox + col * (KW + GAP), oy + row * (KH + GAP), w, h)));
        }
        private void NP(string label, string name, float ox, float oy, int row, int col, int rspan, int cspan)
        {
            float w = cspan * KW + (cspan - 1) * GAP;
            float h = rspan * KH + (rspan - 1) * GAP;
            Slots.Add(new KeySlot(new KeyDef(label, name), new RectangleF(ox + col * (KW + GAP), oy + row * (KH + GAP), w, h)));
        }
        private float RowsMaxWidth()
        {
            // 主区最大行宽（row0 16 键）
            return 16 * KW + 15 * GAP;
        }

        private int Hit(int mx, int my)
        {
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i].Rect.Contains(mx, my)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Font f = new Font("Segoe UI", 9f, FontStyle.Regular);
            for (int i = 0; i < Slots.Count; i++)
            {
                KeySlot s = Slots[i];
                if (s.Def.Name.Length == 0) continue;
                Color c = Color.FromArgb(58, 63, 74);
                if (DownKeys.Contains(s.Def.Name)) c = Color.FromArgb(232, 163, 61);
                else if (i == hover) c = Color.FromArgb(74, 81, 96);
                using (SolidBrush b = new SolidBrush(c))
                    R.Fill(g, b, s.Rect, 6);
                using (Pen p = new Pen(Color.FromArgb(255, 255, 255, 36)))
                    R.Stroke(g, p, s.Rect, 6);
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(240, 243, 246)))
                    R.Center(g, s.Def.Label, f, tb, s.Rect);
            }
            f.Dispose();
        }
    }

    // ============ 时间轴视图 ============
    public class TimelineView : Panel
    {
        public List<KeyStep> Steps = new List<KeyStep>();
        public event Action<KeyStep> StepSelected;
        public event Action<KeyStep> StepEdited;
        public double CursorMs = -1;
        private double totalMs = 2000;   // 内容总时长（所有步骤末端 + 余量）
        private double viewMs = 2000;    // 当前视窗跨度（放大缩小）
        private double scrollMs = 0;     // 视窗起点（平移查看）
        private int selected = -1;
        private float _scale = 1f;
        private enum DragMode { None, ResizeL, ResizeR, Move, Pan }
        private DragMode drag = DragMode.None;
        private int dragIndex = -1;
        private double grabMs = 0;
        private double panStartX = 0, panStartScroll = 0;

        public void SetScale(float s)
        {
            if (Math.Abs(s - _scale) < 0.001f) return;
            _scale = s;
            Invalidate();
        }

        public TimelineView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            MouseWheel += OnWheel;
            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += (s, e) => { drag = DragMode.None; Cursor = Cursors.Default; };
        }

        private double PxPerMs() { return (Width - 16f) / Math.Max(1, viewMs); }

        public void RecalcMax()
        {
            double m = 2000;
            for (int i = 0; i < Steps.Count; i++) m = Math.Max(m, Steps[i].ReleaseTime + 200);
            totalMs = m;
            if (viewMs > totalMs) viewMs = totalMs;
            ClampScroll();
            Invalidate();
        }
        private void ClampScroll() { scrollMs = Math.Max(0, Math.Min(scrollMs, totalMs - viewMs)); }

        private void OnWheel(object s, MouseEventArgs e)
        {
            double factor = e.Delta > 0 ? 0.8 : 1.22;
            double ppm = PxPerMs();
            double anchorMs = scrollMs + (e.X - 8) / Math.Max(1, ppm);
            double nv = Math.Max(200, Math.Min(totalMs, viewMs * factor));
            viewMs = nv;
            scrollMs = anchorMs - (e.X - 8) * nv / Math.Max(1, Width - 16f);
            ClampScroll();
            Invalidate();
        }

        private void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            double ppm = PxPerMs();
            int[] laneOf; int laneCount;
            ComputeLanes(out laneOf, out laneCount);
            float rh = RowHFor(laneCount);
            for (int i = Steps.Count - 1; i >= 0; i--)
            {
                KeyStep st = Steps[i];
                double x0 = 8 + (st.PressTime - scrollMs) * ppm;
                double x1 = 8 + (st.ReleaseTime - scrollMs) * ppm;
                if (e.X > x0 - 4 && e.X < x0 + 4) { drag = DragMode.ResizeL; dragIndex = i; grabMs = st.PressTime; Select(i); return; }
                if (e.X > x1 - 4 && e.X < x1 + 4) { drag = DragMode.ResizeR; dragIndex = i; grabMs = st.ReleaseTime; Select(i); return; }
            }
            for (int i = Steps.Count - 1; i >= 0; i--)
            {
                KeyStep st = Steps[i];
                double x0 = 8 + (st.PressTime - scrollMs) * ppm;
                double x1 = 8 + (st.ReleaseTime - scrollMs) * ppm;
                float y = LaneY(i, laneOf, rh);
                if (e.X >= x0 && e.X <= x1 && e.Y >= y && e.Y <= y + rh)
                { drag = DragMode.Move; dragIndex = i; grabMs = st.PressTime - (scrollMs + (e.X - 8) / ppm); Select(i); return; }
            }
            // 未命中任何步骤：按住拖动即平移视窗（放大后可滚动查看不同时间段）
            drag = DragMode.Pan; panStartX = e.X; panStartScroll = scrollMs;
            Cursor = Cursors.SizeWE;
        }

        private void OnMove(object s, MouseEventArgs e)
        {
            if (drag == DragMode.None) return;
            double ppm = PxPerMs();
            if (drag == DragMode.Pan)
            {
                scrollMs = panStartScroll - (e.X - panStartX) / Math.Max(1, ppm);
                ClampScroll();
                Invalidate();
                return;
            }
            if (dragIndex < 0 || dragIndex >= Steps.Count) { drag = DragMode.None; return; }
            KeyStep st = Steps[dragIndex];
            double ms = Math.Max(0, scrollMs + (e.X - 8) / ppm + (drag == DragMode.Move ? grabMs : 0));
            int t = (int)Math.Round(ms);
            if (drag == DragMode.ResizeL) { int r = Math.Max(t + 5, st.ReleaseTime); st.PressTime = Math.Min(t, r - 5); }
            else if (drag == DragMode.ResizeR) { int p = Math.Min(t - 5, st.PressTime); st.ReleaseTime = Math.Max(t, p + 5); }
            else if (drag == DragMode.Move)
            {
                int d = Math.Max(0, t); int dur = st.Duration;
                st.PressTime = d; st.ReleaseTime = d + dur;
            }
            if (StepEdited != null) StepEdited(st);
            RecalcMax();
            Invalidate();
        }

        private void Select(int i)
        {
            selected = i;
            if (StepSelected != null && i >= 0 && i < Steps.Count) StepSelected(Steps[i]);
            Invalidate();
        }

        // 轨道分层：互不重叠的键合并到同一行，只有同时按下的键才分配到不同行（区间调度贪心，最小轨道数）
        private void ComputeLanes(out int[] laneOf, out int laneCount)
        {
            int n = Steps.Count;
            laneOf = new int[n];
            if (n == 0) { laneCount = 0; return; }
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => Steps[a].PressTime.CompareTo(Steps[b].PressTime));
            List<int> laneEnd = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int idx = order[k];
                int best = -1;
                for (int l = 0; l < laneEnd.Count; l++)
                    if (laneEnd[l] <= Steps[idx].PressTime) { best = l; break; }
                if (best < 0) { best = laneEnd.Count; laneEnd.Add(0); }
                laneOf[idx] = best;
                laneEnd[best] = Steps[idx].ReleaseTime;
            }
            laneCount = laneEnd.Count;
        }
        private float RowHFor(int laneCount)
        { return Math.Max(12f, Math.Min(16f, (Height - 30f) / Math.Max(1, laneCount))); }
        private float LaneY(int i, int[] laneOf, float rh)
        { return 28f + laneOf[i] * rh; }   // 起点下移，给顶部刻度数字留空间，避免蓝条遮挡时间点

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double ppm = PxPerMs();
            Font f = new Font("Segoe UI", 8f);
            using (Pen axis = new Pen(Color.FromArgb(120, 255, 255, 255)))
                g.DrawLine(axis, 8, 8, Width - 8, 8);
            // 刻度（从视窗起点起，只画视窗范围内的刻度）
            double step = NiceStep(viewMs);
            double startTick = Math.Ceiling(scrollMs / step) * step;
            for (double t = startTick; t <= scrollMs + viewMs; t += step)
            {
                float x = (float)(8 + (t - scrollMs) * ppm);
                using (Pen gp = new Pen(Color.FromArgb(20, 255, 255, 255)))
                    g.DrawLine(gp, x, 8, x, Height - 4);
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                    g.DrawString(FormatMs(t), f, tb, x + 2, 10);
            }
            // 步骤条（轨道分层：互不重叠的键合并到同一行；只在视窗内绘制）
            int[] laneOf; int laneCount;
            ComputeLanes(out laneOf, out laneCount);
            float rh = RowHFor(laneCount);
            for (int i = 0; i < Steps.Count; i++)
            {
                KeyStep st = Steps[i];
                if (st.ReleaseTime < scrollMs || st.PressTime > scrollMs + viewMs) continue;
                float x0 = (float)(8 + (st.PressTime - scrollMs) * ppm);
                float w = Math.Max(3f, (float)(st.Duration * ppm));
                RectangleF r = new RectangleF(x0, LaneY(i, laneOf, rh), w, rh - 2);
                Color c = KeyColor(st);
                if (i == selected) { using (Pen sp = new Pen(Color.White, 1.4f)) R.Stroke(g, sp, r, 4); }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, c.R, c.G, c.B)))
                    R.Fill(g, b, r, 4);
                using (SolidBrush tb = new SolidBrush(Color.White))
                    if (w > 26) R.Center(g, st.KeyName, f, tb, r);
            }
            // 进度游标
            if (CursorMs >= 0 && CursorMs >= scrollMs && CursorMs <= scrollMs + viewMs)
            {
                float cx = (float)(8 + (CursorMs - scrollMs) * ppm);
                using (Pen cp = new Pen(Color.FromArgb(255, 90, 90, 110), 1.4f))
                    g.DrawLine(cp, cx, 6, cx, Height - 4);
            }
            f.Dispose();
        }

        private static Color KeyColor(KeyStep st)
        {
            string k = st.KeyName == null ? "" : st.KeyName.ToLower();
            if (k.Contains("ctrl") || k.Contains("alt") || k.Contains("shift") || k.Contains("win") || k.Contains("menu") || k.Contains("caps"))
                return Color.FromArgb(200, 138, 0);
            if (k.Length == 1 && char.IsDigit(k[0])) return Color.FromArgb(0, 138, 80);
            return Color.FromArgb(0, 120, 212);
        }

        private double NiceStep(double max)
        {
            double raw = max / 10.0;
            double[] steps = new double[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 30000, 60000, 120000 };
            for (int i = 0; i < steps.Length; i++) if (steps[i] >= raw) return steps[i];
            return 120000;
        }

        private static string FormatMs(double ms)
        {
            if (ms >= 1000) return string.Format("{0:N1}s", ms / 1000.0);
            return string.Format("{0:N0}ms", ms);
        }
    }
}

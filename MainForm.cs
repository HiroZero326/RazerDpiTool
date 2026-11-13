using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RazerDpiTool
{
    /// <summary>
    /// C 版「三步引导」主窗口。
    ///
    /// 为什么这么排：
    /// 1. 多数人几周才用一次，流程必须写在外面 —— 顶部 ① 选设备 ② 定数值 ③ 写入确认；
    /// 2. 当前步放大，另两步收成一行摘要（摘要行本身就是状态回显）；
    /// 3. 参数（事务编号 / 等待 / 原始报文）收进面板底部「诊断参数」，主界面不出现黑话；
    /// 4. 全窗只有橙色负责指路：当前步、唯一主按钮、失败状态。
    /// </summary>
    public sealed class MainForm : Form
    {
        // ---- 分区 ----
        private Panel topBar, noteBar, rail, side;
        private CanvasPanel canvas;

        // ---- 顶栏三按钮 / 左侧三导航 ----
        private WindowCaptionButton capMin, capMax, capClose;
        private RailNavButton navDpi, navLog;

        // ---- 面板（设备 + 诊断）----
        private Label lblDevTitle, lblDevCount, lblNote, lblWait;
        private MiniIconButton btnRefresh;
        private InterfaceListView deviceList;
        private FlatButton btnTid, btnProbe;
        private NumberBox nudWait;
        private FlatToggle tgVerbose;

        // ---- 主区（三步）----
        private StepBar steps;
        private SummaryRow rowDevice, rowWrite;
        private HeroNumber hero;
        private Label lblSub, lblX, lblY, lblLogTitle, lblLogState, lblLogHint;
        private RangeSlider slider;
        private PresetChip[] chips;
        private NumberBox nudX, nudY;
        private LinkPill linkPill;
        private FlatButton btnWrite, btnRead;
        private SwitchPill swPersist;
        private TextBox txtLog;

        private Control[] dashControls;
        private Bitmap appIconBitmap;
        private ToolTip tips;

        private bool onLogPage, diagOpen;
        private bool noteVisible = true;   // 不能用 noteBar.Visible：窗口显示前它恒为 false
        private bool _sync;          // 程序改控件时置位，避免事件回环
        private int _lastReadX;      // 鼠标亲口回读的水平值，0 = 还没读过
        private int _lastReadY;      // 垂直值（X/Y 不同时副标题要一起显示）
        private int _lastWritten;    // 上一次写入成功的值
        private int _diagY, _diagH;

        private readonly string[] tids = { "1F", "3F", "FF", "00", "1A" };
        private int tidIndex = 0;

        private const int TopH = 40, NoteH = 28, RailW = 48, PanelW = 200;

        /// <summary>提示条可以关掉，关掉后整个三栏上移 28px。</summary>
        private int BodyTop { get { return noteVisible ? TopH + NoteH : TopH; } }

        /// <summary>左下角版本号：从程序集元数据取，免得和 csproj 里的 Version 各写一遍。</summary>
        private static string AppVersion
        {
            get
            {
                string raw = Application.ProductVersion ?? "";
                int plus = raw.IndexOf('+');           // 形如 1.1.0+abc123 时先截掉提交号
                if (plus >= 0) raw = raw.Substring(0, plus);
                string[] parts = raw.Split('.');
                if (parts.Length >= 2) return "v" + parts[0] + "." + parts[1];
                return raw.Length > 0 ? "v" + raw : "v?";
            }
        }

        public MainForm()
        {
            Text = "雷蛇鼠标 DPI 工具";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(960, 640);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiTheme.Fpx(12F);
            BackColor = UiTheme.BgFrom;
            MaximizeBox = false;
            DoubleBuffered = true;

            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                appIconBitmap = Icon != null ? Icon.ToBitmap() : null;
            }
            catch { appIconBitmap = null; }

            tips = new ToolTip
            {
                InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 6000,
                OwnerDraw = true      // 原生提示框用的是系统字体，自绘才能用雅黑 Light
            };
            tips.Popup += Tips_Popup;
            tips.Draw += Tips_Draw;

            BuildUi();
            ShowPage(false);
            ApplyLayout();
            RefreshDevices();
            Log("准备就绪：① 选接口  ② 定 DPI  ③ 写入（会自动读回核对）。");
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UiTheme.ApplyRoundRegion(this);
            ApplyLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        // ==================== 提示框 ====================

        private void Tip(Control c, string text) { tips.SetToolTip(c, text); }

        private Control tipOwner;

        private void Tips_Popup(object sender, PopupEventArgs e)
        {
            tipOwner = e.AssociatedControl;
            string text = tips.GetToolTip(tipOwner);
            using (var f = UiTheme.Fpx(12F))
            using (var g = CreateGraphics())
            {
                Size sz = TextRenderer.MeasureText(g, text, f);
                e.ToolTipSize = new Size(sz.Width + 20, sz.Height + 12);
            }
        }

        private void Tips_Draw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            using (var b = new SolidBrush(Color.FromArgb(16, 54, 68)))
                UiTheme.FillRound(g, b, r, 6);
            UiTheme.DrawRound(g, UiTheme.Line(56), r, 6);
            using (var f = UiTheme.Fpx(12F))
                UiTheme.Text(g, e.ToolTipText, f, UiTheme.T1,
                    new Rectangle(10, 0, e.Bounds.Width - 20, e.Bounds.Height),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        // ==================== 布局 ====================

        private void ApplyLayout()
        {
            if (topBar == null || canvas == null || capClose == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int bodyTop = BodyTop;

            topBar.SetBounds(0, 0, w, TopH);
            noteBar.Visible = noteVisible;
            if (noteVisible) noteBar.SetBounds(0, TopH, w, NoteH);
            rail.SetBounds(0, bodyTop, RailW, Math.Max(120, h - bodyTop));
            side.SetBounds(RailW, bodyTop, PanelW, Math.Max(120, h - bodyTop));
            canvas.SetBounds(RailW + PanelW, bodyTop,
                Math.Max(320, w - RailW - PanelW), Math.Max(200, h - bodyTop));

            capMin.Left = w - 138;
            capMax.Left = w - 92;
            capClose.Left = w - 46;

            LayoutSide();
            LayoutCanvas();
            ForceRepaint();
        }

        private void LayoutSide()
        {
            int ph = side.ClientSize.Height, pw = side.ClientSize.Width;
            lblDevTitle.SetBounds(12, 14, 100, 18);
            lblDevCount.SetBounds(108, 14, 46, 18);
            btnRefresh.SetBounds(pw - 34, 12, 22, 22);

            _diagH = diagOpen ? 178 : 54;
            const int listTop = 44, noteH = 30;

            int need = Math.Max(1, deviceList.Items.Count) * InterfaceListView.RowHeight + 4;
            int avail = ph - listTop - noteH - 12 - _diagH - 14;
            int listH = Math.Max(88, Math.Min(avail, Math.Max(need, 6 * InterfaceListView.RowHeight)));
            deviceList.SetBounds(12, listTop, pw - 24, listH);

            lblNote.SetBounds(12, listTop + listH + 8, pw - 24, 28);

            _diagY = Math.Max(listTop + listH + noteH + 12, ph - _diagH);

            // 诊断区展开后的四个控件
            btnTid.SetBounds(12, _diagY + 32, pw - 24, 26);
            lblWait.SetBounds(12, _diagY + 68, 80, 26);
            nudWait.SetBounds(pw - 12 - 76, _diagY + 68, 76, 26);
            tgVerbose.SetBounds(12, _diagY + 100, pw - 24, 24);
            btnProbe.SetBounds(12, _diagY + 128, pw - 24, 26);
        }

        private void LayoutCanvas()
        {
            if (steps == null) return;
            int cw = canvas.ClientSize.Width, ch = canvas.ClientSize.Height;
            const int P = 26;

            // 日志页
            lblLogTitle.SetBounds(P, 14, 300, 30);
            lblLogState.SetBounds(cw - 226, 18, 200, 22);
            txtLog.SetBounds(P, 58, cw - P * 2, Math.Max(120, ch - 96));
            lblLogHint.SetBounds(P, ch - 30, cw - P * 2, 18);

            // 仪表页
            steps.SetBounds(P, 16, Math.Max(200, cw - P * 2), 58);
            rowDevice.SetBounds(P, 84, cw - P * 2, 46);
            hero.SetBounds(P, 128, 430, 104);
            lblSub.SetBounds(P, 232, cw - P * 2, 18);
            slider.SetBounds(P, 256, cw - P * 2, 36);

            int x = P;
            foreach (var c in chips)
            {
                c.SetBounds(x, 300, c.Width, 30);
                x += c.Width + 8;
            }

            lblX.SetBounds(P, 340, 150, 16);
            lblY.SetBounds(P + 158, 340, 150, 16);
            nudX.SetBounds(P, 358, 150, 38);
            nudY.SetBounds(P + 158, 358, 150, 38);
            linkPill.SetBounds(P + 320, 358, linkPill.Width, 38);

            rowWrite.SetBounds(P, 412, cw - P * 2, 46);

            int ay = ch - 64;
            btnWrite.SetBounds(P, ay, 196, 44);
            btnRead.SetBounds(P + 208, ay, 134, 44);
            swPersist.SetBounds(Math.Max(P + 360, cw - P - swPersist.Width), ay + 10, swPersist.Width, 24);
        }

        /// <summary>窗口尺寸或页面切换后，强制每个分区连同子控件整块重画，避免残影。</summary>
        private void ForceRepaint()
        {
            if (canvas == null || !IsHandleCreated) return;
            var panels = new[] { topBar, noteBar, rail, side, canvas };
            foreach (var p in panels)
            {
                if (p == null) continue;
                p.Invalidate(true);
                p.Update();
            }
        }

        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                UiTheme.ApplyRoundRegion(this);
                ForceRepaint();
            }
            else
            {
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; // 不遮住任务栏
                WindowState = FormWindowState.Maximized;
                // 全屏时不要圆角裁剪；Region 属性不会自动释放旧实例，手动接管
                Region previous = Region;
                Region = null;
                if (previous != null) previous.Dispose();
                ForceRepaint();
            }
        }

        // ==================== 搭建界面 ====================

        private static Label Lb(string text, Color color, float px = 11F)
        {
            return new Label
            {
                Text = text, ForeColor = color, Font = UiTheme.Fpx(px),
                BackColor = Color.Transparent, AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false
            };
        }

        private void BuildUi()
        {
            BuildTopBar();
            BuildNoteBar();
            BuildRail();
            BuildSide();
            BuildCanvas();
        }

        private void BuildTopBar()
        {
            topBar = new Panel
            {
                Bounds = new Rectangle(0, 0, ClientSize.Width, TopH),
                BackColor = UiTheme.BgTop
            };
            topBar.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                UiTheme.Smooth(g);
                if (appIconBitmap != null)
                {
                    var oldMode = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(appIconBitmap, new Rectangle(11, 9, 22, 22));
                    g.InterpolationMode = oldMode;
                }
                else
                {
                    using (var b = new SolidBrush(UiTheme.T1)) g.FillEllipse(b, 14, 13, 14, 14);
                }
                using (var f1 = UiTheme.F(10F))
                    UiTheme.Text(g, "雷蛇鼠标 DPI 工具", f1, UiTheme.T1,
                        new Rectangle(38, 4, 170, 32), TextFormatFlags.VerticalCenter);
                using (var f2 = UiTheme.F(8.5F))
                    UiTheme.Text(g, "RazerDpiTool", f2, UiTheme.T3,
                        new Rectangle(196, 4, 120, 32), TextFormatFlags.VerticalCenter);
                using (var p = new Pen(UiTheme.Line(16)))
                    g.DrawLine(p, 0, TopH - 1, topBar.Width, TopH - 1);
            };
            topBar.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.X < ClientSize.Width - 140)
                    UiTheme.DragWindow(this);
            };
            Controls.Add(topBar);

            capMin = new WindowCaptionButton(CaptionKind.Minimize)
            { Bounds = new Rectangle(ClientSize.Width - 138, 0, 46, 40) };
            capMax = new WindowCaptionButton(CaptionKind.Maximize)
            { Bounds = new Rectangle(ClientSize.Width - 92, 0, 46, 40) };
            capClose = new WindowCaptionButton(CaptionKind.Close)
            { Bounds = new Rectangle(ClientSize.Width - 46, 0, 46, 40) };

            capMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            capMax.Click += (s, e) => ToggleMaximize();
            capClose.Click += (s, e) => Close();

            Tip(capMin, "最小化");
            Tip(capMax, "全屏");
            Tip(capClose, "关闭");

            topBar.Controls.Add(capMin);
            topBar.Controls.Add(capMax);
            topBar.Controls.Add(capClose);
        }

        private void BuildNoteBar()
        {
            noteBar = new Panel
            {
                Bounds = new Rectangle(0, TopH, ClientSize.Width, NoteH),
                BackColor = Color.FromArgb(10, 43, 56)
            };
            noteBar.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                UiTheme.Smooth(g);
                using (var b = new SolidBrush(UiTheme.AccentAlpha(210))) g.FillEllipse(b, 16, 12, 5, 5);
                using (var f = UiTheme.Fpx(11F))
                {
                    UiTheme.Text(g, "使用前请退出「雷云 / Synapse」，无线鼠标请先动一下唤醒",
                        f, UiTheme.T2, new Rectangle(30, 0, noteBar.Width - 300, NoteH),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    UiTheme.Text(g, "不再提示 ✕", f, UiTheme.T3,
                        new Rectangle(noteBar.Width - 262, 0, 92, NoteH),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                    UiTheme.Text(g, StatusText(), f, UiTheme.T3,
                        new Rectangle(noteBar.Width - 152, 0, 136, NoteH),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }
            };
            noteBar.MouseMove += (s, e) =>
            {
                noteBar.Cursor = e.X > noteBar.Width - 272 ? Cursors.Hand : Cursors.Default;
            };
            noteBar.MouseClick += (s, e) =>
            {
                if (e.X <= noteBar.Width - 272) return;
                noteVisible = false;
                ApplyLayout();
            };
            Controls.Add(noteBar);
        }

        /// <summary>提示条右侧那行小字：当前到底连上没有。</summary>
        private string StatusText()
        {
            var d = deviceList != null ? deviceList.SelectedDevice : null;
            if (d == null) return "未找到设备";
            return "已连接 · " + InterfaceListView.IfaceOf(d);
        }

        private void BuildRail()
        {
            rail = new Panel
            {
                Bounds = new Rectangle(0, BodyTop, RailW, ClientSize.Height - BodyTop),
                BackColor = UiTheme.BgRail
            };
            rail.Paint += (s, e) =>
            {
                using (var f = UiTheme.Fpx(10F))
                    UiTheme.Text(e.Graphics, AppVersion, f, UiTheme.T3,
                        new Rectangle(0, rail.Height - 24, RailW, 18),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            Controls.Add(rail);

            navDpi = new RailNavButton(1) { Bounds = new Rectangle(0, 8, 48, 40) };
            navLog = new RailNavButton(3) { Bounds = new Rectangle(0, 54, 48, 40) };

            navDpi.Click += (s, e) => ShowPage(false);
            navLog.Click += (s, e) => ShowPage(true);

            Tip(navDpi, "DPI 调节");
            Tip(navLog, "运行日志");

            rail.Controls.Add(navDpi);
            rail.Controls.Add(navLog);
        }



        private void BuildSide()
        {
            side = new Panel
            {
                Bounds = new Rectangle(RailW, BodyTop, PanelW, ClientSize.Height - BodyTop),
                BackColor = UiTheme.BgPanel
            };
            side.Paint += Side_Paint;
            side.MouseMove += (s, e) =>
            {
                side.Cursor = (e.Y >= _diagY && e.Y <= _diagY + 30) ? Cursors.Hand : Cursors.Default;
            };
            side.MouseClick += (s, e) =>
            {
                if (e.Y >= _diagY && e.Y <= _diagY + 30) ToggleDiag();
            };
            Controls.Add(side);

            lblDevTitle = Lb("候选接口", UiTheme.T3, 10.5F);
            lblDevTitle.BackColor = UiTheme.BgPanel;
            side.Controls.Add(lblDevTitle);

            lblDevCount = Lb("0", UiTheme.T3, 10.5F);
            lblDevCount.TextAlign = ContentAlignment.MiddleRight;
            lblDevCount.BackColor = UiTheme.BgPanel;
            side.Controls.Add(lblDevCount);

            btnRefresh = new MiniIconButton { Text = "↻", Bounds = new Rectangle(166, 12, 22, 22) };
            btnRefresh.Click += (s, e) => RefreshDevices();
            Tip(btnRefresh, "刷新设备");
            side.Controls.Add(btnRefresh);

            deviceList = new InterfaceListView { Bounds = new Rectangle(12, 44, 176, 200) };
            deviceList.SelectedIndexChanged += (s, e) => SyncDeviceText();
            Tip(deviceList, "候选接口（程序按顺序逐个尝试）");
            side.Controls.Add(deviceList);

            lblNote = Lb("程序逐个尝试，打不开自动换下一个。", UiTheme.T3, 10F);
            lblNote.BackColor = UiTheme.BgPanel;
            side.Controls.Add(lblNote);

            // ---- 诊断参数（默认收起）----
            btnTid = new FlatButton
            {
                Text = "事务编号", RightText = tids[tidIndex] + " ▾", Kind = FlatKind.Plain,
                Bounds = new Rectangle(12, 400, 176, 26)
            };
            btnTid.Click += (s, e) =>
            {
                tidIndex = (tidIndex + 1) % tids.Length;
                btnTid.RightText = tids[tidIndex] + " ▾";
                Log("事务编号切换为 0x" + tids[tidIndex]);
                side.Invalidate();
            };
            Tip(btnTid, "事务编号");
            side.Controls.Add(btnTid);

            lblWait = Lb("等待(毫秒)", UiTheme.T2, 11F);
            lblWait.BackColor = UiTheme.BgPanel;
            side.Controls.Add(lblWait);

            nudWait = new NumberBox(UiTheme.FieldOnPanel) { Minimum = 30, Maximum = 2000 };
            nudWait.Bounds = new Rectangle(112, 400, 76, 26);
            nudWait.Value = 180;
            nudWait.ValueChanged += (s, e) => side.Invalidate();
            Tip(nudWait, "等待毫秒");
            side.Controls.Add(nudWait);

            tgVerbose = new FlatToggle
            {
                Text = "打印原始报文", OnLabel = "开", OffLabel = "关", Checked = false,
                Bounds = new Rectangle(12, 430, 176, 24)
            };
            tgVerbose.CheckedChanged += (s, e) =>
            {
                lblLogState.Text = "打印原始报文 · " + (tgVerbose.Checked ? "开" : "关");
                side.Invalidate();
            };
            Tip(tgVerbose, "打印原始报文");
            side.Controls.Add(tgVerbose);

            btnProbe = new FlatButton
            {
                Text = "自动识别编号 ›", Kind = FlatKind.Ghost,
                Bounds = new Rectangle(12, 460, 176, 26)
            };
            btnProbe.Click += BtnProbe_Click;
            Tip(btnProbe, "自动识别编号");
            side.Controls.Add(btnProbe);

            SetDiagVisible(false);
        }

        private void Side_Paint(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            int y = _diagY;
            using (var p = new Pen(UiTheme.Line(20))) g.DrawLine(p, 12, y, side.Width - 12, y);
            using (var f = UiTheme.Fpx(10.5F))
            {
                UiTheme.Text(g, "诊断参数", f, UiTheme.T3, new Rectangle(12, y + 10, 90, 16),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                UiTheme.Text(g, diagOpen ? "收起 ⌃" : "展开 ›", f, UiTheme.T3,
                    new Rectangle(side.Width - 82, y + 10, 70, 16),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (!diagOpen)
                {
                    using (var f2 = UiTheme.Fpx(10F))
                        UiTheme.Text(g,
                            "0x" + tids[tidIndex] + " · " + nudWait.Value + "ms · 报文" + (tgVerbose.Checked ? "开" : "关"),
                            f2, UiTheme.T3, new Rectangle(12, y + 30, side.Width - 24, 14),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        private void ToggleDiag()
        {
            diagOpen = !diagOpen;
            SetDiagVisible(diagOpen);
            LayoutSide();
            side.Invalidate();
        }

        private void SetDiagVisible(bool on)
        {
            btnTid.Visible = on;
            lblWait.Visible = on;
            nudWait.Visible = on;
            tgVerbose.Visible = on;
            btnProbe.Visible = on;
        }

        private void BuildCanvas()
        {
            canvas = new CanvasPanel { BackColor = UiTheme.BgFrom };
            canvas.Paint += Canvas_Paint;
            Controls.Add(canvas);

            // ---- 步骤条 ----
            steps = new StepBar();
            steps.States[0] = StepState.Done;
            steps.States[1] = StepState.Current;
            steps.States[2] = StepState.Pending;
            steps.SetSub(2, "待执行");
            canvas.Controls.Add(steps);

            // ---- 步骤 1 摘要行 ----
            rowDevice = new SummaryRow { Label = "已选设备", Value = "—", Meta = "", RightText = "更换 ›" };
            rowDevice.Click += (s, e) => { deviceList.Focus(); ToggleDiagClose(); };
            Tip(rowDevice, "在左侧列表里换个接口试试");
            canvas.Controls.Add(rowDevice);

            // ---- 步骤 2 展开 ----
            hero = new HeroNumber { Value = 0 };
            canvas.Controls.Add(hero);

            lblSub = Lb("量程 100 – 35000 · 鼠标当前未读取", UiTheme.T3, 10.5F);
            canvas.Controls.Add(lblSub);

            slider = new RangeSlider { Minimum = 100, Maximum = 35000, Value = 800 };
            slider.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                SetTarget(slider.Value, true);
            };
            Tip(slider, "拖动调整 DPI（对数刻度，低档位也拖得准）");
            canvas.Controls.Add(slider);

            int[] presets = { 400, 800, 1600, 3200 };
            string[] tags = { "精准", "办公", "游戏", "高速" };
            chips = new PresetChip[presets.Length];
            for (int i = 0; i < presets.Length; i++)
            {
                var c = new PresetChip { Value = presets[i], TagText = tags[i] };
                c.AutoFit();
                c.Click += (s, e) =>
                {
                    var chip = (PresetChip)s;
                    ApplyPreset(chip.Value);
                };
                Tip(c, presets[i] + " DPI 快捷档位");
                chips[i] = c;
                canvas.Controls.Add(c);
            }

            lblX = Lb("水平 X", UiTheme.T3, 10.5F);
            lblY = Lb("垂直 Y", UiTheme.T3, 10.5F);
            canvas.Controls.Add(lblX);
            canvas.Controls.Add(lblY);

            nudX = new NumberBox(UiTheme.FieldOnCanvas) { Minimum = 100, Maximum = 35000, Glass = true };
            nudX.Bounds = new Rectangle(26, 358, 150, 38);
            nudX.Value = 800;
            nudX.ValueChanged += (s, e) =>
            {
                if (_sync) return;
                SetTarget(nudX.Value, false);   // 键入时只更新滑杆与读数，不改写正在打的字
            };
            nudX.EditingFinished += (s, e) =>
            {
                if (_sync) return;
                SetTarget(nudX.Value, true);    // 回车或离开焦点后才统一对齐
            };
            Tip(nudX, "水平 X（可直接键入）");
            canvas.Controls.Add(nudX);

            nudY = new NumberBox(UiTheme.FieldOnCanvas) { Minimum = 100, Maximum = 35000, Glass = true };
            nudY.Bounds = new Rectangle(184, 358, 150, 38);
            nudY.Value = 800;
            nudY.EditingFinished += (s, e) => UpdateSubLabel();
            Tip(nudY, "垂直 Y（可直接键入）");
            canvas.Controls.Add(nudY);

            linkPill = new LinkPill();
            linkPill.AutoFit();
            linkPill.CheckedChanged += (s, e) =>
            {
                if (_sync) return;
                nudY.Enabled = !linkPill.Checked;
                if (linkPill.Checked)
                {
                    _sync = true;
                    nudY.Value = nudX.Value;
                    _sync = false;
                }
                UpdateSubLabel();
                SetTarget(nudX.Value, true);
            };
            Tip(linkPill, "X / Y 是否使用同一个数值");
            canvas.Controls.Add(linkPill);

            // ---- 步骤 3 摘要行 ----
            rowWrite = new SummaryRow
            {
                Label = "待写入", Value = "—", Meta = "写入后自动读回核对", RightText = "未执行"
            };
            canvas.Controls.Add(rowWrite);

            // ---- 底部操作 ----
            btnWrite = new FlatButton
            {
                Text = "写入 DPI · 读回确认", Kind = FlatKind.Primary,
                Bounds = new Rectangle(26, 500, 196, 44)
            };
            btnWrite.Click += BtnSet_Click;
            Tip(btnWrite, "写入 DPI 并自动读回确认");
            canvas.Controls.Add(btnWrite);

            btnRead = new FlatButton
            {
                Text = "只读取当前值", Kind = FlatKind.Ghost,
                Bounds = new Rectangle(234, 500, 134, 44)
            };
            btnRead.Click += BtnRead_Click;
            Tip(btnRead, "读取当前 DPI");
            canvas.Controls.Add(btnRead);

            swPersist = new SwitchPill { LabelText = "板载存储", Checked = true };
            swPersist.AutoFit();
            swPersist.CheckedChanged += (s, e) =>
                Log("写入板载存储：" + (swPersist.Checked ? "开（拔插后仍保留）" : "关（临时生效）"));
            Tip(swPersist, "写入板载存储");
            canvas.Controls.Add(swPersist);

            // ---- 日志页 ----
            lblLogTitle = Lb("运行日志", UiTheme.T1, 15F);
            canvas.Controls.Add(lblLogTitle);

            lblLogState = Lb("打印原始报文 · 关", UiTheme.T3, 10.5F);
            lblLogState.TextAlign = ContentAlignment.MiddleRight;
            canvas.Controls.Add(lblLogState);

            txtLog = new TextBox
            {
                Bounds = new Rectangle(26, 58, 600, 400),
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = UiTheme.LogBack, ForeColor = UiTheme.T2,
                Font = UiTheme.Mono(9F)
            };
            Tip(txtLog, "运行日志");
            canvas.Controls.Add(txtLog);

            lblLogHint = Lb("点左侧第一枚图标返回 DPI 调节", UiTheme.T3, 10.5F);
            canvas.Controls.Add(lblLogHint);

            dashControls = new Control[]
            {
                steps, rowDevice, hero, lblSub, slider, chips[0], chips[1], chips[2], chips[3],
                nudX, nudY, lblX, lblY, linkPill, rowWrite, btnWrite, btnRead, swPersist
            };

            SetTarget(800, true);
        }

        /// <summary>两页切换：三步引导 / 运行日志。</summary>
        private void ShowPage(bool log)
        {
            onLogPage = log;
            if (dashControls != null) foreach (var c in dashControls) c.Visible = !log;

            lblLogTitle.Visible = log;
            lblLogState.Visible = log;
            txtLog.Visible = log;
            lblLogHint.Visible = log;

            navDpi.Active = !log;
            navLog.Active = log;
            canvas.Invalidate(true);
            ForceRepaint();
        }

        private void Canvas_Paint(object s, PaintEventArgs e)
        {
            if (onLogPage) return;
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            // 右下角约 5% 白的水印：画在背景层，永远压不住任何文字
            int wx = canvas.ClientSize.Width - 196, wy = canvas.ClientSize.Height - 208;
            using (var path = new GraphicsPath())
            using (var pen = new Pen(UiTheme.Layer(13), 6f))
            {
                pen.LineJoin = LineJoin.Round;
                path.AddLines(new[]
                {
                    new Point(wx, wy), new Point(wx, wy + 118),
                    new Point(wx + 30, wy + 88), new Point(wx + 52, wy + 130),
                    new Point(wx + 72, wy + 120), new Point(wx + 50, wy + 80),
                    new Point(wx + 92, wy + 80), new Point(wx, wy)
                });
                g.DrawPath(pen, path);
            }
        }

        // ==================== 数值与状态 ====================

        /// <summary>
        /// 统一的「目标值」入口：滑杆、档位片、输入框都走这里，保证四处永远一致。
        /// propagate = false 时只更新滑杆/读数，不回写输入框（避免打断用户键入）。
        /// </summary>
        private void SetTarget(int v, bool propagate)
        {
            v = Math.Max(100, Math.Min(35000, v));
            _sync = true;
            slider.Value = v;
            hero.Value = v;
            if (propagate)
            {
                nudX.Value = v;
                if (linkPill.Checked) nudY.Value = v;
            }
            _sync = false;

            bool linked = linkPill.Checked;
            foreach (var c in chips) c.Active = linked && c.Value == v;

            rowWrite.Value = v + " DPI";
            steps.SetSub(1, v.ToString());
            UpdateSubLabel();

            // 值变了，「已确认」就作废 —— 别让人以为新数值也验证过了
            if (steps.States[2] == StepState.Done && v != _lastWritten)
                SetWriteState(StepState.Pending, "待执行");
            else if (steps.States[2] == StepState.Fail)
                SetWriteState(StepState.Pending, "待执行");
        }

        /// <summary>点快捷档位：X / Y 一起设成同一个数并重新联动，符合「档位」本来的含义。</summary>
        private void ApplyPreset(int v)
        {
            _sync = true;
            linkPill.Checked = true;
            nudY.Enabled = false;
            _sync = false;
            SetTarget(v, true);
        }

        private void UpdateSubLabel()
        {
            string now;
            if (_lastReadX > 0)
                now = (_lastReadY > 0 && _lastReadY != _lastReadX)
                    ? "鼠标当前 " + _lastReadX + " × " + _lastReadY
                    : "鼠标当前 " + _lastReadX;
            else
                now = "鼠标当前未读取";

            string target = linkPill.Checked
                ? ""
                : " · 目标 X " + SafeValue(nudX) + " / Y " + SafeValue(nudY);
            lblSub.Text = "量程 100 – 35000 · " + now + target + " · 拖动滑杆或直接键入";
        }

        private static int SafeValue(NumberBox b)
        {
            try { return b.Value; } catch { return 0; }
        }

        private void SetWriteState(StepState st, string sub)
        {
            steps.SetState(2, st);
            steps.SetSub(2, sub);
            rowWrite.RightText = st == StepState.Done ? "已确认"
                           : st == StepState.Fail ? "失败" : "未执行";
            rowWrite.RightColor = st == StepState.Done ? UiTheme.T1
                                : st == StepState.Fail ? UiTheme.Accent : UiTheme.T3;
            rowWrite.Invalidate();
        }

        private void SyncDeviceText()
        {
            var d = deviceList.SelectedDevice;
            if (d == null)
            {
                rowDevice.Value = "未找到设备";
                rowDevice.Meta = "请插好鼠标后点刷新";
                steps.SetSub(0, "未找到");
                steps.SetState(0, StepState.Fail);
            }
            else
            {
                rowDevice.Value = InterfaceListView.IfaceOf(d);
                rowDevice.Meta = InterfaceListView.ModelOf(d) + (InterfaceListView.IsPrimary(d) ? " · 优先" : " · 备用");
                steps.SetSub(0, InterfaceListView.IfaceOf(d));
                steps.SetState(0, StepState.Done);
            }
            rowDevice.Invalidate();
            if (noteBar != null) noteBar.Invalidate();
        }

        private void ToggleDiagClose()
        {
            if (!diagOpen) return;
            diagOpen = false;
            SetDiagVisible(false);
            LayoutSide();
            side.Invalidate();
        }

        // ==================== 设备与参数 ====================

        private void RefreshDevices()
        {
            try
            {
                var list = RazerMouse.FindDevices();
                deviceList.SetItems(list);
                lblDevCount.Text = list.Count.ToString();
                LayoutSide();
                SyncDeviceText();
                if (list.Count > 0)
                    Log("找到 " + list.Count + " 个接口。若某个不回应，程序会自动换下一个。");
                else
                    Log("没有找到雷蛇接口。请确认鼠标已插好、已退出雷云，再点刷新。");
            }
            catch (Exception ex) { Log("枚举设备时出错：" + ex.Message); }
        }

        private byte ParseTid()
        {
            try { return Convert.ToByte(tids[tidIndex], 16); }
            catch { return 0x1F; }
        }

        // 挨个试接口：打不开或没回信就换下一个。全程不抛异常，只写日志。
        private DeviceResult<T> RunOnWorkingDevice<T>(List<RazerDeviceInfo> candidates,
                                                      Func<RazerMouse, T> action,
                                                      byte tid, int wait, bool persist, bool verbose)
        {
            var errors = new List<string>();
            int attempt = 0;
            foreach (var info in candidates)
            {
                attempt++;
                RazerMouse mouse = null;
                try
                {
                    // 逐个报进度：等待时间以「接口数 × 重试次数」累加，不显示序号会像卡死
                    LogThreadSafe("（" + attempt + "/" + candidates.Count + "）尝试 " + info.Display);
                    mouse = new RazerMouse(info.Device)
                    {
                        TransactionId = tid,
                        WaitMilliseconds = wait,
                        Persist = persist,
                        Verbose = verbose,
                        Log = LogThreadSafe
                    };
                    if (!mouse.Open())
                    {
                        errors.Add(info.Display + "：打不开");
                        LogThreadSafe(info.Display + " 打不开，换下一个");
                        continue;
                    }
                    T value = action(mouse);
                    LogThreadSafe("成功使用：" + info.Display);
                    return new DeviceResult<T> { Ok = true, Value = value };
                }
                catch (Exception ex)
                {
                    errors.Add(info.Display + "：" + ex.Message);
                    LogThreadSafe(info.Display + " 不行，换下一个（" + ex.Message + "）");
                }
                finally { if (mouse != null) mouse.Dispose(); }
            }
            string message = "所有接口都试过了，仍无法和鼠标通信。" + Environment.NewLine +
                             "常见原因：雷云(Synapse)仍在后台、无线鼠标睡着了、事务编号不对、或需要以管理员身份运行。" +
                             Environment.NewLine + string.Join(Environment.NewLine, errors);
            LogThreadSafe(message);
            return new DeviceResult<T> { Ok = false, Error = message };
        }

        // ==================== 按钮事件 ====================

        private async void BtnProbe_Click(object sender, EventArgs e)
        {
            var candidates = deviceList.Candidates();
            if (candidates.Count == 0) { Log("请先点刷新设备。"); return; }
            int wait = nudWait.Value;
            bool persist = swPersist.Checked, verbose = tgVerbose.Checked;
            SetBusy(true);
            try
            {
                var r = await Task.Run(() =>
                    RunOnWorkingDevice(candidates, m => m.ProbeTransactionId(),
                                       0x1F, wait, persist, verbose));
                if (!r.Ok) { Log("自动识别失败，原因见上方日志。"); return; }
                string found = r.Value.ToString("X2");
                int idx = Array.IndexOf(tids, found);
                if (idx >= 0) { tidIndex = idx; btnTid.RightText = found + " ▾"; }
                Log("识别成功：事务编号 0x" + found);
                side.Invalidate();
            }
            catch (Exception ex) { Log("自动识别时出错：" + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void BtnRead_Click(object sender, EventArgs e)
        {
            var candidates = deviceList.Candidates();
            if (candidates.Count == 0) { Log("请先点刷新设备。"); return; }
            byte tid = ParseTid();
            int wait = nudWait.Value;
            bool persist = swPersist.Checked, verbose = tgVerbose.Checked;
            SetBusy(true);
            try
            {
                var r = await Task.Run(() =>
                    RunOnWorkingDevice(candidates, m => m.ReadDpi(), tid, wait, persist, verbose));
                if (r.Ok) { ShowCurrent(r.Value); Log("读取成功：" + r.Value); }
                else Log("读取失败，原因见上方日志。");
            }
            catch (Exception ex) { Log("读取时出错：" + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void BtnSet_Click(object sender, EventArgs e)
        {
            var candidates = deviceList.Candidates();
            if (candidates.Count == 0) { Log("请先点刷新设备。"); return; }
            int x = nudX.Value, y = linkPill.Checked ? nudX.Value : nudY.Value;
            byte tid = ParseTid();
            int wait = nudWait.Value;
            bool persist = swPersist.Checked, verbose = tgVerbose.Checked;
            SetBusy(true);
            try
            {
                var r = await Task.Run(() =>
                    RunOnWorkingDevice(candidates, m => m.SetDpi(x, y), tid, wait, persist, verbose));
                if (r.Ok)
                {
                    _lastWritten = x;
                    ShowCurrent(r.Value);
                    SetWriteState(StepState.Done, "已确认 " + x);
                    Log("写入并读回确认成功：" + r.Value);
                }
                else
                {
                    SetWriteState(StepState.Fail, "失败");
                    Log("写入失败，原因见上方日志。");
                }
            }
            catch (Exception ex)
            {
                SetWriteState(StepState.Fail, "失败");
                Log("写入时出错：" + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private void ShowCurrent(DpiValue d)
        {
            bool outOfRange = d.X < 100 || d.X > 35000 || d.Y < 100 || d.Y > 35000;
            int cx = Math.Max(100, Math.Min(35000, d.X));
            int cy = Math.Max(100, Math.Min(35000, d.Y));

            _sync = true;
            linkPill.Checked = (cx == cy);
            nudY.Enabled = !linkPill.Checked;
            nudX.Value = cx;
            nudY.Value = cy;
            _sync = false;

            // 存原始值：界面上的滑杆只能表达 100–35000，但「鼠标当前」这行必须说实话
            _lastReadX = d.X;
            _lastReadY = d.Y;
            SetTarget(cx, true);

            if (outOfRange)
                Log("注意：鼠标回报 " + d.X + " × " + d.Y +
                    "，超出本工具 100–35000 的量程，界面只以边界值呈现，未做任何钳制写入。");
        }

        private void SetBusy(bool busy)
        {
            btnRefresh.Enabled = !busy;
            btnProbe.Enabled = !busy;
            btnRead.Enabled = !busy;
            btnWrite.Enabled = !busy;
            btnTid.Enabled = !busy;
            deviceList.Enabled = !busy;
            slider.Enabled = !busy;
            nudX.Enabled = !busy;
            nudY.Enabled = !busy && !linkPill.Checked;
            swPersist.Enabled = !busy;
            foreach (var c in chips) c.Enabled = !busy;
        }

        // ==================== 日志 ====================

        private const int LogMaxChars = 400000;   // 上限：开「打印原始报文」长时间跑也撑得住

        private void Log(string text)
        {
            if (txtLog == null) return;

            // 超限就丢掉前半段。只留一半而不是逐行删，避免每次写入都搬动整块文本。
            if (txtLog.TextLength > LogMaxChars)
            {
                txtLog.Text = "[…较早日志已省略，只保留最近一半…]" + Environment.NewLine +
                              txtLog.Text.Substring(txtLog.TextLength / 2);
            }

            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void LogThreadSafe(string text)
        {
            // 窗口已销毁：句柄没了 IsHandleCreated 为 false，若继续往下走
            // InvokeRequired 会返回 false，日志就会被工作线程直接写进已销毁的控件
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                // ObjectDisposedException 继承自 InvalidOperationException，一个 catch 就够
                try { BeginInvoke(new Action<string>(Log), text); }
                catch (InvalidOperationException) { }   // 判断与投递之间句柄刚好销毁
                return;
            }
            Log(text);
        }
    }
}

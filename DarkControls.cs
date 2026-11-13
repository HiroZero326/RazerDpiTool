using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerDpiTool
{
    public enum FlatKind { Primary, Ghost, Soft, Plain, Glass }

    /// <summary>自绘按钮：橙色主按钮 / 细边次级按钮 / 半透明白输入框样式 / 纯文字。</summary>
    public sealed class FlatButton : Control
    {
        private FlatKind _kind = FlatKind.Ghost;
        private bool _hover, _down, _active;
        private string _rightText = "";

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            // 透明底：圆角外的四个小角要走父面板的渐变，否则会留下父色的方角
            BackColor = Color.Transparent;
            Height = 30;
            Cursor = Cursors.Hand;
            ForeColor = UiTheme.T1;
        }

        public FlatKind Kind { get { return _kind; } set { _kind = value; if (value == FlatKind.Glass) EnableGlassBack(); Invalidate(); } }
        public bool Active { get { return _active; } set { _active = value; Invalidate(); } }
        public string RightText { get { return _rightText; } set { _rightText = value ?? ""; Invalidate(); } }

        /// <summary>
        /// 玻璃底必须真的透出去，否则圆角处会留下不透明方角，叠色也会和画布不一致。
        /// 与 DotToggleButton 保持同一套设置。
        /// </summary>
        private void EnableGlassBack()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            if (BackColor != Color.Transparent) BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width < 2 || r.Height < 2) return;

            switch (_kind)
            {
                case FlatKind.Primary:
                    using (var b = new SolidBrush(_down ? Color.FromArgb(232, 104, 78)
                        : Enabled ? UiTheme.Accent : UiTheme.AccentAlpha(120)))
                        UiTheme.FillRound(g, b, r, UiTheme.Radius);
                    break;
                case FlatKind.Soft:
                    using (var b = new SolidBrush(UiTheme.Layer(26)))
                        UiTheme.FillRound(g, b, r, UiTheme.Radius);
                    break;
                case FlatKind.Ghost:
                    UiTheme.DrawRound(g, UiTheme.Line(_hover ? 75 : 40), r, UiTheme.Radius);
                    break;
                case FlatKind.Glass:   // 与「写入板载存储」同一套玻璃配方
                    UiTheme.PaintGlass(g, r, _hover);
                    break;
            }

            if (_hover && _kind != FlatKind.Primary && _kind != FlatKind.Glass)
            {
                using (var b = new SolidBrush(UiTheme.Layer(16)))
                    UiTheme.FillRound(g, b, r, UiTheme.Radius);
            }

            // 选中态：白 8% 底 + 2px 橙左条（参考卡原样）
            if (_active)
            {
                using (var b = new SolidBrush(UiTheme.Layer(20)))
                    UiTheme.FillRound(g, b, r, 4);
                using (var b = new SolidBrush(UiTheme.Accent))
                    g.FillRectangle(b, 0, 3, 2, r.Height - 6);
            }

            Color textColor = _kind == FlatKind.Primary ? UiTheme.AccentText
                            : (_active ? UiTheme.T1 : (_kind == FlatKind.Ghost ? UiTheme.T2 : UiTheme.T1));
            if (!Enabled) textColor = Color.FromArgb(120, textColor);
            // 主按钮不再用假粗，改用略大字号建立层级
            float textPt = _kind == FlatKind.Primary ? 10.5F : 9.5F;
            bool left = _active || _rightText.Length > 0 || _kind == FlatKind.Soft;

            int pad = 12;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            Rectangle textRect = new Rectangle(pad, 0, Math.Max(10, r.Width - pad * 2), r.Height);
            UiTheme.TextPt(g, Text, textPt, textColor, textRect, flags);

            if (_rightText.Length > 0)
            {
                Rectangle right = new Rectangle(r.Width - 78, 0, 66, r.Height);
                UiTheme.TextPt(g, _rightText, 9F,
                    _active ? UiTheme.Accent : UiTheme.T3, right,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    /// <summary>自绘开关：左边写名字，右边写「开 / 关」，点一下切换。</summary>
    public sealed class FlatToggle : Control
    {
        private bool _checked = true;
        private bool _hover, _glass;
        public string OnLabel = "开";
        public string OffLabel = "关";
        /// <summary>左右文字的内边距，加大可压缩「X/Y 相同」与「已联动」之间的空白。</summary>
        public int PadLeft = 10, PadRight = 10;
        public event EventHandler CheckedChanged;

        public FlatToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 24;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public bool Glass
        {
            get { return _glass; }
            set
            {
                _glass = value;
                if (value)
                {
                    SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                    if (BackColor != Color.Transparent) BackColor = Color.Transparent;
                }
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_glass)   // 与「写入板载存储」同一套玻璃配方
            {
                UiTheme.PaintGlass(g, r, _hover);
            }
            else if (_hover)
            {
                using (var b = new SolidBrush(UiTheme.Layer(14)))
                    UiTheme.FillRound(g, b, r, 4);
            }
            UiTheme.TextPt(g, Text, 9.5F, UiTheme.T2,
                new Rectangle(PadLeft, 0, Math.Max(20, Width - PadLeft - 60), Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            UiTheme.TextPt(g, _checked ? OnLabel : OffLabel, 9F,
                _checked ? UiTheme.T1 : UiTheme.T3,
                new Rectangle(Width - 38 - PadRight, 0, 38, Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            if (!_glass)
            {
                using (var p = new Pen(UiTheme.Line(18)))
                    g.DrawLine(p, 6, Height - 1, Width - 6, Height - 1);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !_checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    /// <summary>
    /// 设备接口列表（C 版）：44px 两行行高 —— 上排是接口号，下排是型号与「优先 / 备用」。
    /// 七条记录型号完全一样，所以主行必须放能区分它们的东西：mi_00 / mi_01…
    /// </summary>
    public sealed class InterfaceListView : Control
    {
        public const int RowHeight = 44;
        private readonly List<RazerDeviceInfo> _items = new List<RazerDeviceInfo>();
        private int _selected = -1;
        private int _scroll;
        private int _hover = -1;
        public event EventHandler SelectedIndexChanged;

        public InterfaceListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = UiTheme.BgPanel;   // 与父面板同色，不必依赖透明背景
            Cursor = Cursors.Hand;
        }

        public List<RazerDeviceInfo> Items { get { return _items; } }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = Math.Max(-1, Math.Min(_items.Count - 1, value));
                if (_selected == v) return;
                _selected = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public RazerDeviceInfo SelectedDevice
        {
            get { return (_selected >= 0 && _selected < _items.Count) ? _items[_selected] : null; }
        }

        /// <summary>选中项排最前，其余依次兜底 —— 与旧版「自动换接口」逻辑一致。</summary>
        public List<RazerDeviceInfo> Candidates()
        {
            var list = new List<RazerDeviceInfo>();
            var sel = SelectedDevice;
            if (sel != null) list.Add(sel);
            foreach (var d in _items) if (d != sel) list.Add(d);
            return list;
        }

        public void SetItems(List<RazerDeviceInfo> list)
        {
            _items.Clear();
            if (list != null) _items.AddRange(list);
            _scroll = 0;
            _hover = -1;
            _selected = _items.Count > 0 ? 0 : -1;
            Invalidate();
        }

        private int VisibleRows { get { return Math.Max(1, Height / RowHeight); } }

        /// <summary>接口号，例如 mi_00；拿不到就退回报告长度。</summary>
        public static string IfaceOf(RazerDeviceInfo d)
        {
            string raw = d.Display ?? "";
            int m = raw.IndexOf("mi_", StringComparison.Ordinal);
            if (m < 0) return d.FeatureLength + "B";
            return raw.Substring(m, Math.Min(5, raw.Length - m));
        }

        /// <summary>型号主名，去掉开头的 ★ 与后面的 0x 编号。</summary>
        public static string ModelOf(RazerDeviceInfo d)
        {
            string s = (d.Display ?? "").Replace("★", "").Replace("Razer ", "").Trim();
            int p = s.IndexOf("  0x", StringComparison.Ordinal);
            if (p > 0) s = s.Substring(0, p);
            return s.Trim();
        }

        /// <summary>打了 ★ 的接口能收满 90 字节，优先试它。</summary>
        public static bool IsPrimary(RazerDeviceInfo d)
        {
            return (d.Display ?? "").TrimStart().StartsWith("★");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            int rows = VisibleRows;
            for (int k = 0; k < rows; k++)
            {
                int i = _scroll + k;
                if (i >= _items.Count) break;
                int top = k * RowHeight;
                var r = new Rectangle(0, top + 1, Width - 1, RowHeight - 4);
                if (i == _selected)
                {
                    using (var b = new SolidBrush(UiTheme.Layer(20))) UiTheme.FillRound(g, b, r, 6);
                    using (var b = new SolidBrush(UiTheme.Accent))
                        g.FillRectangle(b, 0, r.Top + 7, 2, r.Height - 14);
                }
                else if (i == _hover)
                {
                    using (var b = new SolidBrush(UiTheme.Layer(9))) UiTheme.FillRound(g, b, r, 6);
                }

                var item = _items[i];
                bool on = i == _selected;

                // 左侧小圆点：优先接口实心，其余空心
                int dx = 11, dy = top + RowHeight / 2 - 3;
                if (on || IsPrimary(item))
                {
                    using (var b = new SolidBrush(on ? UiTheme.T1 : UiTheme.T2))
                        g.FillEllipse(b, dx, dy, 6, 6);
                }
                else
                {
                    using (var p = new Pen(UiTheme.Line(90)))
                        g.DrawEllipse(p, dx, dy, 5, 5);
                }

                using (var f1 = UiTheme.Fpx(12.5F))
                using (var f2 = UiTheme.Fpx(10F))
                {
                    UiTheme.Text(g, IfaceOf(item), f1, on ? UiTheme.T1 : UiTheme.T2,
                        new Rectangle(26, top + 7, Width - 40, 18),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    string sub = ModelOf(item) + (IsPrimary(item) ? " · 优先" : " · 备用");
                    UiTheme.Text(g, sub, f2, on ? UiTheme.T2 : UiTheme.T3,
                        new Rectangle(26, top + 24, Width - 40, 15),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }

            if (_items.Count > rows)
            {
                int trackH = Math.Max(20, Height * rows / _items.Count);
                int top = _scroll * (Height - trackH) / Math.Max(1, _items.Count - rows);
                using (var b = new SolidBrush(UiTheme.Line(45)))
                    UiTheme.FillRound(g, b, new Rectangle(Width - 3, top, 2, trackH), 1);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            int i = _scroll + e.Y / RowHeight;
            if (i >= 0 && i < _items.Count) SelectedIndex = i;
            base.OnMouseClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = _scroll + e.Y / RowHeight;
            if (i >= _items.Count) i = -1;
            if (i != _hover) { _hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, _items.Count - VisibleRows);
            _scroll = Math.Max(0, Math.Min(max, _scroll - Math.Sign(e.Delta) * 2));
            Invalidate();
            base.OnMouseWheel(e);
        }
    }

    /// <summary>支持透明背景的文本框，玻璃质感输入框专用。</summary>
    public sealed class GlassTextBox : TextBox
    {
        public GlassTextBox()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BorderStyle = BorderStyle.None;
            BackColor = Color.Transparent;
        }
    }

    /// <summary>数字输入框：圆角半透明白底 + 内嵌一个无边框文本框，可自由输入。</summary>
    public sealed class NumberBox : Panel
    {
        private readonly TextBox _box;
        private readonly Color _fieldColor;
        private readonly Color _equivColor;   // 玻璃底叠完的等效不透明色，给内嵌文本框用
        // WinForm 的原生文本框不支持真透明背景；用「渐变底色 + 白浮层」的等效色代替，
        // 视觉上与 DotToggleButton 的玻璃底一致，又不会露出白块。
        private static readonly Color GlassField = Color.FromArgb(61, 105, 95);
        // 联动时「垂直 Y」是灰掉的：用一块更暗的等效色，数字降到 T3，一眼看出不可改。
        private static readonly Color DimField = Color.FromArgb(46, 88, 84);
        private int _minimum = 0, _maximum = 999999;
        private int _lastGood;      // 最近一次解析成功的值，粘贴非法内容时回退到它
        private bool _silent, _glass;
        public event EventHandler ValueChanged;

        /// <summary>键入结束（回车或离开焦点）。主窗口靠它在「打完了」之后才统一对齐数值。</summary>
        public event EventHandler EditingFinished;

        public NumberBox(Color fieldColor)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            _fieldColor = fieldColor;
            // 外框画的是「底色 + 白 20%」，内嵌文本框若用底色会显出一块更深的方块。
            // 这里算出叠完之后的等效色，让数字看起来真正落在同一块玻璃里。
            _equivColor = BlendWhite(fieldColor, 20);
            BackColor = fieldColor;
            _box = new GlassTextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = _equivColor,
                ForeColor = UiTheme.T1,
                Font = UiTheme.F(11F),
                Text = "0",
                TextAlign = HorizontalAlignment.Left
            };
            _box.TextChanged += (s, e) =>
            {
                if (_silent) return;
                // 记住最近一次合法值，粘贴非法内容时用它回退，而不是一脚跌到最小值
                int v;
                if (int.TryParse(_box.Text.Trim(), out v) && v >= _minimum && v <= _maximum)
                    _lastGood = v;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            _box.KeyPress += (s, e) =>
            {
                // 只放行数字与编辑控制键，免得键入「abc」被静默当成最小值
                if (char.IsControl(e.KeyChar)) return;
                if (!char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            _box.LostFocus += (s, e) =>
            {
                if (EditingFinished != null) EditingFinished(this, EventArgs.Empty);
            };
            _box.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (EditingFinished != null) EditingFinished(this, EventArgs.Empty);
            };
            Controls.Add(_box);
            Height = 34;      // 内部文本框造好之后再改尺寸，避免排版摸空
            LayoutBox();
        }

        private static Color BlendWhite(Color c, int alpha)
        {
            float a = alpha / 255f;
            return Color.FromArgb(
                (int)(c.R + (255 - c.R) * a),
                (int)(c.G + (255 - c.G) * a),
                (int)(c.B + (255 - c.B) * a));
        }

        public int Minimum { get { return _minimum; } set { _minimum = value; } }
        public int Maximum { get { return _maximum; } set { _maximum = value; } }

        /// <summary>切成和「写入板载存储」一样的透明玻璃底 + 细边框。</summary>
        public bool Glass
        {
            get { return _glass; }
            set
            {
                _glass = value;
                BackColor = value ? GlassField : _fieldColor;
                _box.BackColor = value ? GlassField : _equivColor;
                Invalidate(true);
            }
        }

        public int Value
        {
            get
            {
                int v;
                if (!int.TryParse(_box.Text.Trim(), out v)) v = _lastGood;
                return Math.Max(_minimum, Math.Min(_maximum, v));
            }
            set
            {
                int c = Math.Max(_minimum, Math.Min(_maximum, value));
                _lastGood = c;
                _silent = true;
                _box.Text = c.ToString();
                _silent = false;
            }
        }

        public void FocusValue() { _box.Focus(); _box.SelectAll(); }

        private void LayoutBox()
        {
            if (_box == null) return;
            // 单行原生文本框内部会自行把文字垂直居中，所以只要把文本框本身上下居中，
            // 数字就正好落在圆角框的正中（原先固定 top=7 会让它偏上/显出内框）。
            int boxH = Math.Max(16, Height - 6);
            _box.Bounds = new Rectangle(10, (Height - boxH) / 2,
                Math.Max(20, Width - 20), boxH);
        }

        protected override void OnResize(EventArgs e)
        {
            LayoutBox();
            // Region 属性不会自动释放前一个实例，这里手动接管，否则每次布局都漏一个
            Region previous = Region;
            using (var path = UiTheme.RoundRect(new Rectangle(0, 0, Width, Height), UiTheme.Radius))
                Region = new Region(path);
            if (previous != null) previous.Dispose();
            Invalidate();
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (!Enabled)
            {
                using (var b = new SolidBrush(DimField)) g.FillRectangle(b, ClientRectangle);
                using (var b = new SolidBrush(UiTheme.Layer(6)))
                    UiTheme.FillRound(g, b, r, UiTheme.Radius);
            }
            else if (_glass)
            {
                // 等效玻璃色铺底（Region 已裁成圆角），再补极淡高光 + 细边框
                using (var b = new SolidBrush(GlassField)) g.FillRectangle(b, ClientRectangle);
                using (var b = new SolidBrush(UiTheme.Layer(12)))
                    UiTheme.FillRound(g, b, r, UiTheme.Radius);
                UiTheme.DrawRound(g, UiTheme.Line(40), r, UiTheme.Radius);
            }
            else
            {
                using (var b = new SolidBrush(UiTheme.Layer(20)))
                    UiTheme.FillRound(g, b, r, UiTheme.Radius);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (_box != null)
            {
                _box.ForeColor = Enabled ? UiTheme.T1 : UiTheme.T3;
                _box.BackColor = Enabled ? (_glass ? GlassField : _equivColor) : DimField;
            }
            Cursor = Enabled ? Cursors.IBeam : Cursors.Default;
            Invalidate();
        }
    }

    /// <summary>
    /// 带状态圆点的开关按钮：关 = 红点，开 = 亮绿点。
    /// 用在主界面的「写入板载存储」，一眼就能看出有没有存进鼠标。
    /// </summary>
    public sealed class DotToggleButton : Control
    {
        private bool _checked = true;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public static readonly Color DotOff = Color.FromArgb(255, 91, 91);    // 红
        public static readonly Color DotOn = Color.FromArgb(74, 222, 128);    // 亮绿

        public DotToggleButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            UiTheme.PaintGlass(g, r, _hover);

            // 文字左边的小圆点
            int textWidth;
            using (var mf = UiTheme.F(9.5F)) textWidth = TextRenderer.MeasureText(Text, mf).Width;
            int start = Math.Max(10, (Width - (textWidth + 20)) / 2);
            int cy = Height / 2;
            using (var b = new SolidBrush(_checked ? DotOn : DotOff))
            {
                g.FillEllipse(b, start, cy - 4, 8, 8);
                using (var glow = new SolidBrush(Color.FromArgb(60, _checked ? DotOn : DotOff)))
                    g.FillEllipse(glow, start - 2, cy - 6, 12, 12);
            }
            UiTheme.TextPt(g, Text, 9.5F, UiTheme.T1,
                new Rectangle(start + 14, 0, Width - start - 18, Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnClick(EventArgs e) { Checked = !_checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    public enum CaptionKind { Minimize, Maximize, Close }

    /// <summary>顶栏右上角的窗口按钮：细线 — □ ×。做成独立控件，才能各挂各的提示。</summary>
    public sealed class WindowCaptionButton : Control
    {
        private readonly CaptionKind _kind;
        private bool _hover, _down;

        public WindowCaptionButton(CaptionKind kind)
        {
            _kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.BgTop;   // 不透明底，避免窗口缩放时留下半透明残影
            Size = new Size(46, 40);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_hover)
            {
                using (var b = new SolidBrush(UiTheme.Layer(_down ? 34 : 20)))
                    UiTheme.FillRound(g, b, r, 4);
            }
            Color c = _hover ? UiTheme.T1 : UiTheme.T2;
            int cx = Width / 2, cy = Height / 2;
            using (var p = new Pen(c, 1.3f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                if (_kind == CaptionKind.Minimize)
                    g.DrawLine(p, cx - 6, cy, cx + 6, cy);
                else if (_kind == CaptionKind.Maximize)
                    g.DrawRectangle(p, cx - 5, cy - 5, 10, 10);
                else
                {
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    }

    /// <summary>左侧导航图标：一枚 = 一页。选中项左侧 2px 橙条。</summary>
    public sealed class RailNavButton : Control
    {
        private readonly int _iconIndex;
        private bool _active, _hover;

        public RailNavButton(int iconIndex)
        {
            _iconIndex = iconIndex;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.BgRail;
            Size = new Size(48, 40);
            Cursor = Cursors.Hand;
        }

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (_hover && !_active)
            {
                using (var b = new SolidBrush(UiTheme.Layer(14)))
                    UiTheme.FillRound(g, b, new Rectangle(2, 2, Width - 5, Height - 5), 6);
            }
            if (_active)
            {
                using (var b = new SolidBrush(UiTheme.Accent))
                    g.FillRectangle(b, 0, 6, 2, Height - 12);
            }
            UiTheme.PaintRailIcon(g, _iconIndex, new Rectangle(4, 3, 40, 34),
                _active ? UiTheme.Accent : (_hover ? UiTheme.T1 : UiTheme.T3));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // ==================================================================
    //  C 版「三步引导」新增的一组零件
    // ==================================================================

    /// <summary>
    /// 主区面板：渐变画在 OnPaintBackground 里。
    /// 这样所有「透明背景」的子控件（步骤条、大数字、滑杆）取到的就是真渐变，
    /// 而不是父面板那个单一底色，圆角边缘不会露出色块。
    /// </summary>
    public sealed class CanvasPanel : Panel
    {
        public CanvasPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            UiTheme.PaintCanvas(e.Graphics, ClientRectangle);
        }
    }

    /// <summary>22px 的小图标按钮（面板标题右侧的「刷新」）。</summary>
    public sealed class MiniIconButton : Control
    {
        private bool _hover;

        public MiniIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(22, 22);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            if (_hover)
            {
                using (var b = new SolidBrush(UiTheme.Layer(20)))
                    UiTheme.FillRound(g, b, new Rectangle(0, 0, Width - 1, Height - 1), 6);
            }
            using (var f = UiTheme.Fpx(12.5F))
            {
                UiTheme.Text(g, Text, f, _hover ? UiTheme.T1 : UiTheme.T2,
                    new Rectangle(0, 0, Width, Height),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    public enum StepState { Pending, Current, Done, Fail }

    /// <summary>
    /// 三步指示器：① 选设备 ② 定数值 ③ 写入确认。
    /// 圆圈之间只画一段短线（不去遮底），因此不必知道背后渐变是什么颜色。
    /// </summary>
    public sealed class StepBar : Control
    {
        public readonly string[] Titles = { "选择设备", "设定 DPI", "写入确认" };
        public readonly string[] Subs = { "", "", "" };
        public readonly StepState[] States = { StepState.Done, StepState.Current, StepState.Pending };

        public StepBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 58;
        }

        public void SetSub(int i, string text)
        {
            if (i < 0 || i > 2 || Subs[i] == text) return;
            Subs[i] = text;
            Invalidate();
        }

        public void SetState(int i, StepState st)
        {
            if (i < 0 || i > 2 || States[i] == st) return;
            States[i] = st;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            float colW = Width / 3f;
            float cy = 12f;

            using (var p = new Pen(UiTheme.Line(38)))
            {
                for (int i = 0; i < 2; i++)
                    g.DrawLine(p, colW * (i + 0.5f) + 16, cy, colW * (i + 1.5f) - 16, cy);
            }

            for (int k = 0; k < 3; k++)
            {
                float cx = colW * (k + 0.5f);
                DrawBadge(g, cx, cy, k);
                using (var f1 = UiTheme.Fpx(11.5F))
                using (var f2 = UiTheme.Fpx(10F))
                {
                    UiTheme.Text(g, Titles[k], f1,
                        States[k] == StepState.Current ? UiTheme.T1 :
                        States[k] == StepState.Pending ? UiTheme.T3 : UiTheme.T2,
                        new Rectangle((int)(cx - 70), 28, 140, 17),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                    if (Subs[k].Length > 0)
                    {
                        UiTheme.Text(g, Subs[k], f2,
                            States[k] == StepState.Current ? UiTheme.Accent :
                            States[k] == StepState.Fail ? UiTheme.Accent : UiTheme.T3,
                            new Rectangle((int)(cx - 70), 45, 140, 15),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPadding);
                    }
                }
            }
        }

        /// <summary>圆心徽标：已完成打勾、当前填橙、待执行只画一圈线。</summary>
        private void DrawBadge(Graphics g, float cx, float cy, int i)
        {
            const float r = 10.5f;
            var box = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            var st = States[i];

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(box);
                if (st == StepState.Done)
                {
                    using (var b = new SolidBrush(UiTheme.Layer(51))) g.FillPath(b, path);
                }
                else if (st == StepState.Current)
                {
                    using (var halo = new SolidBrush(UiTheme.AccentAlpha(18)))
                        g.FillEllipse(halo, cx - r - 5, cy - r - 5, (r + 5) * 2, (r + 5) * 2);
                    using (var b = new SolidBrush(UiTheme.Accent)) g.FillPath(b, path);
                }
                else
                {
                    using (var p = new Pen(st == StepState.Fail ? UiTheme.AccentAlpha(160) : UiTheme.Line(56), 1.2f))
                        g.DrawPath(p, path);
                }
            }

            if (st == StepState.Done)
            {
                using (var p = new Pen(UiTheme.T1, 2.0f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx - 4f, cy, cx - 1f, cy + 3.4f);
                    g.DrawLine(p, cx - 1f, cy + 3.4f, cx + 4.4f, cy - 3.6f);
                }
            }
            else
            {
                string t = st == StepState.Fail ? "!" : (i + 1).ToString();
                using (var f = UiTheme.Fpx(11F))
                {
                    UiTheme.Text(g, t, f,
                        st == StepState.Current ? UiTheme.AccentText : UiTheme.T3,
                        new Rectangle((int)(cx - 10), (int)(cy - 9), 20, 18),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }
        }
    }

    /// <summary>
    /// 折叠的步骤摘要行：左边小标题，中间值，右边状态 / 入口。
    /// 点一下可以回到对应步骤。
    /// </summary>
    public sealed class SummaryRow : Control
    {
        private string _label = "", _value = "", _meta = "", _right = "";
        public Color RightColor = UiTheme.T3;
        private bool _hover;

        // 必须走属性：设字段不会触发重绘，界面会停在上一帧的旧数值
        public string Label { get { return _label; } set { _label = value ?? ""; Invalidate(); } }
        public string Value { get { return _value; } set { _value = value ?? ""; Invalidate(); } }
        public string Meta { get { return _meta; } set { _meta = value ?? ""; Invalidate(); } }
        public string RightText { get { return _right; } set { _right = value ?? ""; Invalidate(); } }

        public SummaryRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 46;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(UiTheme.Layer(_hover ? 22 : 14)))
                UiTheme.FillRound(g, b, r, UiTheme.Radius);

            using (var f = UiTheme.Fpx(10.5F))
                UiTheme.Text(g, Label, f, UiTheme.T3, new Rectangle(14, 0, 54, Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int x = 68;
            using (var fv = UiTheme.Fpx(12F))
            {
                Size sv = TextRenderer.MeasureText(Value, fv, new Size(int.MaxValue, int.MaxValue),
                                                   TextFormatFlags.NoPadding);
                UiTheme.Text(g, Value, fv, UiTheme.T1, new Rectangle(x, 0, Width - x - 20, Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += sv.Width + 8;
            }
            if (Meta.Length > 0 && x < Width - 120)
            {
                using (var fm = UiTheme.Fpx(10.5F))
                    UiTheme.Text(g, Meta, fm, UiTheme.T3, new Rectangle(x, 0, Width - x - 96, Height),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            if (RightText.Length > 0)
            {
                using (var fr = UiTheme.Fpx(10.5F))
                    UiTheme.Text(g, RightText, fr, _hover ? UiTheme.T1 : RightColor,
                        new Rectangle(Width - 96, 0, 82, Height),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    /// <summary>84px 的主角数字。0 表示还没读过，显示两个短横，不伪装成已知值。</summary>
    public sealed class HeroNumber : Control
    {
        private int _value;
        public string Unit = "DPI";

        public HeroNumber()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 104;
        }

        public int Value
        {
            get { return _value; }
            set { _value = Math.Max(0, value); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            string txt = _value > 0 ? _value.ToString() : "--";

            // 用 GDI（TextRenderer）而不是 GDI+ DrawString：中文版雅黑在 84px 这种大字号下，
            // DrawString 的量测与基线会飘，GDI 的结果才和系统里其它文字一致。
            using (var f = UiTheme.Fpx(84F))
            {
                Size sz = TextRenderer.MeasureText(g, txt, f, new Size(int.MaxValue, int.MaxValue),
                                                   TextFormatFlags.NoPadding);
                UiTheme.Text(g, txt, f, _value > 0 ? UiTheme.T1 : UiTheme.T3,
                    new Rectangle(0, 0, Width, Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                using (var fu = UiTheme.Fpx(13F))
                    UiTheme.Text(g, Unit, fu, UiTheme.T2,
                        new Rectangle(sz.Width + 14, Height - 40, 60, 22),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>
    /// DPI 滑杆：对数刻度。
    /// 100–35000 若按线性摆，400 / 800 / 1600 会全挤在最左端两三个像素里；对数刻度才拖得准。
    /// </summary>
    public sealed class RangeSlider : Control
    {
        public int Minimum = 100, Maximum = 35000;
        private int _value = 800;
        private bool _drag;
        public event EventHandler ValueChanged;

        public RangeSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 36;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        private float Ratio(int v)
        {
            if (Maximum <= Minimum) return 0f;
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            double lo = Math.Log(Minimum), hi = Math.Log(Maximum);
            return (float)((Math.Log(v) - lo) / (hi - lo));
        }

        private int ValueAt(int x)
        {
            int span = Math.Max(1, Width - 18);
            float p = Math.Max(0f, Math.Min(1f, (x - 9f) / span));
            double lo = Math.Log(Minimum), hi = Math.Log(Maximum);
            int v = (int)Math.Round(Math.Exp(lo + p * (hi - lo)));
            v = (v / 10) * 10;
            return Math.Max(Minimum, Math.Min(Maximum, v));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            int left = 9, right = Width - 9, ty = 12;
            if (right <= left) return;

            using (var b = new SolidBrush(UiTheme.Line(34)))
                UiTheme.FillRound(g, b, new Rectangle(left, ty - 2, right - left, 4), 2);

            int fill = (int)((right - left) * Ratio(_value));
            if (fill > 0)
            {
                using (var b = new SolidBrush(UiTheme.Line(102)))
                    UiTheme.FillRound(g, b, new Rectangle(left, ty - 2, fill, 4), 2);
            }

            int tx = left + fill - 9;
            tx = Math.Max(0, Math.Min(Width - 18, tx));
            using (var halo = new SolidBrush(UiTheme.AccentAlpha(38)))
                g.FillEllipse(halo, tx - 3, ty - 12, 24, 24);
            using (var b = new SolidBrush(UiTheme.Accent))
                g.FillEllipse(b, tx, ty - 9, 18, 18);

            using (var f = UiTheme.Fpx(10F))
            {
                UiTheme.Text(g, Minimum.ToString(), f, UiTheme.T3,
                    new Rectangle(0, 22, 80, 14),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                UiTheme.Text(g, Maximum.ToString(), f, UiTheme.T3,
                    new Rectangle(Width - 80, 22, 80, 14),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _drag = true;
            Capture = true;
            Value = ValueAt(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) Value = ValueAt(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _drag = false;
            Capture = false;
            base.OnMouseUp(e);
        }
    }

    /// <summary>档位片：左边数字、右边用途，选中时底色提亮 + 橙边。</summary>
    public sealed class PresetChip : Control
    {
        public int Value;
        public string TagText = "";
        private bool _active, _hover;

        public PresetChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        /// <summary>按「数字 + 用途」量出宽度，五个片子才能排成整齐一行。</summary>
        public void AutoFit()
        {
            int w1, w2;
            using (var f1 = UiTheme.Fpx(12F))
                w1 = TextRenderer.MeasureText(Value.ToString(), f1,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            using (var f2 = UiTheme.Fpx(9F))
                w2 = TagText.Length == 0 ? 0 : TextRenderer.MeasureText(TagText, f2,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            Width = 20 + w1 + (w2 > 0 ? 6 + w2 : 0);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (_active)
            {
                using (var b = new SolidBrush(UiTheme.Layer(41))) UiTheme.FillRound(g, b, r, UiTheme.Radius);
                UiTheme.DrawRound(g, UiTheme.AccentAlpha(140), r, UiTheme.Radius);
            }
            else
            {
                if (_hover)
                {
                    using (var b = new SolidBrush(UiTheme.Layer(10))) UiTheme.FillRound(g, b, r, UiTheme.Radius);
                }
                UiTheme.DrawRound(g, UiTheme.Line(_hover ? 75 : 40), r, UiTheme.Radius);
            }

            using (var f1 = UiTheme.Fpx(12F))
            {
                UiTheme.Text(g, Value.ToString(), f1, _active ? UiTheme.T1 : UiTheme.T2,
                    new Rectangle(10, 0, Width - 20, Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Size s1 = TextRenderer.MeasureText(Value.ToString(), f1,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                if (TagText.Length > 0)
                {
                    using (var f2 = UiTheme.Fpx(9F))
                        UiTheme.Text(g, _active ? "当前" : TagText, f2, _active ? UiTheme.Accent : UiTheme.T3,
                            new Rectangle(10 + s1.Width + 6, 0, Width - s1.Width - 16, Height),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>板载存储开关：真开关（34×19 轨道 + 圆钮），不再伪装成按钮。</summary>
    public sealed class SwitchPill : Control
    {
        private bool _checked = true;
        private bool _hover;
        public string LabelText = "";
        public event EventHandler CheckedChanged;

        public SwitchPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 24;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public void AutoFit()
        {
            int tw;
            using (var f = UiTheme.Fpx(11F))
                tw = TextRenderer.MeasureText(LabelText, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding).Width;
            Width = 44 + tw;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            int ty = (Height - 19) / 2;

            using (var b = new SolidBrush(_checked ? UiTheme.AccentAlpha(77) : UiTheme.Layer(36)))
                UiTheme.FillRound(g, b, new Rectangle(0, ty, 34, 19), 9);
            using (var b = new SolidBrush(_checked ? UiTheme.Accent : UiTheme.T3))
                g.FillEllipse(b, _checked ? 17 : 3, ty + 2.5f, 14, 14);

            using (var f = UiTheme.Fpx(11F))
                UiTheme.Text(g, LabelText, f, _hover ? UiTheme.T1 : UiTheme.T2,
                    new Rectangle(44, 0, Math.Max(10, Width - 44), Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnClick(EventArgs e) { Checked = !_checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    /// <summary>X / Y 联动提示：左边一颗小点，点一下切换联动。</summary>
    public sealed class LinkPill : Control
    {
        public string OnText = "X / Y 已联动";
        public string OffText = "X / Y 分开";
        private bool _checked = true;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public LinkPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 38;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public void AutoFit()
        {
            int tw;
            using (var f = UiTheme.Fpx(11F))
                tw = TextRenderer.MeasureText(OnText, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding).Width;
            Width = 26 + tw + 12;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiTheme.Smooth(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_hover)
            {
                using (var b = new SolidBrush(UiTheme.Layer(8))) UiTheme.FillRound(g, b, r, UiTheme.Radius);
            }
            UiTheme.DrawRound(g, UiTheme.Line(_hover ? 75 : 40), r, UiTheme.Radius);

            int cy = Height / 2;
            using (var b = new SolidBrush(_checked ? UiTheme.Accent : UiTheme.T3))
                g.FillEllipse(b, 13, cy - 3, 6, 6);

            using (var f = UiTheme.Fpx(11F))
                UiTheme.Text(g, _checked ? OnText : OffText, f, UiTheme.T2,
                    new Rectangle(26, 0, Width - 30, Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnClick(EventArgs e) { Checked = !_checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }
}

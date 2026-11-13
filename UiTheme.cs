using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerDpiTool
{
    /// <summary>
    /// 「深海三栏」设计参考卡里的调色盘与尺子。
    /// 所有颜色、圆角、间距集中在这里，改一处整窗跟着变。
    /// </summary>
    internal static class UiTheme
    {
        // 底色：一整块从深青过渡到墨绿的画布
        public static readonly Color BgTop = Color.FromArgb(14, 50, 66);       // #0E3242 顶栏
        public static readonly Color BgRail = Color.FromArgb(16, 58, 72);      // #103A48 图标栏
        public static readonly Color BgPanel = Color.FromArgb(26, 76, 87);     // #1A4C57 列表面板
        public static readonly Color BgFrom = Color.FromArgb(27, 75, 86);      // #1B4B56 渐变起点
        public static readonly Color BgTo = Color.FromArgb(62, 107, 76);       // #3E6B4C 渐变终点
        public static readonly Color Page = Color.FromArgb(8, 25, 31);         // #08191F 草稿页底色

        // 文字三档深浅，不换颜色只换档位
        public static readonly Color T1 = Color.FromArgb(232, 242, 241);       // #E8F2F1 主文字
        public static readonly Color T2 = Color.FromArgb(169, 194, 196);       // #A9C2C4 次文字
        public static readonly Color T3 = Color.FromArgb(126, 154, 156);       // #7E9A9C 弱文字

        // 全屏唯一亮色，只负责指路
        public static readonly Color Accent = Color.FromArgb(255, 122, 92);    // #FF7A5C 珊瑚橙
        public static readonly Color AccentText = Color.FromArgb(18, 38, 43);  // 橙底上的深色字

        public const int Radius = 8;          // 控件圆角
        public const int WindowRadius = 10;   // 窗口圆角
        public const int TopHeight = 40;      // 顶栏
        public const int NoteHeight = 28;     // 提示条
        public const int RailWidth = 48;      // 图标栏
        public const int PanelWidth = 200;    // 列表面板

        /// <summary>半透明白浮层（8~12% 白）。WinForm 自绘时支持真实透明度。</summary>
        public static Color Layer(int alpha) { return Color.FromArgb(alpha, 255, 255, 255); }
        public static Color Line(int alpha) { return Color.FromArgb(alpha, 255, 255, 255); }

        /// <summary>输入框 / 按钮用的不透明近似色（原生控件不支持半透明背景色）。</summary>
        public static readonly Color FieldOnPanel = Color.FromArgb(49, 95, 105);
        public static readonly Color FieldOnCanvas = Color.FromArgb(52, 101, 108);
        public static readonly Color LogBack = Color.FromArgb(22, 61, 70);

        // ---- 全窗统一字体：微软雅黑 Light ----
        // 系统缺这个字体时（极少见）退回微软雅黑 UI，保证不会崩。
        private static readonly string FontFamily = ResolveFamily();

        private static string ResolveFamily()
        {
            try
            {
                using (var probe = new Font("Microsoft YaHei Light", 9F, FontStyle.Regular, GraphicsUnit.Point))
                    return probe.Name;
            }
            catch (Exception)
            {
                return "Microsoft YaHei UI";
            }
        }

        /// <summary>正文用字。</summary>
        public static Font F(float size) { return new Font(FontFamily, size, FontStyle.Regular, GraphicsUnit.Point); }

        /// <summary>
        /// 按「像素」给字号。设计稿里的 84px 大数字、12px 正文都按 px 标注，
        /// 用点制换算容易差半档，这里直接用像素单位，和草稿一比一。
        /// </summary>
        public static Font Fpx(float px) { return new Font(FontFamily, px, FontStyle.Regular, GraphicsUnit.Pixel); }

        /// <summary>橙色带透明度，用来画光环、浅底。</summary>
        public static Color AccentAlpha(int alpha) { return Color.FromArgb(alpha, Accent); }

        // 不提供 Bold 版本：雅黑 Light 没有真正的粗体字族，FontStyle.Bold 会由 GDI+
        // 合成「假粗」——笔画边缘糊，中文尤其明显。要强调就用字号与颜色建立层级。

        /// <summary>日志用字：同样跟全窗统一为雅黑 Light（不再用等宽 Consolas）。</summary>
        public static Font Mono(float size) { return new Font(FontFamily, size, FontStyle.Regular, GraphicsUnit.Point); }

        /// <summary>让线条与文字平滑，不锯齿。</summary>
        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        /// <summary>
        /// 一套玻璃配方：底色浮层 + 1px 描边。所有玻璃控件都必须走这里，
        /// 才能保证「读取当前 DPI」「X/Y 相同」「写入板载存储」三处完全一致。
        /// </summary>
        public static void PaintGlass(Graphics g, Rectangle r, bool hover)
        {
            using (var b = new SolidBrush(Layer(hover ? 32 : 20)))
                FillRound(g, b, r, Radius);
            DrawRound(g, Line(hover ? 70 : 40), r, Radius);
        }

        /// <summary>圆角矩形路径，控件与按钮共用。</summary>
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Brush brush, Rectangle r, int radius)
        {
            using (var path = RoundRect(r, radius)) g.FillPath(brush, path);
        }

        public static void DrawRound(Graphics g, Color color, Rectangle r, int radius, float width = 1f)
        {
            using (var path = RoundRect(r, radius))
            using (var pen = new Pen(color, width)) g.DrawPath(pen, path);
        }

        /// <summary>从左上到右下的整块渐变画布（参考卡的核心）。</summary>
        public static void PaintCanvas(Graphics g, Rectangle r)
        {
            using (var brush = new LinearGradientBrush(
                new Point(r.Left, r.Top), new Point(r.Right, r.Bottom), BgFrom, BgTo))
            {
                g.FillRectangle(brush, r);
            }
        }

        /// <summary>画一行文字（GDI 渲染，中文更清晰）。</summary>
        public static void Text(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, r, color, flags);
        }

        /// <summary>
        /// 按「点」字号画一行文字，内部自建并释放 Font。
        /// 自绘代码一律走这个重载——否则每次重绘都会漏掉一个 Font 句柄。
        /// </summary>
        public static void TextPt(Graphics g, string text, float pt, Color color, Rectangle r, TextFormatFlags flags)
        {
            using (var font = F(pt))
                TextRenderer.DrawText(g, text, font, r, color, flags);
        }

        /// <summary>左侧图标栏的 4 枚线性图标。</summary>
        public static void PaintRailIcon(Graphics g, int index, Rectangle box, Color color)
        {
            Smooth(g);
            using (var pen = new Pen(color, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                int cx = box.Left + box.Width / 2;
                int cy = box.Top + box.Height / 2;
                switch (index)
                {
                    case 0: // 鼠标：竖长圆角矩形 + 中缝
                        using (var path = RoundRect(new Rectangle(cx - 5, cy - 9, 10, 18), 5))
                            g.DrawPath(pen, path);
                        g.DrawLine(pen, cx, cy - 9, cx, cy - 3);
                        break;
                    case 1: // 仪表盘：圆 + 指针 + 圆心
                        g.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
                        g.DrawLine(pen, cx, cy, cx + 5, cy - 5);
                        using (var b = new SolidBrush(color)) g.FillEllipse(b, cx - 1.5f, cy - 1.5f, 3, 3);
                        break;
                    case 2: // 滑杆：三条横线 + 三个滑块
                        g.DrawLine(pen, cx - 8, cy - 6, cx + 8, cy - 6);
                        g.DrawLine(pen, cx - 8, cy, cx + 8, cy);
                        g.DrawLine(pen, cx - 8, cy + 6, cx + 8, cy + 6);
                        using (var b = new SolidBrush(BgRail))
                        {
                            g.FillEllipse(b, cx - 6, cy - 8, 4, 4);
                            g.FillEllipse(b, cx + 2, cy - 2, 4, 4);
                            g.FillEllipse(b, cx - 4, cy + 4, 4, 4);
                        }
                        g.DrawEllipse(pen, cx - 6, cy - 8, 4, 4);
                        g.DrawEllipse(pen, cx + 2, cy - 2, 4, 4);
                        g.DrawEllipse(pen, cx - 4, cy + 4, 4, 4);
                        break;
                    case 3: // 日志：三行列表
                        g.DrawLine(pen, cx - 8, cy - 6, cx + 8, cy - 6);
                        g.DrawLine(pen, cx - 8, cy, cx + 8, cy);
                        g.DrawLine(pen, cx - 8, cy + 6, cx + 3, cy + 6);
                        break;
                    default: // 问号：使用说明
                        g.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
                        using (var f = Fpx(11F))
                        {
                            TextRenderer.DrawText(g, "?", f,
                                new Rectangle(cx - 9, cy - 9, 18, 18), color,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                TextFormatFlags.NoPadding);
                        }
                        break;
                }
            }
        }

        // ---- 无边框窗口需要的几个系统小工具 ----
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;

        /// <summary>
        /// 把窗口切成 10px 圆角。
        /// CreateRoundRectRgn 返回的是裸 GDI 句柄，FromHrgn 只复制区域数据、不接管所有权，
        /// 所以这里必须 DeleteObject，否则每调一次漏一个 GDI 句柄。
        /// </summary>
        public static void ApplyRoundRegion(Form form)
        {
            Region old = form.Region;
            Region created = null;

            IntPtr rgn = CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1,
                WindowRadius * 2, WindowRadius * 2);
            if (rgn != IntPtr.Zero)
            {
                try { created = Region.FromHrgn(rgn); }
                finally { DeleteObject(rgn); }
            }

            form.Region = created;     // 创建失败时退化为直角窗口，不影响使用
            if (old != null) old.Dispose();
        }

        /// <summary>按住顶栏拖动窗口。</summary>
        public static void DragWindow(Form form)
        {
            ReleaseCapture();
            SendMessage(form.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }
    }
}

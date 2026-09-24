using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace InstantLock
{
    public static class Pal
    {
        public static readonly Color HeaderA = Color.FromArgb(74, 59, 53);
        public static readonly Color HeaderB = Color.FromArgb(41, 32, 28);
        public static readonly Color Cyan = Color.FromArgb(53, 205, 232);
        public static readonly Color CyanDark = Color.FromArgb(24, 164, 197);
        public static readonly Color CyanDeep = Color.FromArgb(15, 128, 158);
        public static readonly Color Body = Color.FromArgb(246, 247, 248);
        public static readonly Color Strip = Color.FromArgb(233, 235, 238);
        public static readonly Color Line = Color.FromArgb(206, 210, 216);
        public static readonly Color Text = Color.FromArgb(51, 51, 51);
        public static readonly Color Muted = Color.FromArgb(122, 126, 133);
        public static readonly Color White = Color.White;
        public static readonly Color Warn = Color.FromArgb(214, 88, 60);
        public static readonly Color Green = Color.FromArgb(58, 168, 116);
        public static readonly Color Card = Color.FromArgb(250, 251, 252);
        public static readonly Color CardLine = Color.FromArgb(226, 230, 234);

        public static Color HeaderAt(float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(HeaderA.R + (HeaderB.R - HeaderA.R) * t),
                (int)(HeaderA.G + (HeaderB.G - HeaderA.G) * t),
                (int)(HeaderA.B + (HeaderB.B - HeaderA.B) * t));
        }
    }

    public static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void FillRoundGradient(Graphics g, Rectangle r, int radius, Color c1, Color c2, bool vertical)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (LinearGradientBrush b = new LinearGradientBrush(
                vertical ? new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height)) : new Rectangle(r.X, r.Y, Math.Max(1, r.Width), r.Height),
                c1, c2, vertical ? 90f : 0f))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c)) { g.DrawPath(pen, p); }
        }

        public static GraphicsPath BezierSwoosh(int w, int h)
        {
            GraphicsPath p = new GraphicsPath();
            int top = (int)(h * 0.52);
            p.AddLine(0, top, 0, h);
            p.AddLine(0, h, w, h);
            p.AddBezier(w, h, (int)(w * 0.72), h, (int)(w * 0.66), top + 10, (int)(w * 0.52), top + 4);
            p.AddBezier((int)(w * 0.40), top, (int)(w * 0.34), 0, (int)(w * 0.18), 0, 0, 0);
            p.AddLine(0, 0, 0, top);
            p.CloseFigure();
            return p;
        }
    }

    public class SkinForm : Form
    {
        protected bool Dragging;
        protected Point DragStart;
        private bool dragging;

        public SkinForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("SimSun", 9.75f);
            BackColor = Pal.Body;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnLoad(EventArgs e)
        {
            float f = DeviceDpi / 96f;
            Diag.Log("OnLoad dpi=" + DeviceDpi + " factor=" + f.ToString("F2"));
            if (Math.Abs(f - 1f) > 0.01f)
            {
                SuspendLayout();
                Ui.ScaleTree(this, f);
                ClientSize = new Size((int)Math.Round(ClientSize.Width * f), (int)Math.Round(ClientSize.Height * f));
                ResumeLayout(true);
                Diag.Log("scaled to " + ClientSize.Width + "x" + ClientSize.Height);
            }
            base.OnLoad(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < 52)
            {
                dragging = true;
                DragStart = new Point(e.X, e.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Point sp = PointToScreen(new Point(e.X, e.Y));
                Location = new Point(sp.X - DragStart.X, sp.Y - DragStart.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected void PaintChrome(Graphics g, int headerHeight, bool bottomStrip, int stripHeight)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle all = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (SolidBrush b = new SolidBrush(Pal.Body)) g.FillRectangle(b, all);
            int hh = headerHeight;
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, ClientSize.Width, hh), Pal.HeaderA, Pal.HeaderB, 0f))
                g.FillRectangle(b, new Rectangle(0, 0, ClientSize.Width, hh));
            int swooshW = Math.Max(120, ClientSize.Width / 3);
            using (GraphicsPath p = Draw.BezierSwoosh(swooshW, hh + 26))
            {
                g.TranslateTransform(ClientSize.Width - swooshW, 0);
                using (SolidBrush b = new SolidBrush(Pal.Cyan)) g.FillPath(b, p);
                g.TranslateTransform(-(ClientSize.Width - swooshW), 0);
            }
            using (SolidBrush b = new SolidBrush(Pal.Body)) g.FillRectangle(b, 0, hh, ClientSize.Width, ClientSize.Height - hh);
            if (bottomStrip)
            {
                using (SolidBrush b = new SolidBrush(Pal.Strip))
                    g.FillRectangle(b, 0, ClientSize.Height - stripHeight, ClientSize.Width, stripHeight);
                using (Pen p = new Pen(Pal.Line))
                    g.DrawLine(p, 0, ClientSize.Height - stripHeight, ClientSize.Width, ClientSize.Height - stripHeight);
            }
            using (Pen p = new Pen(Pal.Line))
                g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    public class FlatButton : Control
    {
        private bool hover, down;
        public bool Accent = true;
        public bool Small;
        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("SimSun", 9.75f);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color c1, c2, border;
            if (!Enabled) { c1 = Color.FromArgb(214, 216, 220); c2 = Color.FromArgb(199, 202, 207); border = Pal.Line; }
            else if (Accent)
            {
                c1 = down ? Pal.CyanDeep : (hover ? Color.FromArgb(78, 219, 241) : Color.FromArgb(66, 212, 238));
                c2 = down ? Pal.CyanDark : (hover ? Pal.CyanDark : Color.FromArgb(31, 176, 208));
                border = Pal.CyanDark;
            }
            else
            {
                c1 = down ? Color.FromArgb(224, 226, 229) : (hover ? Color.FromArgb(246, 247, 249) : Color.FromArgb(238, 239, 242));
                c2 = down ? Color.FromArgb(205, 208, 213) : (hover ? Color.FromArgb(232, 234, 238) : Color.FromArgb(224, 226, 230));
                border = Pal.Line;
            }
            Draw.FillRoundGradient(g, r, 4, c1, c2, true);
            Draw.StrokeRound(g, r, 4, border);
            Color fg = Accent && Enabled ? Color.White : (Enabled ? Pal.Text : Pal.Muted);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    public enum Glyph { Close, Gear, Globe, Refresh, Help, Folder, Lock, Key, Search, Eye, Shield, Warning, Check, Disk, Tray, Info, Doc, Mail }

    public class IconButton : Control
    {
        private bool hover;
        public Glyph Icon = Glyph.Close;
        public Color IconColor = Pal.White;
        public IconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(16, 16);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Icon == Glyph.Close)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, hover ? Color.FromArgb(232, 106, 78) : Pal.HeaderA, hover ? Color.FromArgb(196, 63, 40) : Pal.HeaderB, 90f))
                    g.FillRectangle(b, ClientRectangle);
                using (Pen p = new Pen(Color.White, 2f))
                {
                    int cx = Width / 2, cy = Height / 2;
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                }
                return;
            }
            float s = Math.Min(Width, Height) / 16f;
            g.TranslateTransform((Width - 16 * s) / 2, (Height - 16 * s) / 2);
            g.ScaleTransform(s, s);
            Color c = Enabled ? (hover ? Pal.CyanDeep : IconColor) : Pal.Muted;
            using (Pen p = new Pen(c, 1.6f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; Glyphs.Paint(g, Icon, p, c); }
            g.ResetTransform();
        }
    }

    public static class Glyphs
    {
        public static void Paint(Graphics g, Glyph ic, Pen p, Color c)
        {
            switch (ic)
            {
                case Glyph.Gear:
                    g.DrawEllipse(p, 6.5f, 6.5f, 3f, 3f);
                    g.DrawEllipse(p, 3.2f, 3.2f, 9.6f, 9.6f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        float x1 = 8 + (float)(Math.Cos(a) * 4.8), y1 = 8 + (float)(Math.Sin(a) * 4.8);
                        float x2 = 8 + (float)(Math.Cos(a) * 7.2), y2 = 8 + (float)(Math.Sin(a) * 7.2);
                        g.DrawLine(p, x1, y1, x2, y2);
                    }
                    break;
                case Glyph.Globe:
                    g.DrawEllipse(p, 1.5f, 1.5f, 13f, 13f);
                    g.DrawLine(p, 1.5f, 8f, 14.5f, 8f);
                    g.DrawEllipse(p, 5f, 1.5f, 6f, 13f);
                    g.DrawArc(p, 2.6f, 3.4f, 10.8f, 9f, 200, 140);
                    g.DrawArc(p, 2.6f, 3.6f, 10.8f, 9f, 20, 140);
                    break;
                case Glyph.Refresh:
                    g.DrawArc(p, 2f, 2f, 12f, 12f, 40, 290);
                    using (SolidBrush b = new SolidBrush(c))
                    {
                        PointF[] tri = new PointF[] { new PointF(12.6f, 1.4f), new PointF(14.6f, 5.4f), new PointF(10.2f, 4.6f) };
                        g.FillPolygon(b, tri);
                    }
                    break;
                case Glyph.Help:
                    g.DrawEllipse(p, 1.5f, 1.5f, 13f, 13f);
                    g.DrawArc(p, 5.6f, 4.2f, 4.8f, 4.4f, 180, 240);
                    g.DrawLine(p, 8f, 8.6f, 8f, 10.2f);
                    g.DrawLine(p, 8f, 12.4f, 8f, 12.5f);
                    break;
                case Glyph.Folder:
                    g.DrawLines(p, new PointF[] { new PointF(1.8f, 13.2f), new PointF(1.8f, 3.2f), new PointF(6.2f, 3.2f), new PointF(7.6f, 5.2f), new PointF(14.2f, 5.2f), new PointF(14.2f, 13.2f), new PointF(1.8f, 13.2f) });
                    break;
                case Glyph.Lock:
                    g.DrawRectangle(p, 3.4f, 7.2f, 9.2f, 7f);
                    g.DrawArc(p, 5.2f, 2.6f, 5.6f, 6.4f, 180, 180);
                    g.DrawLine(p, 8f, 9.6f, 8f, 11.8f);
                    break;
                case Glyph.Key:
                    g.DrawEllipse(p, 1.8f, 5.4f, 5.2f, 5.2f);
                    g.DrawLine(p, 6.6f, 8f, 14f, 8f);
                    g.DrawLine(p, 11.4f, 8f, 11.4f, 10.6f);
                    g.DrawLine(p, 13.6f, 8f, 13.6f, 10f);
                    break;
                case Glyph.Search:
                    g.DrawEllipse(p, 2.2f, 2.2f, 8.4f, 8.4f);
                    g.DrawLine(p, 9.8f, 9.8f, 14f, 14f);
                    break;
                case Glyph.Eye:
                    g.DrawArc(p, 1.4f, 3.4f, 13.2f, 9.2f, 180, 180);
                    g.DrawArc(p, 1.4f, 3.4f, 13.2f, 9.2f, 0, 180);
                    g.DrawEllipse(p, 6.2f, 6.2f, 3.6f, 3.6f);
                    break;
                case Glyph.Shield:
                    g.DrawLines(p, new PointF[] { new PointF(8f, 1.6f), new PointF(13.6f, 4f), new PointF(13.6f, 8f), new PointF(8f, 14.2f), new PointF(2.4f, 8f), new PointF(2.4f, 4f), new PointF(8f, 1.6f) });
                    break;
                case Glyph.Warning:
                    g.DrawLines(p, new PointF[] { new PointF(8f, 1.8f), new PointF(14.6f, 13.6f), new PointF(1.4f, 13.6f), new PointF(8f, 1.8f) });
                    g.DrawLine(p, 8f, 6.4f, 8f, 10f);
                    g.DrawLine(p, 8f, 11.6f, 8f, 11.7f);
                    break;
                case Glyph.Check:
                    g.DrawLines(p, new PointF[] { new PointF(2.4f, 8.4f), new PointF(6.2f, 12f), new PointF(13.6f, 3.8f) });
                    break;
                case Glyph.Disk:
                    g.DrawRectangle(p, 2.2f, 2.6f, 11.6f, 10.8f);
                    g.DrawLine(p, 5f, 2.6f, 5f, 7f);
                    g.DrawLine(p, 6.4f, 2.6f, 6.4f, 7f);
                    g.DrawEllipse(p, 10f, 9.4f, 2.2f, 2.2f);
                    break;
                case Glyph.Tray:
                    g.DrawRectangle(p, 2.4f, 2.6f, 11.2f, 10.8f);
                    g.DrawLine(p, 2.4f, 9.4f, 6f, 9.4f);
                    g.DrawLine(p, 6f, 9.4f, 6.6f, 10.8f);
                    g.DrawLine(p, 6.6f, 10.8f, 9.4f, 10.8f);
                    g.DrawLine(p, 9.4f, 10.8f, 10f, 9.4f);
                    g.DrawLine(p, 10f, 9.4f, 13.6f, 9.4f);
                    break;
                case Glyph.Info:
                    g.DrawEllipse(p, 1.5f, 1.5f, 13f, 13f);
                    g.DrawLine(p, 8f, 7f, 8f, 11.6f);
                    g.DrawLine(p, 8f, 4.4f, 8f, 4.5f);
                    break;
                case Glyph.Mail:
                    g.DrawRectangle(p, 1.8f, 3.8f, 12.4f, 8.6f);
                    g.DrawLines(p, new PointF[] { new PointF(1.8f, 3.8f), new PointF(8f, 9.2f), new PointF(14.2f, 3.8f) });
                    break;
                case Glyph.Doc:
                    g.DrawLines(p, new PointF[] { new PointF(3.4f, 1.8f), new PointF(9.4f, 1.8f), new PointF(12.6f, 5f), new PointF(12.6f, 14.2f), new PointF(3.4f, 14.2f), new PointF(3.4f, 1.8f) });
                    g.DrawLines(p, new PointF[] { new PointF(9.4f, 1.8f), new PointF(9.4f, 5f), new PointF(12.6f, 5f) });
                    break;
            }
        }
    }

    public class SkinEdit : UserControl
    {
        private readonly TextBox box = new TextBox();
        private bool hover, focus;
        public event EventHandler TextChanged2;
        public bool UseSystemPasswordChar { get { return box.UseSystemPasswordChar; } set { box.UseSystemPasswordChar = value; } }

        public SkinEdit()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("SimSun", 9.75f);
            box.BackColor = Color.White;
            box.ForeColor = Pal.Text;
            TabStop = true;
            Controls.Add(box);
            box.KeyDown += delegate(object ks, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Tab)
                {
                    ke.SuppressKeyPress = true;
                    Form host = FindForm();
                    if (host != null) host.SelectNextControl(this, !ke.Shift, true, true, true);
                }
            };
            box.GotFocus += delegate { focus = true; Invalidate(); };
            box.LostFocus += delegate { focus = false; Invalidate(); };
            box.MouseEnter += delegate { hover = true; Invalidate(); };
            box.MouseLeave += delegate { hover = false; Invalidate(); };
            box.TextChanged += delegate { if (TextChanged2 != null) TextChanged2(this, EventArgs.Empty); };
        }

        public override string Text
        {
            get { return box.Text; }
            set { box.Text = value; }
        }

        public TextBox Inner { get { return box; } }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))
            {
                Form host = FindForm();
                if (host != null)
                {
                    bool okNext = host.SelectNextControl(this, keyData == Keys.Tab, true, true, true);
                    if (okNext) return true;
                }
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            if (box != null && !box.Focused) box.Focus();
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (box != null) box.Font = Font; }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); box.Enabled = Enabled; Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Math.Max(6, Height / 5);
            box.Location = new Point(pad, Math.Max(2, (Height - box.PreferredHeight) / 2));
            box.Width = Math.Max(10, Width - pad * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(240, 241, 243))) g.FillRectangle(b, r);
            Color bc = !Enabled ? Pal.Line : (focus ? Pal.Cyan : (hover ? Color.FromArgb(168, 174, 182) : Pal.Line));
            Draw.StrokeRound(g, r, 3, bc);
            if (focus) Draw.StrokeRound(g, new Rectangle(1, 1, Width - 3, Height - 3), 3, Color.FromArgb(190, 238, 248));
        }
    }

    public class FlatCheck : Control
    {
        private bool hover;
        public bool Checked;
        public event EventHandler CheckedChanged;
        public FlatCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("SimSun", 9.75f);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Checked = !Checked;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(0, (Height - 14) / 2, 14, 14);
            if (Checked)
            {
                Draw.FillRoundGradient(g, box, 3, Color.FromArgb(78, 219, 241), Pal.CyanDark, true);
                Draw.StrokeRound(g, box, 3, Pal.CyanDeep);
                using (Pen p = new Pen(Color.White, 1.9f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLines(p, new PointF[] { new PointF(box.X + 3f, box.Y + 7.2f), new PointF(box.X + 5.8f, box.Y + 10f), new PointF(box.X + 11f, box.Y + 4f) });
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillRectangle(b, box);
                Draw.StrokeRound(g, box, 3, hover ? Pal.CyanDark : Pal.Line);
            }
            Rectangle tr = new Rectangle(box.Right + 7, 0, Math.Max(10, Width - box.Right - 7), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Pal.Text : Pal.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }
    }

    public class FlatGauge : Control
    {
        public double Value;
        public FlatGauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
        }
        public void Set(double v) { Value = Math.Max(0, Math.Min(100, v)); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Draw.FillRound(g, r, Math.Min(3, Height / 2), Color.FromArgb(236, 238, 241));
            Draw.StrokeRound(g, r, Math.Min(3, Height / 2), Pal.Line);
            int w = (int)((Width - 2) * Value / 100.0);
            if (w > 2)
            {
                Rectangle f = new Rectangle(1, 1, w, Height - 2);
                Draw.FillRoundGradient(g, f, Math.Min(3, Height / 2), Color.FromArgb(92, 224, 245), Pal.CyanDark, false);
            }
        }
    }

    public class SkinLabel : Control
    {
        public ContentAlignment Align = ContentAlignment.MiddleLeft;
        public bool Ellipsis;
        public SkinLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Pal.Body;
            ForeColor = Pal.Text;
            TabStop = false;
            Font = new Font("SimSun", 9.75f);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (BackColor != Color.Transparent)
            {
                using (SolidBrush bg = new SolidBrush(BackColor)) e.Graphics.FillRectangle(bg, ClientRectangle);
            }
            TextFormatFlags f = TextFormatFlags.NoPrefix;
            if (Ellipsis)
                f |= TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            else
                f |= TextFormatFlags.WordBreak;
            switch (Align)
            {
                case ContentAlignment.MiddleRight: f |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleCenter: f |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.TopLeft: f |= TextFormatFlags.Left | TextFormatFlags.Top; break;
                case ContentAlignment.TopRight: f |= TextFormatFlags.Right | TextFormatFlags.Top; break;
                case ContentAlignment.TopCenter: f |= TextFormatFlags.HorizontalCenter | TextFormatFlags.Top; break;
                default: f |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter; break;
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Pal.Muted, f);
        }
    }

    public class ArrowStrip : Control
    {
        private bool hover;
        public bool PointLeft = true;
        public ArrowStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(hover ? Color.FromArgb(222, 240, 245) : Color.FromArgb(236, 238, 241))) g.FillRectangle(b, r);
            using (Pen p = new Pen(Pal.Line)) g.DrawRectangle(p, r);
            int cx = Width / 2, cy = Height / 2;
            PointF[] tri;
            if (PointLeft)
                tri = new PointF[] { new PointF(cx + 2f, cy - 6f), new PointF(cx + 2f, cy + 6f), new PointF(cx - 3f, cy) };
            else
                tri = new PointF[] { new PointF(cx - 2f, cy - 6f), new PointF(cx - 2f, cy + 6f), new PointF(cx + 3f, cy) };
            using (SolidBrush b = new SolidBrush(hover ? Pal.CyanDeep : Pal.Muted)) g.FillPolygon(b, tri);
        }
    }

    public class HeaderBar : Control
    {
        public string Title = "";
        public string Version = "";
        public HeaderBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int badge = Math.Min(Height, 34);
            Rectangle r = new Rectangle(0, 0, badge, badge);
            Draw.FillRoundGradient(g, r, 6, Color.FromArgb(78, 219, 241), Pal.CyanDeep, true);
            using (Pen p = new Pen(Color.White, 1.8f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.TranslateTransform(badge / 2 - 8, badge / 2 - 8);
                Glyphs.Paint(g, Glyph.Shield, p, Color.White);
                g.ResetTransform();
            }
            using (Font f = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, f, new Rectangle(badge + 9, 0, Width - badge - 12, badge), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Version))
            {
                using (Font f = new Font("Microsoft YaHei UI", 8f))
                {
                    Size sz = TextRenderer.MeasureText(Version, f);
                    TextRenderer.DrawText(g, Version, f, new Rectangle(Width - sz.Width - 2, 0, sz.Width, badge), Color.FromArgb(214, 232, 240),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}

















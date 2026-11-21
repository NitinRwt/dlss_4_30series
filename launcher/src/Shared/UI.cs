// Shared dark UI toolkit for the DLSSG Launcher and its setup (WinForms, .NET Framework 4.x, C# 5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DlssgUi
{
    public static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public static void DarkTitle(IntPtr h)
        {
            try { int on = 1; if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); } catch { }
        }
        public static void DarkScroll(Control c) { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
        public static void DarkCfd(Control c) { try { SetWindowTheme(c.Handle, "DarkMode_CFD", null); } catch { } }
        public static void CueBanner(TextBox t, string text) { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, text); } catch { } }
    }

    public static class Glyphs
    {
        public const string Play = "", Settings = "", Folder = "", Delete = "", Add = "",
            Refresh = "", Search = "", Doc = "", Warning = "", Check = "", Shield = "",
            Download = "", Link = "", Info = "", Game = "", Save = "", Close = "",
            Edit = "", Error = "", Question = "", Library = "", App = "", Accept = "",
            Rocket = "", Hide = "", OpenFile = "", Forward = "", Back = "";
    }

    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(16, 18, 22);
        public static readonly Color Side = Color.FromArgb(22, 25, 30);
        public static readonly Color Card = Color.FromArgb(29, 33, 40);
        public static readonly Color Input = Color.FromArgb(39, 44, 53);
        public static readonly Color Hover = Color.FromArgb(49, 55, 66);
        public static readonly Color Border = Color.FromArgb(57, 63, 75);
        public static readonly Color Text = Color.FromArgb(235, 237, 240);
        public static readonly Color Muted = Color.FromArgb(148, 155, 167);
        public static readonly Color Accent = Color.FromArgb(118, 185, 0);
        public static readonly Color AccentHover = Color.FromArgb(136, 205, 16);
        public static readonly Color AccentPress = Color.FromArgb(96, 154, 0);
        public static readonly Color OnAccent = Color.FromArgb(12, 18, 4);
        public static readonly Color Danger = Color.FromArgb(232, 78, 84);
        public static readonly Color Warn = Color.FromArgb(242, 170, 50);
        public static readonly Color Info = Color.FromArgb(96, 160, 255);
        public static readonly Color Selected = Color.FromArgb(36, 46, 30);

        public static float Scale = 1f;
        public static Font Body, Small, Bold, H1, H2, H3, Button, Glyph, GlyphSmall, GlyphLarge, GlyphHuge, Mono;
        public static string IconFont = "Segoe MDL2 Assets";

        public static void Init()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            Body = new Font("Segoe UI", 9.5f);
            Small = new Font("Segoe UI", 8.5f);
            Bold = new Font("Segoe UI Semibold", 9.5f);
            H1 = new Font("Segoe UI Semibold", 18f);
            H2 = new Font("Segoe UI Semibold", 12f);
            H3 = new Font("Segoe UI Semibold", 10.5f);
            Button = new Font("Segoe UI Semibold", 9.5f);
            using (var fonts = new InstalledFontCollection())
                foreach (FontFamily ff in fonts.Families) if (ff.Name == "Segoe Fluent Icons") { IconFont = ff.Name; break; }
            Glyph = new Font(IconFont, 10.5f);
            GlyphSmall = new Font(IconFont, 8f);
            GlyphLarge = new Font(IconFont, 14f);
            GlyphHuge = new Font(IconFont, 30f);
            Mono = new Font("Consolas", 9f);
        }

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float Sf(float v) { return v * Scale; }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        // App logo: green rounded square with a white "play" triangle
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            using (var p = Round(r, r.Width * 0.24f))
            using (var b = new LinearGradientBrush(r, Color.FromArgb(146, 210, 24), Color.FromArgb(78, 146, 0), LinearGradientMode.Vertical))
                g.FillPath(b, p);
            var tri = new PointF[] {
                new PointF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.27f),
                new PointF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.73f),
                new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.50f) };
            g.FillPolygon(Brushes.White, tri);
        }

        public static void DrawGlyph(Graphics g, string glyph, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, glyph, f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public interface IAutoHeight { int HeightFor(int width); }

    // ------------------------------------------------------------------------------------------------
    public class DarkForm : Form
    {
        public DarkForm()
        {
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(Handle); }
    }

    public class UiControl : Control
    {
        protected bool hover, down;
        public UiControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Body; ForeColor = Theme.Text;
        }
        protected Color ParentBack { get { return Parent != null ? Parent.BackColor : Theme.Bg; } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    public enum BtnKind { Primary, Secondary, Danger, Ghost }

    public class FlatButton : UiControl
    {
        public BtnKind Kind;
        public string Glyph;
        public bool Big;
        public FlatButton(string text, string glyph, BtnKind kind)
        {
            Text = text; Glyph = glyph; Kind = kind;
            Cursor = Cursors.Hand; Font = Theme.Button; Height = Theme.S(34);
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
        }

        Font TextFont { get { return Big ? Theme.H2 : Font; } }
        Font GlyphFont { get { return Big ? Theme.GlyphLarge : Theme.Glyph; } }

        public int PreferredWidth
        {
            get
            {
                int w = Theme.S(Big ? 44 : 28);
                if (!string.IsNullOrEmpty(Text)) w += TextRenderer.MeasureText(Text, TextFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (!string.IsNullOrEmpty(Glyph)) w += TextRenderer.MeasureText(Glyph, GlyphFont, Size.Empty, TextFormatFlags.NoPadding).Width + (string.IsNullOrEmpty(Text) ? 0 : Theme.S(8));
                if (string.IsNullOrEmpty(Text)) w = Math.Max(Height, w - Theme.S(12));
                return w;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(ParentBack);
            Color bg, fg, border = Color.Empty;
            switch (Kind)
            {
                case BtnKind.Primary:
                    bg = down ? Theme.AccentPress : hover ? Theme.AccentHover : Theme.Accent; fg = Theme.OnAccent; break;
                case BtnKind.Danger:
                    bg = down ? Theme.Mix(Theme.Input, Theme.Danger, 0.45f) : hover ? Theme.Mix(Theme.Input, Theme.Danger, 0.3f) : Theme.Input;
                    fg = hover ? Color.White : Theme.Mix(Theme.Text, Theme.Danger, 0.55f); break;
                case BtnKind.Ghost:
                    bg = down ? Theme.Hover : hover ? Theme.Input : ParentBack; fg = Theme.Text; break;
                default:
                    bg = down ? Theme.Border : hover ? Theme.Hover : Theme.Input; fg = Theme.Text; border = Theme.Border; break;
            }
            if (!Enabled) { bg = Theme.Mix(bg, ParentBack, 0.55f); fg = Theme.Mix(fg, ParentBack, 0.5f); border = Color.Empty; }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Theme.Sf(Big ? 8 : 6);
            Theme.FillRound(g, r, rad, bg);
            if (border != Color.Empty && Kind == BtnKind.Secondary) Theme.StrokeRound(g, r, rad, Theme.Mix(bg, border, 0.6f), 1);
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), rad, Theme.Text, 1.2f);

            Size ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, TextFont, Size.Empty, TextFormatFlags.NoPadding);
            Size gs = string.IsNullOrEmpty(Glyph) ? Size.Empty : TextRenderer.MeasureText(g, Glyph, GlyphFont, Size.Empty, TextFormatFlags.NoPadding);
            int gap = (gs.Width > 0 && ts.Width > 0) ? Theme.S(8) : 0;
            int total = gs.Width + gap + ts.Width;
            int x = Math.Max(Theme.S(8), (Width - total) / 2);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            if (gs.Width > 0) TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(x, 0, gs.Width + 2, Height), fg, flags);
            if (ts.Width > 0) TextRenderer.DrawText(g, Text, TextFont, new Rectangle(x + gs.Width + gap, 0, Math.Min(ts.Width + 4, Width - x - gs.Width - gap), Height), fg, flags | TextFormatFlags.EndEllipsis);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyUp(e);
        }
    }

    public class Segmented : UiControl
    {
        string[] items = new string[0];
        int sel = -1, hot = -1;
        public bool[] Disabled;
        public event EventHandler SelectedChanged;
        public Segmented(params string[] items) { this.items = items; Height = Theme.S(34); Cursor = Cursors.Hand; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        public int SelectedIndex
        {
            get { return sel; }
            set { if (value == sel) return; sel = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }
        bool IsDisabled(int i) { return Disabled != null && i < Disabled.Length && Disabled[i]; }
        int IndexAt(int x) { if (items.Length == 0 || Width <= 0) return -1; return Math.Max(0, Math.Min(items.Length - 1, x * items.Length / Width)); }
        protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = IndexAt(e.X);
            if (i >= 0 && Enabled && !IsDisabled(i)) { Focus(); SelectedIndex = i; }
            base.OnMouseClick(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (step != 0)
            {
                int i = sel;
                for (int k = 0; k < items.Length; k++) { i = (i + step + items.Length) % items.Length; if (!IsDisabled(i)) break; }
                SelectedIndex = i; e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g); g.Clear(ParentBack);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Theme.Sf(7);
            Theme.FillRound(g, r, rad, Theme.Input);
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, r, rad, Theme.Muted, 1);
            int n = items.Length; if (n == 0) return;
            float w = (Width - Theme.Sf(6)) / n;
            for (int i = 0; i < n; i++)
            {
                var sr = new RectangleF(Theme.Sf(3) + i * w, Theme.Sf(3), w, Height - Theme.Sf(6));
                bool dis = IsDisabled(i) || !Enabled;
                Color fg = dis ? Theme.Mix(Theme.Muted, Theme.Input, 0.45f) : Theme.Text;
                if (i == sel) { Theme.FillRound(g, RectangleF.Inflate(sr, -Theme.Sf(1), 0), Theme.Sf(5), Enabled ? Theme.Accent : Theme.Border); fg = Enabled ? Theme.OnAccent : Theme.Text; }
                else if (i == hot && !dis) Theme.FillRound(g, RectangleF.Inflate(sr, -Theme.Sf(1), 0), Theme.Sf(5), Theme.Hover);
                TextRenderer.DrawText(g, items[i], i == sel ? Theme.Button : Font, Rectangle.Round(sr), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }

    public class Toggle : UiControl
    {
        bool on;
        public event EventHandler CheckedChanged;
        public Toggle(string text) { Text = text; Height = Theme.S(28); Cursor = Cursors.Hand; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnMouseClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } base.OnKeyUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g); g.Clear(ParentBack);
            float tw = Theme.Sf(40), th = Theme.Sf(22), y = (Height - th) / 2f;
            var track = new RectangleF(0.5f, y, tw, th);
            Color tc = on ? (Enabled ? Theme.Accent : Theme.Border) : (hover && Enabled ? Theme.Hover : Theme.Input);
            Theme.FillRound(g, track, th / 2, tc);
            if (!on) Theme.StrokeRound(g, track, th / 2, Theme.Border, 1);
            float k = th - Theme.Sf(8);
            float kx = on ? track.Right - k - Theme.Sf(4) : track.X + Theme.Sf(4);
            using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.Muted)) g.FillEllipse(b, kx, y + Theme.Sf(4), k, k);
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, RectangleF.Inflate(track, Theme.Sf(2), Theme.Sf(2)), th / 2 + 2, Theme.Muted, 1);
            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(tw + Theme.S(12)), 0, Width - (int)tw - Theme.S(12), Height),
                    Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    public class Choice
    {
        public string Value, Text;
        public Choice(string value, string text) { Value = value; Text = text; }
        public override string ToString() { return Text; }
    }

    public class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Input; ForeColor = Theme.Text; Font = Theme.Body; ItemHeight = Theme.S(26);
            IntegralHeight = false; MaxDropDownItems = 14;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkCfd(this); }

        // The flat combo draws a light drop-down button: paint our own button and border over it
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F || !IsHandleCreated) return;   // WM_PAINT
            using (var g = Graphics.FromHwnd(Handle))
            {
                int bw = SystemInformation.VerticalScrollBarWidth + Theme.S(4);
                var r = new Rectangle(Width - bw - 1, 1, bw, Height - 2);
                using (var b = new SolidBrush(Enabled ? Theme.Input : Theme.Mix(Theme.Input, Theme.Card, 0.5f))) g.FillRectangle(b, r);
                Theme.DrawGlyph(g, "", Theme.GlyphSmall, Enabled ? Theme.Muted : Theme.Border, r);
                using (var p = new Pen(Focused || DroppedDown ? Theme.Accent : Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !edit;
            using (var b = new SolidBrush(sel ? Theme.Hover : Theme.Input)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + Theme.S(6), e.Bounds.Y, e.Bounds.Width - Theme.S(8), e.Bounds.Height),
                    Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        public void SetChoices(IEnumerable<Choice> choices)
        {
            BeginUpdate(); Items.Clear(); foreach (var c in choices) Items.Add(c); EndUpdate();
        }
        public string Value
        {
            get { var c = SelectedItem as Choice; return c == null ? null : c.Value; }
        }
        // selects the choice with this value; an unknown value is added as "(current value)"
        public void SelectValue(string v)
        {
            for (int i = 0; i < Items.Count; i++) { var c = Items[i] as Choice; if (c != null && string.Equals(c.Value, v, StringComparison.OrdinalIgnoreCase)) { SelectedIndex = i; return; } }
            if (v == null) { SelectedIndex = Items.Count > 0 ? 0 : -1; return; }
            SelectedIndex = Items.Add(new Choice(v, v + "   (current value, not a standard choice)"));
        }
    }

    public class InputBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        public string Glyph;
        string cue;
        public InputBox(string glyph, string placeholder)
        {
            Glyph = glyph; cue = placeholder;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Input; Height = Theme.S(34); Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None; Box.BackColor = Theme.Input; Box.ForeColor = Theme.Text; Box.Font = Theme.Body;
            Controls.Add(Box);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.HandleCreated += delegate { if (cue != null) Native.CueBanner(Box, cue); };
            Click += delegate { Box.Focus(); };
        }
        public override string Text { get { return Box.Text; } set { Box.Text = value; } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = Theme.S(10) + (Glyph != null ? Theme.S(24) : 0);
            Box.SetBounds(left, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(10, Width - left - Theme.S(10)), Box.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, r, Theme.Sf(6), Theme.Input);
            Theme.StrokeRound(g, r, Theme.Sf(6), Box.Focused ? Theme.Accent : Theme.Border, 1);
            if (Glyph != null) Theme.DrawGlyph(g, Glyph, Theme.Glyph, Theme.Muted, new Rectangle(Theme.S(8), 0, Theme.S(22), Height));
        }
    }

    // A rounded card with an optional title; children are added as label / control rows
    public class Card : Panel
    {
        public class Row
        {
            public Label Label; public Control Control; public Label Hint;
            public bool Visible = true; public bool Full; public int FixedWidth;
        }
        public string Title, Glyph;
        public readonly List<Row> Rows = new List<Row>();
        public int LabelWidth = Theme.S(170);
        bool inLayout;

        public Card(string title, string glyph)
        {
            Title = title; Glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
            Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(20));
            Height = Theme.S(100);
        }

        public Row AddRow(string label, Control c, string hint, int fixedWidth)
        {
            var r = new Row { Control = c, FixedWidth = fixedWidth, Full = label == null };
            if (label != null)
            {
                r.Label = new Label { Text = label, AutoSize = false, ForeColor = Theme.Text, BackColor = Theme.Card, Font = Theme.Body, TextAlign = ContentAlignment.MiddleLeft };
                Controls.Add(r.Label);
            }
            r.Hint = new Label { Text = hint ?? "", AutoSize = false, ForeColor = Theme.Muted, BackColor = Theme.Card, Font = Theme.Small };
            Controls.Add(r.Hint);
            c.BackColor = c is InputBox || c is DarkCombo ? c.BackColor : Theme.Card;
            Controls.Add(c);
            Rows.Add(r);
            return r;
        }
        public Row AddFull(Control c, string hint) { return AddRow(null, c, hint, 0); }

        int HeaderH { get { return string.IsNullOrEmpty(Title) ? 0 : Theme.S(38); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (inLayout) return;
            inLayout = true;
            try
            {
                int x0 = Padding.Left, y = Padding.Top + HeaderH, w = ClientSize.Width - Padding.Horizontal;
                bool any = false;
                foreach (var r in Rows)
                {
                    r.Control.Visible = r.Visible;
                    if (r.Label != null) r.Label.Visible = r.Visible;
                    bool hintOn = r.Visible && !string.IsNullOrEmpty(r.Hint.Text);
                    r.Hint.Visible = hintOn;
                    if (!r.Visible) continue;
                    if (any) y += Theme.S(14);
                    any = true;
                    bool stacked = !r.Full && w < Theme.S(560);
                    int cx = r.Full ? x0 : x0 + LabelWidth;
                    if (stacked) { r.Label.SetBounds(x0, y, w, Theme.S(22)); y += Theme.S(24); cx = x0; }
                    int cw = x0 + w - cx;
                    if (r.FixedWidth > 0) cw = Math.Min(cw, r.FixedWidth);
                    int ch = r.Control is IAutoHeight ? ((IAutoHeight)r.Control).HeightFor(cw) : r.Control.Height;
                    r.Control.SetBounds(cx, y, cw, ch);
                    if (!stacked && r.Label != null) r.Label.SetBounds(x0, y + (Math.Min(ch, Theme.S(34)) - Theme.S(22)) / 2, LabelWidth - Theme.S(12), Theme.S(22));
                    y += ch;
                    if (hintOn)
                    {
                        int hw = r.Full ? w : x0 + w - cx;
                        int hh = TextRenderer.MeasureText(r.Hint.Text, r.Hint.Font, new Size(hw, 0), TextFormatFlags.WordBreak).Height;
                        r.Hint.SetBounds(cx, y + Theme.S(5), hw, hh);
                        y += hh + Theme.S(5);
                    }
                }
                y += Padding.Bottom;
                if (Height != y) Height = y;
            }
            finally { inLayout = false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, r, Theme.Sf(10), BackColor);
            Theme.StrokeRound(g, r, Theme.Sf(10), Theme.Mix(BackColor, Theme.Border, 0.55f), 1);
            if (!string.IsNullOrEmpty(Title))
            {
                int x = Padding.Left;
                if (Glyph != null)
                {
                    Theme.DrawGlyph(g, Glyph, Theme.Glyph, Theme.Accent, new Rectangle(x - Theme.S(2), Padding.Top - Theme.S(4), Theme.S(24), Theme.S(28)));
                    x += Theme.S(28);
                }
                TextRenderer.DrawText(g, Title, Theme.H2, new Rectangle(x, Padding.Top - Theme.S(4), Width - x - Padding.Right, Theme.S(28)), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Vertical, scrolling stack of full-width children
    public class VStack : Panel
    {
        public int Gap = Theme.S(16);
        public int MaxContentWidth = Theme.S(980);
        bool inLayout;
        public VStack()
        {
            AutoScroll = true; BackColor = Theme.Bg; Padding = new Padding(Theme.S(28), Theme.S(24), Theme.S(28), Theme.S(28));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkScroll(this); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (inLayout) return;
            inLayout = true;
            try
            {
                int avail = ClientSize.Width - Padding.Horizontal;
                if (avail < 60) return;
                int w = Math.Min(avail, MaxContentWidth);
                int x = Padding.Left + (avail - w) / 2;
                Point off = AutoScrollPosition;
                int y = Padding.Top;
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control c = Controls[i];
                    if (!c.Visible) continue;
                    c.SetBounds(x + off.X, y + off.Y, w, c.Height);
                    y += c.Height + Gap;
                }
                AutoScrollMinSize = new Size(0, y - Gap + Padding.Bottom);
            }
            finally { inLayout = false; }
        }
    }

    // Buttons left to right, wrapping
    public class ButtonRow : Panel, IAutoHeight
    {
        public int Gap = Theme.S(8);
        public ButtonRow() { BackColor = Theme.Card; }
        public void Add(params Control[] cs) { foreach (var c in cs) Controls.Add(c); }
        int Place(int width, bool apply)
        {
            int x = 0, y = 0, rowH = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int w = c is FlatButton ? ((FlatButton)c).PreferredWidth : c.Width;
                if (c is FlatButton && c.Tag is int) w = Math.Max(w, (int)c.Tag);
                if (x > 0 && x + w > width) { x = 0; y += rowH + Gap; rowH = 0; }
                if (apply) c.SetBounds(x, y, Math.Min(w, width), c.Height);
                x += w + Gap; rowH = Math.Max(rowH, c.Height);
            }
            return y + rowH;
        }
        public int HeightFor(int width) { return Math.Max(1, Place(width, false)); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Place(Width, true); }
    }

    // Two or more controls side by side; one of them (FillIndex) takes the remaining width
    public class HBox : Panel, IAutoHeight
    {
        public int Gap = Theme.S(8);
        public int FillIndex;
        public HBox(int fillIndex, params Control[] cs) { FillIndex = fillIndex; BackColor = Theme.Card; foreach (var c in cs) Controls.Add(c); }
        public int HeightFor(int width) { int h = 0; foreach (Control c in Controls) h = Math.Max(h, c.Height); return h; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int fixedW = 0;
            for (int i = 0; i < Controls.Count; i++) if (i != FillIndex) fixedW += (Controls[i] is FlatButton ? ((FlatButton)Controls[i]).PreferredWidth : Controls[i].Width) + Gap;
            int x = 0;
            for (int i = 0; i < Controls.Count; i++)
            {
                var c = Controls[i];
                int w = i == FillIndex ? Math.Max(Theme.S(60), Width - fixedW) : (c is FlatButton ? ((FlatButton)c).PreferredWidth : c.Width);
                c.SetBounds(x, (Height - c.Height) / 2, w, c.Height);
                x += w + Gap;
            }
        }
    }

    public class ProgressLine : UiControl
    {
        int value;
        int phase;
        readonly Timer anim = new Timer { Interval = 30 };
        public ProgressLine() { Height = Theme.S(6); anim.Tick += delegate { phase = (phase + 3) % 200; Invalidate(); }; }
        // -1 = indeterminate
        public int Value
        {
            get { return value; }
            set { this.value = Math.Min(100, value); anim.Enabled = this.value < 0 && Visible; Invalidate(); }
        }
        protected override void OnVisibleChanged(EventArgs e) { anim.Enabled = value < 0 && Visible; base.OnVisibleChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g); g.Clear(ParentBack);
            var r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, r, Height / 2f, Theme.Input);
            if (value >= 0) { if (value > 0) Theme.FillRound(g, new RectangleF(0, 0, Math.Max(Height, Width * value / 100f), Height), Height / 2f, Theme.Accent); }
            else
            {
                float seg = Width * 0.3f, x = (phase / 200f) * (Width + seg) - seg;
                var s = RectangleF.Intersect(new RectangleF(x, 0, seg, Height), r);
                if (s.Width > 0) Theme.FillRound(g, s, Height / 2f, Theme.Accent);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    public enum NoticeKind { Info, Warning, Danger, Success }

    // Tinted callout box with a glyph, bold title and wrapped text
    public class Notice : UiControl, IAutoHeight
    {
        public NoticeKind Kind;
        public string Title, Body;
        public Notice(NoticeKind kind, string title, string body) { Kind = kind; Title = title; Body = body; }
        Color Tone { get { return Kind == NoticeKind.Danger ? Theme.Danger : Kind == NoticeKind.Warning ? Theme.Warn : Kind == NoticeKind.Success ? Theme.Accent : Theme.Info; } }
        string GlyphText { get { return Kind == NoticeKind.Info ? Glyphs.Info : Kind == NoticeKind.Success ? Glyphs.Accept : Glyphs.Warning; } }
        int TextLeft { get { return Theme.S(46); } }
        public int HeightFor(int width)
        {
            int tw = Math.Max(40, width - TextLeft - Theme.S(14));
            int h = Theme.S(12);
            if (!string.IsNullOrEmpty(Title)) h += TextRenderer.MeasureText(Title, Theme.Bold, new Size(tw, 0), TextFormatFlags.WordBreak).Height + Theme.S(2);
            if (!string.IsNullOrEmpty(Body)) h += TextRenderer.MeasureText(Body, Theme.Body, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            return Math.Max(Theme.S(48), h + Theme.S(12));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g); g.Clear(ParentBack);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, r, Theme.Sf(8), Theme.Mix(ParentBack, Tone, 0.14f));
            Theme.StrokeRound(g, r, Theme.Sf(8), Theme.Mix(ParentBack, Tone, 0.55f), 1);
            Theme.DrawGlyph(g, GlyphText, Theme.GlyphLarge, Tone, new Rectangle(Theme.S(10), Theme.S(10), Theme.S(28), Theme.S(28)));
            int tw = Math.Max(40, Width - TextLeft - Theme.S(14));
            int y = Theme.S(12);
            if (!string.IsNullOrEmpty(Title))
            {
                int h = TextRenderer.MeasureText(Title, Theme.Bold, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
                TextRenderer.DrawText(g, Title, Theme.Bold, new Rectangle(TextLeft, y, tw, h), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                y += h + Theme.S(2);
            }
            if (!string.IsNullOrEmpty(Body))
                TextRenderer.DrawText(g, Body, Theme.Body, new Rectangle(TextLeft, y, tw, Height - y), Theme.Mix(Theme.Text, Theme.Muted, 0.35f), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e);
        }
        class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.Hover; } }
            public override Color MenuItemBorder { get { return Theme.Hover; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Card; } }
        }
    }

    // ------------------------------------------------------------------------------------------------
    public enum DlgKind { Info, Warning, Error, Question, Success }

    public class MessageForm : DarkForm
    {
        public int Result = -1;
        public MessageForm(string title, string text, DlgKind kind, string[] buttons, int defaultIndex, bool dangerFirst)
        {
            Text = Application.ProductName == null || Application.ProductName.Length == 0 ? title : Application.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            KeyPreview = true;
            int w = Theme.S(520), pad = Theme.S(24), textLeft = pad + Theme.S(48);
            int tw = w - textLeft - pad;
            Color tone = kind == DlgKind.Error ? Theme.Danger : kind == DlgKind.Warning ? Theme.Warn : kind == DlgKind.Success ? Theme.Accent : Theme.Info;
            string glyph = kind == DlgKind.Error ? Glyphs.Error : kind == DlgKind.Warning ? Glyphs.Warning : kind == DlgKind.Success ? Glyphs.Accept : kind == DlgKind.Question ? Glyphs.Question : Glyphs.Info;

            var icon = new Label { Text = glyph, Font = Theme.GlyphLarge, ForeColor = tone, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
            icon.SetBounds(pad, pad - Theme.S(2), Theme.S(36), Theme.S(36));
            var lt = new Label { Text = title, Font = Theme.H2, ForeColor = Theme.Text, AutoSize = false };
            int th = TextRenderer.MeasureText(title, Theme.H2, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            lt.SetBounds(textLeft, pad, tw, th);
            var lb = new Label { Text = text, Font = Theme.Body, ForeColor = Theme.Mix(Theme.Text, Theme.Muted, 0.3f), AutoSize = false };
            int bh = TextRenderer.MeasureText(text, Theme.Body, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            lb.SetBounds(textLeft, pad + th + Theme.S(10), tw, bh);
            Controls.Add(icon); Controls.Add(lt); Controls.Add(lb);

            int by = pad + th + Theme.S(10) + bh + Theme.S(26);
            var bar = new Panel { BackColor = Theme.Card };
            bar.SetBounds(0, by, w, Theme.S(64));
            Controls.Add(bar);
            int x = w - pad;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                var b = new FlatButton(buttons[i], null, i == 0 ? (dangerFirst ? BtnKind.Danger : BtnKind.Primary) : BtnKind.Secondary);
                b.BackColor = Theme.Card;
                int bw = Math.Max(Theme.S(96), b.PreferredWidth);
                x -= bw;
                b.SetBounds(x, Theme.S(15), bw, Theme.S(34));
                x -= Theme.S(10);
                int idx = i;
                b.Click += delegate { Result = idx; Close(); };
                bar.Controls.Add(b);
                if (i == defaultIndex) ActiveControl = b;
            }
            ClientSize = new Size(w, by + Theme.S(64));
            int cancelIdx = buttons.Length - 1;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Result = cancelIdx; Close(); } };
        }
    }

    public static class Dialogs
    {
        public static int Show(IWin32Window owner, string title, string text, DlgKind kind, int defaultIndex, bool dangerFirst, params string[] buttons)
        {
            using (var f = new MessageForm(title, text, kind, buttons, defaultIndex, dangerFirst))
            {
                if (owner == null) { f.StartPosition = FormStartPosition.CenterScreen; f.TopMost = true; f.ShowInTaskbar = true; f.ShowDialog(); }
                else { f.StartPosition = FormStartPosition.CenterParent; f.ShowDialog(owner); }
                return f.Result < 0 ? buttons.Length - 1 : f.Result;
            }
        }
        public static bool Ask(IWin32Window owner, string title, string text, string yes, string no) { return Show(owner, title, text, DlgKind.Question, 0, false, yes, no) == 0; }
        public static bool AskDanger(IWin32Window owner, string title, string text, string yes, string no) { return Show(owner, title, text, DlgKind.Warning, 1, true, yes, no) == 0; }
        public static void Info(IWin32Window owner, string title, string text) { Show(owner, title, text, DlgKind.Info, 0, false, "OK"); }
        public static void Success(IWin32Window owner, string title, string text) { Show(owner, title, text, DlgKind.Success, 0, false, "OK"); }
        public static void Warn(IWin32Window owner, string title, string text) { Show(owner, title, text, DlgKind.Warning, 0, false, "OK"); }
        public static void Error(IWin32Window owner, string title, string text) { Show(owner, title, text, DlgKind.Error, 0, false, "OK"); }

        public static string Input(IWin32Window owner, string title, string text, string initial)
        {
            using (var f = new DarkForm())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                int w = Theme.S(520), pad = Theme.S(24);
                var lt = new Label { Text = title, Font = Theme.H2, ForeColor = Theme.Text, AutoSize = false };
                lt.SetBounds(pad, pad, w - 2 * pad, Theme.S(28));
                var lb = new Label { Text = text, ForeColor = Theme.Muted, AutoSize = false };
                int bh = TextRenderer.MeasureText(text, Theme.Body, new Size(w - 2 * pad, 0), TextFormatFlags.WordBreak).Height;
                lb.SetBounds(pad, pad + Theme.S(34), w - 2 * pad, bh);
                var ib = new InputBox(null, null); ib.Text = initial ?? "";
                ib.SetBounds(pad, pad + Theme.S(44) + bh, w - 2 * pad, Theme.S(36));
                int by = pad + Theme.S(44) + bh + Theme.S(60);
                var ok = new FlatButton("OK", null, BtnKind.Primary); var cancel = new FlatButton("Cancel", null, BtnKind.Secondary);
                ok.SetBounds(w - pad - Theme.S(96) * 2 - Theme.S(10), by, Theme.S(96), Theme.S(34));
                cancel.SetBounds(w - pad - Theme.S(96), by, Theme.S(96), Theme.S(34));
                string result = null;
                ok.Click += delegate { result = ib.Text; f.Close(); };
                cancel.Click += delegate { f.Close(); };
                ib.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { result = ib.Text; f.Close(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { f.Close(); } };
                f.Controls.AddRange(new Control[] { lt, lb, ib, ok, cancel });
                f.ClientSize = new Size(w, by + Theme.S(34) + pad);
                f.Shown += delegate { ib.Box.Focus(); ib.Box.SelectAll(); };
                if (owner == null) { f.TopMost = true; f.ShowDialog(); } else f.ShowDialog(owner);
                return result;
            }
        }
    }

    // Modern (Vista+) folder picker, falls back to FolderBrowserDialog
    public static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogCo { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint c, IntPtr specs);
            void SetFileTypeIndex(uint i);
            void GetFileTypeIndex(out uint i);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem si);
            void SetFolder(IShellItem si);
            void GetFolder(out IShellItem si);
            void GetCurrentSelection(out IShellItem si);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem si);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem si);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attrs);
            void Compare(IShellItem si, uint hint, out int order);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        public static string Pick(IWin32Window owner, string title, string initial)
        {
            try
            {
                var dlg = (IFileDialog)new FileOpenDialogCo();
                uint opts; dlg.GetOptions(out opts);
                dlg.SetOptions(opts | 0x20 | 0x40 | 0x800);   // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
                dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                {
                    Guid iid = typeof(IShellItem).GUID; IShellItem si;
                    SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out si);
                    dlg.SetFolder(si);
                }
                if (dlg.Show(owner == null ? IntPtr.Zero : owner.Handle) != 0) return null;
                IShellItem res; dlg.GetResult(out res);
                string path; res.GetDisplayName(0x80058000, out path);
                return path;
            }
            catch (COMException) { return null; }
            catch (Exception)
            {
                using (var f = new FolderBrowserDialog { Description = title, SelectedPath = initial ?? "" })
                    return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
            }
        }
    }
}

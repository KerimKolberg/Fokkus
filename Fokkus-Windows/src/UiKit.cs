using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fokkus
{
    abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
        }

        protected Color ParentBack
        {
            get
            {
                Control p = Parent;
                while (p != null && p.BackColor.A < 255) p = p.Parent;
                return p != null ? p.BackColor : Theme.Bg;
            }
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    class FlatButton : PaintedControl
    {
        public Color BaseColor = Theme.Surface2;
        public Color HoverColor = Theme.Hover;
        public Color TextColor = Theme.Text;
        public Color SelectedColor = Theme.Hover;
        public string Glyph;
        public bool Selected;
        public bool LeftAlign;
        public int Radius = Theme.S(8);
        bool hover, down;

        public FlatButton(string text)
        {
            Text = text;
            Font = Theme.F(10);
            Cursor = Cursors.Hand;
        }

        public void SetAccent(Color c)
        {
            BaseColor = c;
            HoverColor = Theme.Lighten(c, 0.12f);
            TextColor = Theme.OnColor(c);
            Invalidate();
        }

        public void SetPlain()
        {
            BaseColor = Theme.Surface2;
            HoverColor = Theme.Hover;
            TextColor = Theme.Text;
            Invalidate();
        }

        public void SetSelected(bool sel) { if (Selected != sel) { Selected = sel; Invalidate(); } }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);

            Color c = Selected ? SelectedColor : BaseColor;
            if (Enabled)
            {
                if (down) c = Theme.Darken(c.A == 0 ? HoverColor : c, 0.12f);
                else if (hover) c = Selected ? Theme.Lighten(SelectedColor, 0.06f) : HoverColor;
            }
            else if (c.A > 0) c = Theme.Blend(c, back, 0.45f);

            if (c.A > 0) Theme.FillRound(g, c, new RectangleF(0, 0, Width - 1, Height - 1), Radius);

            Color tc = Enabled ? (Selected && BaseColor.A == 0 ? Theme.Text : TextColor) : Theme.Blend(TextColor, back, 0.55f);
            if (!Selected && BaseColor.A == 0 && !hover && Enabled) tc = Theme.Muted;

            Font iconFont = Theme.Icons(Font.SizeInPoints * 0.95f);
            int gap = Theme.S(8);
            Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            Size iconSize = string.IsNullOrEmpty(Glyph) ? Size.Empty : TextRenderer.MeasureText(g, Glyph, iconFont, Size.Empty, TextFormatFlags.NoPadding);
            int total = textSize.Width + iconSize.Width + (textSize.Width > 0 && iconSize.Width > 0 ? gap : 0);
            int x = LeftAlign ? Theme.S(14) : (Width - total) / 2;
            if (iconSize.Width > 0)
            {
                Theme.DrawText(g, Glyph, iconFont, tc, new Rectangle(x, 0, iconSize.Width + 2, Height), Theme.Left);
                x += iconSize.Width + gap;
            }
            if (textSize.Width > 0)
                Theme.DrawText(g, Text, Font, tc, new Rectangle(x, 0, Width - x, Height), Theme.Left);
        }
    }

    class Toggle : PaintedControl
    {
        bool on;
        public string Description;
        public Color OnColor = Theme.Focus;
        public event EventHandler CheckedChanged;

        public Toggle(string text, string description)
        {
            Text = text;
            Description = description;
            Font = Theme.F(10);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public void SetSilently(bool value) { if (on != value) { on = value; Invalidate(); } }

        public static int PreferredHeightFor(string description) { return description == null ? Theme.S(28) : Theme.S(48); }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);
            int tw = Theme.S(40), th = Theme.S(22), ty = Theme.S(3);
            var track = new RectangleF(0, ty, tw, th);
            Color trackColor = on ? OnColor : Theme.Surface2;
            if (!Enabled) trackColor = Theme.Blend(trackColor, back, 0.5f);
            Theme.FillRound(g, trackColor, track, th / 2f);
            if (!on)
                using (var p = new Pen(Theme.Border, 1f))
                using (var path = Theme.RoundRect(new RectangleF(0.5f, ty + 0.5f, tw - 1, th - 1), th / 2f))
                    g.DrawPath(p, path);
            float k = th - Theme.S(6);
            float kx = on ? tw - k - Theme.S(3) : Theme.S(3);
            using (var b = new SolidBrush(on ? Color.White : Theme.Muted))
                g.FillEllipse(b, kx, ty + Theme.S(3), k, k);

            Color tc = Enabled ? Theme.Text : Theme.Muted;
            Theme.DrawText(g, Text, Font, tc, new Rectangle(tw + Theme.S(12), 0, Width - tw - Theme.S(12), Theme.S(28)), Theme.Left);
            if (!string.IsNullOrEmpty(Description))
                Theme.DrawText(g, Description, Theme.F(8.5f), Theme.Muted,
                    new Rectangle(tw + Theme.S(12), Theme.S(26), Width - tw - Theme.S(12), Height - Theme.S(26)),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    class Stepper : PaintedControl
    {
        int val;
        public int Min = 1, Max = 100, Step = 1;
        public string Suffix = "";
        public event EventHandler ValueChanged;
        int hotZone;
        readonly Timer repeat = new Timer();
        int repeatDir;

        public Stepper()
        {
            Font = Theme.F(10);
            Cursor = Cursors.Hand;
            repeat.Tick += (s, e) => { repeat.Interval = 70; Value += repeatDir * Step; };
        }

        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(Min, Math.Min(Max, value));
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SetSilently(int v) { val = Math.Max(Min, Math.Min(Max, v)); Invalidate(); }

        int Zone(Point p) { if (p.X < Height) return -1; if (p.X > Width - Height) return 1; return 0; }

        protected override void OnMouseMove(MouseEventArgs e) { int z = Zone(e.Location); if (z != hotZone) { hotZone = z; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hotZone = 0; repeat.Stop(); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                int z = Zone(e.Location);
                if (z != 0)
                {
                    Value += z * Step;
                    repeatDir = z;
                    repeat.Interval = 420;
                    repeat.Start();
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { repeat.Stop(); base.OnMouseUp(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Enabled) Value += (e.Delta > 0 ? 1 : -1) * Step;
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);
            Theme.FillRound(g, Enabled ? Theme.Surface2 : Theme.Blend(Theme.Surface2, back, 0.5f), new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(8));
            int h = Height;
            if (Enabled && hotZone != 0)
            {
                var r = hotZone < 0 ? new RectangleF(Theme.S(3), Theme.S(3), h - Theme.S(6), h - Theme.S(6)) : new RectangleF(Width - h + Theme.S(3), Theme.S(3), h - Theme.S(6), h - Theme.S(6));
                Theme.FillRound(g, Theme.Hover, r, Theme.S(6));
            }
            Color tc = Enabled ? Theme.Text : Theme.Muted;
            Theme.DrawText(g, "−", Theme.F(13), tc, new Rectangle(0, -Theme.S(1), h, h), Theme.Center);
            Theme.DrawText(g, "+", Theme.F(13), tc, new Rectangle(Width - h, -Theme.S(1), h, h), Theme.Center);
            Theme.DrawText(g, val + Suffix, Theme.FB(10), tc, new Rectangle(h, 0, Width - 2 * h, h), Theme.Center);
        }
    }

    class Segmented : PaintedControl
    {
        readonly string[] options;
        int selected;
        int hot = -1;
        public event EventHandler SelectionChanged;

        public Segmented(params string[] options)
        {
            this.options = options;
            Font = Theme.F(9.5f);
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { if (value == selected) return; selected = value; Invalidate(); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }
        }

        public void SetSilently(int index) { selected = index; Invalidate(); }

        int Hit(Point p) { int w = Width / options.Length; return Math.Max(0, Math.Min(options.Length - 1, p.X / Math.Max(1, w))); }

        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) SelectedIndex = Hit(e.Location); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);
            Theme.FillRound(g, Theme.Surface2, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(9));
            float w = (Width - Theme.S(6)) / (float)options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var r = new RectangleF(Theme.S(3) + i * w, Theme.S(3), w, Height - Theme.S(6) - 1);
                if (i == selected) Theme.FillRound(g, Enabled ? Theme.Hover : Theme.Surface, r, Theme.S(7));
                else if (i == hot && Enabled) Theme.FillRound(g, Theme.Blend(Theme.Surface2, Theme.Hover, 0.5f), r, Theme.S(7));
                Color tc = i == selected ? (Enabled ? Theme.Text : Theme.Muted) : Theme.Muted;
                Theme.DrawText(g, options[i], i == selected ? Theme.FB(9.5f) : Font, tc, Rectangle.Round(r), Theme.Center);
            }
        }
    }

    class DarkList : ListBox
    {
        public Func<object, string> RightText;
        public string EmptyText = "Nothing here yet";

        public DarkList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface2;
            ForeColor = Theme.Text;
            Font = Theme.F(10);
            ItemHeight = Theme.S(30);
            IntegralHeight = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkScrollbars(Handle);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                using (var bb = new SolidBrush(BackColor)) g.FillRectangle(bb, e.Bounds);
                return;
            }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Hover : BackColor)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var a = new SolidBrush(Theme.Focus)) g.FillRectangle(a, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            object item = Items[e.Index];
            string right = RightText != null ? RightText(item) : null;
            int rightW = 0;
            if (!string.IsNullOrEmpty(right))
            {
                rightW = TextRenderer.MeasureText(right, Theme.F(8.5f)).Width + Theme.S(12);
                Theme.DrawText(g, right, Theme.F(8.5f), Theme.Muted, new Rectangle(e.Bounds.Right - rightW, e.Bounds.Y, rightW - Theme.S(10), e.Bounds.Height), Theme.Right);
            }
            Theme.DrawText(g, GetItemText(item), Font, Theme.Text,
                new Rectangle(e.Bounds.X + Theme.S(14), e.Bounds.Y, e.Bounds.Width - Theme.S(20) - rightW, e.Bounds.Height), Theme.Left);
        }

        const int WM_PAINT = 0x000F;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && Items.Count == 0 && !string.IsNullOrEmpty(EmptyText))
            {
                using (var g = CreateGraphics())
                    Theme.DrawText(g, EmptyText, Theme.F(9.5f), Theme.Muted, new Rectangle(0, Theme.S(12), Width, Theme.S(24)), Theme.Center);
            }
        }

        public void SetItems(IEnumerable<object> items)
        {
            object selectedItem = SelectedItem;
            string selText = selectedItem != null ? GetItemText(selectedItem) : null;
            int top = TopIndex;
            BeginUpdate();
            Items.Clear();
            foreach (var it in items) Items.Add(it);
            if (selText != null)
                for (int i = 0; i < Items.Count; i++)
                    if (GetItemText(Items[i]) == selText) { SelectedIndex = i; break; }
            if (top < Items.Count) TopIndex = top;
            EndUpdate();
            Invalidate();
        }
    }

    // List whose rows each have an on/off switch on the right (click the switch to toggle).
    class SwitchList : DarkList
    {
        public Func<object, bool> IsOn = o => true;
        public event Action<object> Toggled;

        static int SwitchW { get { return Theme.S(36); } }

        Rectangle SwitchRect(Rectangle row)
        {
            int h = Theme.S(20);
            return new Rectangle(row.Right - SwitchW - Theme.S(14), row.Y + (row.Height - h) / 2, SwitchW, h);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                using (var bb = new SolidBrush(BackColor)) g.FillRectangle(bb, e.Bounds);
                return;
            }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Hover : BackColor)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var a = new SolidBrush(Theme.Focus)) g.FillRectangle(a, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            object item = Items[e.Index];
            bool on = IsOn(item);
            var sw = SwitchRect(e.Bounds);
            Theme.DrawText(g, GetItemText(item), Font, on ? Theme.Text : Theme.Muted,
                new Rectangle(e.Bounds.X + Theme.S(14), e.Bounds.Y, sw.Left - e.Bounds.X - Theme.S(22), e.Bounds.Height), Theme.Left);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, on ? Theme.Focus : Theme.Blend(Theme.Surface, Theme.Bg, 0.3f), sw, sw.Height / 2f);
            float k = sw.Height - Theme.S(6);
            using (var kb = new SolidBrush(on ? Color.White : Theme.Muted))
                g.FillEllipse(kb, on ? sw.Right - k - Theme.S(3) : sw.Left + Theme.S(3), sw.Top + Theme.S(3), k, k);
            g.SmoothingMode = old;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0 && i < Items.Count && e.X >= Width - SwitchW - Theme.S(26) && Toggled != null)
                Toggled(Items[i]);
            base.OnMouseDown(e);
        }
    }

    // Big on/off button (Timer page): icon, label and an ON/OFF pill.
    class QuickToggle : PaintedControl
    {
        public string Glyph;
        public bool Checked;
        bool hover;

        public QuickToggle(string text, string glyph)
        {
            Text = text;
            Glyph = glyph;
            Font = Theme.F(9.5f);
            Cursor = Cursors.Hand;
        }

        public void SetChecked(bool on) { if (Checked != on) { Checked = on; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);
            Theme.FillRound(g, hover && Enabled ? Theme.Surface2 : Theme.Surface, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10));
            Color ic = !Enabled ? Theme.Blend(Theme.Muted, back, 0.4f) : Checked ? Theme.Focus : Theme.Muted;
            Theme.DrawText(g, Glyph ?? "", Theme.Icons(10), ic, new Rectangle(Theme.S(12), 0, Theme.S(20), Height), Theme.Center);
            int pillW = Theme.S(40), pillH = Theme.S(20);
            var pill = new RectangleF(Width - pillW - Theme.S(10), (Height - pillH) / 2f, pillW, pillH);
            Theme.FillRound(g, Checked ? Theme.Focus : Theme.Surface2, pill, pillH / 2f);
            Theme.DrawText(g, Checked ? "ON" : "OFF", Theme.FB(8), Checked ? Theme.OnColor(Theme.Focus) : Theme.Muted, Rectangle.Round(pill), Theme.Center);
            Theme.DrawText(g, Text, Font, Enabled ? Theme.Text : Theme.Muted,
                new Rectangle(Theme.S(38), 0, (int)pill.Left - Theme.S(44), Height), Theme.Left);
        }
    }

    class TextInput : Panel
    {
        public readonly TextBox Box;
        public event EventHandler Submitted;

        public TextInput(string placeholder)
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface2;
            Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(10), Theme.S(4));
            Box = new TextBox();
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Surface2;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.F(10);
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Box.HandleCreated += (s, e) => Native.SetCueBanner(Box.Handle, placeholder);
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (Submitted != null) Submitted(this, EventArgs.Empty);
                }
            };
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        public static int PreferredHeightNow { get { return Theme.S(36); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Parent != null ? Parent.BackColor : Theme.Bg;
            g.Clear(back);
            Theme.FillRound(g, Theme.Surface2, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(8));
        }

        public void Flash()
        {
            var t = new Timer();
            int n = 0;
            t.Interval = 90;
            t.Tick += (s, e) =>
            {
                n++;
                Box.BackColor = n % 2 == 1 ? Theme.Blend(Theme.Surface2, Theme.Focus, 0.35f) : Theme.Surface2;
                if (n >= 4) { Box.BackColor = Theme.Surface2; t.Stop(); t.Dispose(); }
            };
            t.Start();
        }
    }

    class RingView : PaintedControl
    {
        public float Remaining = 1f;
        public Color RingColor = Theme.Focus;
        public string TimeText = "25:00", PhaseText = "FOCUS", SubText = "";
        public int Dots = 4, DotsFilled;
        public bool Dim;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            float th = Theme.S(12);
            float inset = th / 2 + Theme.S(8);
            var r = new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset);
            using (var p = new Pen(Theme.Surface2, th)) g.DrawEllipse(p, r);
            Color rc = Dim ? Theme.Blend(RingColor, Theme.Surface2, 0.45f) : RingColor;
            if (Remaining > 0.001f)
            {
                float sweep = 360f * Math.Min(1f, Remaining);
                using (var glow = new Pen(Color.FromArgb(Dim ? 18 : 38, RingColor), th + Theme.S(12)))
                {
                    glow.StartCap = LineCap.Round; glow.EndCap = LineCap.Round;
                    g.DrawArc(glow, r, -90, sweep);
                }
                using (var p = new Pen(rc, th))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, r, -90, sweep);
                }
            }
            int cx = Width / 2, cy = Height / 2;
            Theme.DrawText(g, PhaseText, Theme.FB(10.5f), rc, new Rectangle(0, cy - Theme.S(84), Width, Theme.S(24)), Theme.Center);
            Theme.DrawText(g, TimeText, Theme.FL(44), Theme.Text, new Rectangle(0, cy - Theme.S(58), Width, Theme.S(92)), Theme.Center);
            Theme.DrawText(g, SubText, Theme.F(9.5f), Theme.Muted, new Rectangle(0, cy + Theme.S(36), Width, Theme.S(22)), Theme.Center);
            if (Dots > 0)
            {
                int d = Theme.S(8), gap = Theme.S(8);
                int total = Dots * d + (Dots - 1) * gap;
                int x = cx - total / 2, y = cy + Theme.S(70);
                for (int i = 0; i < Dots; i++)
                    using (var b = new SolidBrush(i < DotsFilled ? rc : Theme.Surface2))
                        g.FillEllipse(b, x + i * (d + gap), y, d, d);
            }
        }
    }

    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Hover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Surface; } }
        public override Color CheckBackground { get { return Theme.Surface; } }
        public override Color CheckSelectedBackground { get { return Theme.Hover; } }
        public override Color CheckPressedBackground { get { return Theme.Hover; } }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            Theme.DrawText(e.Graphics, Glyph.Check, Theme.Icons(9), Theme.Focus, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), Theme.Center);
        }
    }
}

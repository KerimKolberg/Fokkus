using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // Quote card shown beside the timer.
    class MotivationCard : PaintedControl
    {
        public string Quote = "";
        public string Caption = "";

        public void Set(string quote, string caption)
        {
            if (quote == Quote && caption == Caption) return;
            Quote = quote;
            Caption = caption;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            Theme.FillRound(g, Theme.Surface, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(14));
            using (var b = new SolidBrush(Theme.Focus)) g.FillRectangle(b, 0, Theme.S(14), Theme.S(3), Height - Theme.S(28));
            Theme.DrawText(g, "“", Theme.FB(26), Theme.Focus, new Rectangle(Theme.S(14), -Theme.S(4), Theme.S(40), Theme.S(52)), TextFormatFlags.Left | TextFormatFlags.Top);
            Theme.DrawText(g, Quote, Theme.FB(12), Theme.Text,
                new Rectangle(Theme.S(18), Theme.S(40), Width - Theme.S(32), Height - Theme.S(68)),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, Caption, Theme.F(8.5f), Theme.Muted, new Rectangle(Theme.S(18), Height - Theme.S(28), Width - Theme.S(32), Theme.S(20)), Theme.Left);
        }
    }

    // Today's subjects with progress, click one to study it.
    class TodayPlanView : PaintedControl
    {
        public List<PlanRow> Rows = new List<PlanRow>();
        public string ActiveId;
        public event Action<Subject> SubjectClicked;
        int hover = -1;

        int RowH { get { return Theme.S(40); } }

        public TodayPlanView() { Cursor = Cursors.Hand; }

        public void SetRows(List<PlanRow> rows, string activeId)
        {
            Rows = rows;
            ActiveId = activeId;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = e.Y / RowH;
            if (h >= Rows.Count) h = -1;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = e.Y / RowH;
            if (e.Button == MouseButtons.Left && i >= 0 && i < Rows.Count && SubjectClicked != null) SubjectClicked(Rows[i].Subject);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            if (Rows.Count == 0)
            {
                Theme.DrawText(g, "No plan for today yet.\nSet one up on the Plan page.", Theme.F(9.5f), Theme.Muted,
                    new Rectangle(0, Theme.S(4), Width, Theme.S(48)), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                return;
            }
            int rh = RowH;
            for (int i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i];
                int y = i * rh;
                if (y + rh > Height + 2) break;
                bool active = r.Subject.Id == ActiveId;
                var rect = new RectangleF(0, y, Width - 1, rh - Theme.S(4));
                if (active) Theme.FillRound(g, Theme.Surface2, rect, Theme.S(8));
                else if (i == hover) Theme.FillRound(g, Theme.Surface, rect, Theme.S(8));
                if (active) using (var b = new SolidBrush(Theme.Focus)) g.FillRectangle(b, 0, y + Theme.S(8), Theme.S(3), rh - Theme.S(20));
                Color c = Theme.SubjectColor(r.Subject);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, Theme.S(12), y + Theme.S(9), Theme.S(9), Theme.S(9));
                bool done = r.Target > 0 && r.Done >= r.Target;
                string right = r.Target > 0 ? r.Done + "/" + r.Target : r.Done + "";
                int rw = Theme.S(54);
                Theme.DrawText(g, r.Subject.Name, active ? Theme.FB(10) : Theme.F(10), Theme.Text, new Rectangle(Theme.S(28), y + Theme.S(2), Width - Theme.S(28) - rw, Theme.S(22)), Theme.Left);
                Theme.DrawText(g, done ? Glyph.Check + "" : right, done ? Theme.Icons(9) : Theme.FB(9.5f), done ? Theme.Focus : Theme.Text,
                    new Rectangle(Width - rw, y + Theme.S(2), rw - Theme.S(10), Theme.S(22)), Theme.Right);
                // progress
                float px = Theme.S(28), pw = Width - Theme.S(40), py = y + Theme.S(26), ph = Theme.S(4);
                Theme.FillRound(g, active ? Theme.Hover : Theme.Surface2, new RectangleF(px, py, pw, ph), ph / 2);
                float frac = r.Target > 0 ? Math.Min(1f, r.Done / (float)r.Target) : (r.Done > 0 ? 1f : 0f);
                if (frac > 0) Theme.FillRound(g, c, new RectangleF(px, py, Math.Max(ph, pw * frac), ph), ph / 2);
            }
        }
    }

    // Editable weekly table: rows = subjects, columns = weekdays, cells = pomodoros.
    class PlanGrid : PaintedControl
    {
        public AppSettings Settings;
        public string SelectedId;
        public event EventHandler Changed;
        public event EventHandler SelectionChanged;
        int hoverRow = -1, hoverCol = -2;

        int NameW { get { return Theme.S(168); } }
        int CellW { get { return Theme.S(56); } }
        int HeaderH { get { return Theme.S(34); } }
        int RowH { get { return Theme.S(38); } }
        int FooterH { get { return Theme.S(48); } }
        int TotalX { get { return NameW + 7 * CellW; } }

        public PlanGrid() { Cursor = Cursors.Hand; }

        public int PreferredGridHeight
        {
            get { return HeaderH + Math.Max(1, Settings == null ? 0 : Settings.Subjects.Count) * RowH + FooterH; }
        }

        void Hit(Point p, out int row, out int col)
        {
            row = -1; col = -2;
            if (Settings == null || p.Y < HeaderH) return;
            int r = (p.Y - HeaderH) / RowH;
            if (r >= Settings.Subjects.Count) return;
            row = r;
            if (p.X < NameW) col = -1;
            else if (p.X < TotalX) col = (p.X - NameW) / CellW;
            else col = 7;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int r, c;
            Hit(e.Location, out r, out c);
            if (r != hoverRow || c != hoverCol) { hoverRow = r; hoverCol = c; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverRow = -1; hoverCol = -2; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int r, c;
            Hit(e.Location, out r, out c);
            if (r < 0) return;
            var s = Settings.Subjects[r];
            if (SelectedId != s.Id) { SelectedId = s.Id; if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }
            if (c >= 0 && c < 7)
            {
                if (e.Button == MouseButtons.Left) Bump(s, c, 1);
                else if (e.Button == MouseButtons.Right) Bump(s, c, -1);
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int r, c;
            Hit(e.Location, out r, out c);
            if (r >= 0 && c >= 0 && c < 7) Bump(Settings.Subjects[r], c, e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        void Bump(Subject s, int day, int d)
        {
            int v = Math.Max(0, Math.Min(16, s.Targets[day] + d));
            if (v == s.Targets[day]) return;
            s.Targets[day] = v;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);
            if (Settings == null) return;
            int today = Planner.DayIndex(DateTime.Today);
            int focus = Settings.FocusMinutes;

            // header
            Theme.DrawText(g, "SUBJECT", Theme.FB(8.5f), Theme.Muted, new Rectangle(Theme.S(12), 0, NameW, HeaderH), Theme.Left);
            for (int d = 0; d < 7; d++)
            {
                var r = new Rectangle(NameW + d * CellW, 0, CellW, HeaderH);
                if (d == today) Theme.FillRound(g, Theme.Surface, new RectangleF(r.X + Theme.S(4), Theme.S(4), CellW - Theme.S(8), HeaderH - Theme.S(8)), Theme.S(8));
                Theme.DrawText(g, Planner.DayShort[d], d == today ? Theme.FB(9) : Theme.F(9), d == today ? Theme.Focus : Theme.Muted, r, Theme.Center);
            }
            Theme.DrawText(g, "WEEK", Theme.FB(8.5f), Theme.Muted, new Rectangle(TotalX, 0, Width - TotalX - Theme.S(8), HeaderH), Theme.Right);

            var subjects = Settings.Subjects;
            if (subjects.Count == 0)
                Theme.DrawText(g, "No subjects yet — add your first one above.", Theme.F(10), Theme.Muted,
                    new Rectangle(Theme.S(12), HeaderH, Width, RowH), Theme.Left);

            for (int i = 0; i < subjects.Count; i++)
            {
                var s = subjects[i];
                int y = HeaderH + i * RowH;
                bool sel = s.Id == SelectedId;
                var rowRect = new RectangleF(0, y + Theme.S(2), Width - 1, RowH - Theme.S(4));
                if (sel) Theme.FillRound(g, Theme.Surface2, rowRect, Theme.S(10));
                else if (i == hoverRow) Theme.FillRound(g, Theme.Surface, rowRect, Theme.S(10));
                Color c = Theme.SubjectColor(s);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, Theme.S(12), y + RowH / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
                Theme.DrawText(g, s.Name, sel ? Theme.FB(10.5f) : Theme.F(10.5f), Theme.Text, new Rectangle(Theme.S(30), y, NameW - Theme.S(36), RowH), Theme.Left);
                for (int d = 0; d < 7; d++)
                {
                    int v = s.Targets[d];
                    var cell = new RectangleF(NameW + d * CellW + Theme.S(6), y + Theme.S(6), CellW - Theme.S(12), RowH - Theme.S(12));
                    bool hot = i == hoverRow && d == hoverCol;
                    if (v > 0) Theme.FillRound(g, Theme.Blend(c, Theme.Bg, hot ? 0.45f : 0.6f), cell, Theme.S(7));
                    else if (hot) Theme.FillRound(g, Theme.Hover, cell, Theme.S(7));
                    Theme.DrawText(g, v > 0 ? v.ToString() : "·", v > 0 ? Theme.FB(10.5f) : Theme.F(10.5f), v > 0 ? Theme.Text : Theme.Muted, Rectangle.Round(cell), Theme.Center);
                }
                int sum = s.Targets.Sum();
                Theme.DrawText(g, sum > 0 ? sum + " · " + Theme.Compact(sum * focus) : "–", Theme.F(9), Theme.Muted,
                    new Rectangle(TotalX, y, Width - TotalX - Theme.S(8), RowH), Theme.Right);
            }

            // footer: per-day totals
            int fy = HeaderH + Math.Max(1, subjects.Count) * RowH + Theme.S(4);
            using (var p = new Pen(Theme.Surface2, 1f)) g.DrawLine(p, 0, fy, Width, fy);
            Theme.DrawText(g, "Per day", Theme.F(9), Theme.Muted, new Rectangle(Theme.S(12), fy, NameW, FooterH - Theme.S(6)), Theme.Left);
            int week = 0;
            for (int d = 0; d < 7; d++)
            {
                int day = subjects.Sum(s => s.Targets[d]);
                week += day;
                var r = new Rectangle(NameW + d * CellW, fy + Theme.S(4), CellW, Theme.S(20));
                Theme.DrawText(g, day.ToString(), Theme.FB(10), day > 0 ? Theme.Text : Theme.Muted, r, Theme.Center);
                Theme.DrawText(g, day > 0 ? Theme.Compact(day * focus) : "", Theme.F(8), Theme.Muted, new Rectangle(r.X, r.Bottom, CellW, Theme.S(18)), Theme.Center);
            }
            Theme.DrawText(g, week + " · " + Theme.Compact(week * focus), Theme.FB(9.5f), Theme.Text,
                new Rectangle(TotalX, fy + Theme.S(4), Width - TotalX - Theme.S(8), Theme.S(20)), Theme.Right);
        }
    }

    // Stacked column chart (minutes per bucket, one colour per subject), with plan markers and hover tooltip.
    class BarChart : PaintedControl
    {
        StatsResult data;
        int hover = -1;
        Point mouse;

        public void SetData(StatsResult r)
        {
            data = r;
            hover = -1;
            Invalidate();
        }

        Rectangle Plot { get { return new Rectangle(Theme.S(46), Theme.S(10), Width - Theme.S(54), Height - Theme.S(38)); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            int h = -1;
            var p = Plot;
            if (data != null && data.Buckets.Count > 0 && e.X >= p.Left && e.X < p.Right && e.Y >= p.Top - Theme.S(6) && e.Y < p.Bottom + Theme.S(20))
                h = (int)((e.X - p.Left) / (p.Width / (float)data.Buckets.Count));
            hover = h;
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        internal void DebugHover(int bucket)
        {
            hover = bucket;
            var p = Plot;
            float slot = p.Width / (float)Math.Max(1, data.Buckets.Count);
            mouse = new Point((int)(p.Left + (bucket + 0.5f) * slot), p.Top + p.Height / 2);
            Invalidate();
        }

        static double NiceStep(double max)
        {
            double[] steps = { 5, 10, 15, 30, 60, 120, 180, 240, 360, 480, 600, 900, 1200, 1800, 2400, 3000, 3600, 6000, 9000, 12000, 18000, 24000, 36000 };
            foreach (var s in steps) if (max / s <= 4) return s;
            return Math.Ceiling(max / 4 / 6000) * 6000;
        }

        static GraphicsPath TopRounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width / 2, r.Height));
            if (rad < 0.5f) { p.AddRectangle(r); return p; }
            p.AddLine(r.Left, r.Bottom, r.Left, r.Top + rad);
            p.AddArc(r.Left, r.Top, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Top, rad * 2, rad * 2, 270, 90);
            p.AddLine(r.Right, r.Top + rad, r.Right, r.Bottom);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = ParentBack;
            g.Clear(back);
            if (data == null) return;
            var p = Plot;
            int n = data.Buckets.Count;
            double max = data.Buckets.Max(b => Math.Max(b.Total, b.Target));
            bool empty = data.TotalMinutes < 0.5 && max <= 0;
            if (max <= 0) max = 60;
            double step = NiceStep(max);
            double top = Math.Ceiling(max / step) * step;
            Func<double, float> Y = v => (float)(p.Bottom - v / top * p.Height);

            // grid + y axis labels (hairline, recessive)
            g.SmoothingMode = SmoothingMode.None;
            for (double v = 0; v <= top + 0.01; v += step)
            {
                float y = Y(v);
                using (var pen = new Pen(v == 0 ? Theme.Baseline : Theme.GridLine, 1f)) g.DrawLine(pen, p.Left, y, p.Right, y);
                Theme.DrawText(g, v == 0 ? "0" : Theme.Compact(v), Theme.F(8), Theme.Muted, new Rectangle(0, (int)y - Theme.S(10), p.Left - Theme.S(8), Theme.S(20)), Theme.Right);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float slot = p.Width / (float)n;
            float barW = Math.Min(Theme.S(24), slot * 0.62f);
            float gap = Math.Max(1f, Theme.S(2));
            float radius = Theme.S(4);

            if (hover >= 0 && hover < n)
                using (var wash = new SolidBrush(Color.FromArgb(70, Theme.Surface2)))
                    g.FillRectangle(wash, p.Left + hover * slot, p.Top, slot, p.Height);

            for (int i = 0; i < n; i++)
            {
                var b = data.Buckets[i];
                float x = p.Left + i * slot + (slot - barW) / 2;
                // stack in series order; top-most visible segment gets the rounded data-end
                int topIdx = -1;
                for (int s = 0; s < b.Minutes.Length; s++) if (b.Minutes[s] > 0.01) topIdx = s;
                double cum = 0;
                bool first = true;
                for (int s = 0; s < b.Minutes.Length; s++)
                {
                    double v = b.Minutes[s];
                    if (v <= 0.01) continue;
                    float y0 = Y(cum), y1 = Y(cum + v);
                    cum += v;
                    if (!first) y0 -= gap;
                    first = false;
                    if (y0 - y1 < 0.75f) continue;
                    var rect = new RectangleF(x, y1, barW, y0 - y1);
                    using (var brush = new SolidBrush(data.Series[s].Color))
                    {
                        if (s == topIdx) using (var path = TopRounded(rect, radius)) g.FillPath(brush, path);
                        else g.FillRectangle(brush, rect);
                    }
                }
                if (b.Target > 0)
                {
                    float ty = Y(b.Target), w = Math.Min(slot * 0.86f, barW + Theme.S(12));
                    using (var pen = new Pen(Theme.Text, Math.Max(1.5f, Theme.S(2))))
                    {
                        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, p.Left + i * slot + (slot - w) / 2, ty, p.Left + i * slot + (slot + w) / 2, ty);
                    }
                }
                if (!string.IsNullOrEmpty(b.Label))
                    Theme.DrawText(g, b.Label, Theme.F(8), i == hover ? Theme.Text : Theme.Muted,
                        new Rectangle((int)(p.Left + i * slot + slot / 2 - Theme.S(30)), p.Bottom + Theme.S(6), Theme.S(60), Theme.S(18)), Theme.Center);
            }

            if (empty)
                Theme.DrawText(g, "No focus time in this period yet.", Theme.F(10), Theme.Muted, new Rectangle(p.Left, p.Top, p.Width, p.Height - Theme.S(20)), Theme.Center);

            if (hover >= 0 && hover < n) DrawTooltip(g, data.Buckets[hover]);
        }

        void DrawTooltip(Graphics g, ChartBucket b)
        {
            var lines = new List<KeyValuePair<Color, string>>();
            for (int s = 0; s < b.Minutes.Length; s++)
                if (b.Minutes[s] >= 0.5) lines.Add(new KeyValuePair<Color, string>(data.Series[s].Color, data.Series[s].Name + "   " + Theme.Compact(b.Minutes[s])));
            string total = "Total " + Theme.Compact(b.Total) + (b.Target > 0 ? "  ·  plan " + Theme.Compact(b.Target) : "");
            int w = Theme.S(210), lh = Theme.S(20);
            foreach (var l in lines) w = Math.Max(w, TextRenderer.MeasureText(l.Value, Theme.F(9)).Width + Theme.S(44));
            w = Math.Max(w, TextRenderer.MeasureText(total, Theme.F(9)).Width + Theme.S(28));
            int h = Theme.S(52) + lines.Count * lh + Theme.S(6);
            int x = mouse.X + Theme.S(16), y = mouse.Y - h / 2;
            if (x + w > Width - 2) x = mouse.X - w - Theme.S(16);
            y = Math.Max(2, Math.Min(Height - h - 2, y));
            var r = new RectangleF(x, y, w, h);
            using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) using (var path = Theme.RoundRect(new RectangleF(x + 2, y + 3, w, h), Theme.S(10))) g.FillPath(shadow, path);
            Theme.FillRound(g, Theme.Surface, r, Theme.S(10));
            using (var pen = new Pen(Theme.Border, 1f)) using (var path = Theme.RoundRect(r, Theme.S(10))) g.DrawPath(pen, path);
            Theme.DrawText(g, b.LongLabel, Theme.FB(9.5f), Theme.Text, new Rectangle(x + Theme.S(12), y + Theme.S(8), w - Theme.S(20), Theme.S(20)), Theme.Left);
            Theme.DrawText(g, total, Theme.F(9), Theme.Muted, new Rectangle(x + Theme.S(12), y + Theme.S(28), w - Theme.S(20), Theme.S(18)), Theme.Left);
            int ly = y + Theme.S(50);
            foreach (var l in lines)
            {
                using (var br = new SolidBrush(l.Key)) g.FillRectangle(br, x + Theme.S(12), ly + Theme.S(5), Theme.S(10), Theme.S(10));
                Theme.DrawText(g, l.Value, Theme.F(9), Theme.Text, new Rectangle(x + Theme.S(30), ly, w - Theme.S(40), lh), Theme.Left);
                ly += lh;
            }
        }
    }

    // Legend row: always shown for 2+ series; plan marker key when the plan is drawn.
    class LegendView : PaintedControl
    {
        List<SeriesInfo> series = new List<SeriesInfo>();
        bool plan;

        public void Set(IEnumerable<SeriesInfo> s, bool showPlan)
        {
            series = s.ToList();
            plan = showPlan;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            int x = Theme.S(46);
            var items = series.Count >= 2 ? series : new List<SeriesInfo>();
            foreach (var s in items)
            {
                using (var b = new SolidBrush(s.Color)) g.FillRectangle(b, x, Height / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
                int w = TextRenderer.MeasureText(s.Name, Theme.F(9)).Width;
                Theme.DrawText(g, s.Name, Theme.F(9), Theme.Text, new Rectangle(x + Theme.S(16), 0, w + 4, Height), Theme.Left);
                x += w + Theme.S(34);
                if (x > Width - Theme.S(80)) break;
            }
            if (plan)
            {
                using (var pen = new Pen(Theme.Text, Math.Max(1.5f, Theme.S(2))))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, x, Height / 2, x + Theme.S(14), Height / 2);
                }
                Theme.DrawText(g, "Plan", Theme.F(9), Theme.Text, new Rectangle(x + Theme.S(20), 0, Theme.S(60), Height), Theme.Left);
            }
        }
    }

    class StatTile : PaintedControl
    {
        public string Label = "", Value = "", Sub = "";

        public void Set(string label, string value, string sub)
        {
            Label = label; Value = value; Sub = sub;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            Theme.FillRound(g, Theme.Surface, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(12));
            Theme.DrawText(g, Label, Theme.F(8.5f), Theme.Muted, new Rectangle(Theme.S(14), Theme.S(8), Width - Theme.S(20), Theme.S(18)), Theme.Left);
            Theme.DrawText(g, Value, Theme.FB(17), Theme.Text, new Rectangle(Theme.S(14), Theme.S(26), Width - Theme.S(20), Theme.S(32)), Theme.Left);
            Theme.DrawText(g, Sub, Theme.F(8), Theme.Muted, new Rectangle(Theme.S(14), Height - Theme.S(22), Width - Theme.S(20), Theme.S(18)), Theme.Left);
        }
    }

    // "By subject" table: swatch, name, share bar, time, pomodoros vs plan.
    class SubjectBreakdown : PaintedControl
    {
        StatsResult data;

        public void SetData(StatsResult r) { data = r; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            if (data == null) return;
            if (data.BySubject.Count == 0)
            {
                Theme.DrawText(g, "Pick a subject on the Timer page before you focus to see your time per subject here.", Theme.F(9), Theme.Muted,
                    new Rectangle(0, 0, Width, Theme.S(24)), Theme.Left);
                return;
            }
            int rh = Theme.S(26), nameW = Theme.S(170), valW = Theme.S(130), planW = Theme.S(110);
            double total = Math.Max(1, data.TotalMinutes);
            double maxM = Math.Max(1, data.BySubject.Max(x => x.Minutes));
            int y = 0;
            foreach (var s in data.BySubject)
            {
                if (y + rh > Height) break;
                using (var b = new SolidBrush(s.Series.Color)) g.FillRectangle(b, 0, y + rh / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
                Theme.DrawText(g, s.Series.Name, Theme.F(9.5f), Theme.Text, new Rectangle(Theme.S(18), y, nameW - Theme.S(22), rh), Theme.Left);
                float bx = nameW, bw = Width - nameW - valW - planW - Theme.S(16), bh = Theme.S(8);
                Theme.FillRound(g, Theme.GridLine, new RectangleF(bx, y + rh / 2 - bh / 2, bw, bh), bh / 2);
                if (s.Minutes > 0) Theme.FillRound(g, s.Series.Color, new RectangleF(bx, y + rh / 2 - bh / 2, Math.Max(bh, (float)(bw * s.Minutes / maxM)), bh), bh / 2);
                Theme.DrawText(g, Theme.Compact(s.Minutes) + "  ·  " + (int)Math.Round(100 * s.Minutes / total) + "%", Theme.F(9), Theme.Text,
                    new Rectangle(Width - valW - planW, y, valW, rh), Theme.Right);
                string plan = s.Planned > 0 ? s.Pomodoros + " / " + s.Planned + " planned" : s.Pomodoros + (s.Pomodoros == 1 ? " pomodoro" : " pomodoros");
                Theme.DrawText(g, plan, Theme.F(9), Theme.Muted, new Rectangle(Width - planW, y, planW, rh), Theme.Right);
                y += rh;
            }
        }
    }
}

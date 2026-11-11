using System;
using System.Drawing;
using System.Windows.Forms;

namespace Fokkus
{
    // "What did you finish?" - pops up after a pomodoro without taking the keyboard; click it to type.
    // Enter saves, Esc skips; it goes away by itself after a few minutes.
    class NotePrompt : Form
    {
        readonly TextInput input;
        readonly Label sub;
        readonly Timer life = new Timer();
        Action<string> save;

        static int S(float v) { return Theme.S(v); }

        public NotePrompt()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            Text = "Fokkus note";
            KeyPreview = true;
            ClientSize = new Size(S(380), S(150));

            var title = new Label { Text = "✎  What did you finish?", Font = Theme.FB(11), ForeColor = Theme.Text, BackColor = Theme.Surface, AutoSize = false, UseMnemonic = false };
            title.SetBounds(S(18), S(12), S(320), S(24));
            Controls.Add(title);
            sub = new Label { Font = Theme.F(9), ForeColor = Theme.Muted, BackColor = Theme.Surface, AutoSize = false, UseMnemonic = false };
            sub.SetBounds(S(18), S(36), S(340), S(20));
            Controls.Add(sub);

            input = new TextInput("e.g. Exercise sheet 3, tasks 1–2");
            input.BackColor = Theme.Surface;
            input.SetBounds(S(18), S(62), S(344), TextInput.PreferredHeightNow);
            input.Box.MaxLength = 200;
            input.Submitted += (s, e) => DoSave();
            Controls.Add(input);

            var skip = new FlatButton("Skip");
            skip.SetBounds(S(184), S(108), S(80), S(30));
            skip.Click += (s, e) => Hide();
            Controls.Add(skip);
            var ok = new FlatButton("Save");
            ok.SetAccent(Theme.Focus);
            ok.SetBounds(S(272), S(108), S(90), S(30));
            ok.Click += (s, e) => DoSave();
            Controls.Add(ok);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
            // Gone after 3 minutes - unless you're typing.
            life.Interval = 180000;
            life.Tick += (s, e) =>
            {
                if (input.Box.Focused && input.Box.Text.Length > 0) return;
                life.Stop();
                Hide();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
            Native.BorderColor(Handle, Theme.Border);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(Theme.Focus)) e.Graphics.FillRectangle(b, 0, 0, S(4), Height);
        }

        // Screenshot self-test: fill without showing.
        internal void Fill(string subtitle) { sub.Text = subtitle; input.Box.Text = ""; }

        // Shows the prompt in the bottom-right corner, above 'avoid' (the break pop-up / mini timer).
        public void Ask(string subtitle, Rectangle avoid, Action<string> onSave)
        {
            save = onSave;
            sub.Text = subtitle;
            input.Box.Text = "";
            Screen screen = avoid != Rectangle.Empty ? Screen.FromRectangle(avoid) : Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            var r = new Rectangle(wa.Right - Width - S(16), wa.Bottom - Height - S(16), Width, Height);
            if (avoid != Rectangle.Empty && r.IntersectsWith(avoid))
            {
                r.Y = avoid.Top - Height - S(10);
                if (r.Y < wa.Top) r.Y = wa.Top + S(16);
            }
            Bounds = r;
            if (!Visible) Show();
            life.Stop();
            life.Start();
        }

        void DoSave()
        {
            string t = input.Box.Text.Trim();
            if (t.Length > 0 && save != null) save(t);
            Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }
}

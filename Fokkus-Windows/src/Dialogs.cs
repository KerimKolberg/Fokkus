using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Fokkus
{
    // Small dark dialogs: a name prompt and the deadline editor.
    static class Dialogs
    {
        static int S(float v) { return Theme.S(v); }

        static Form NewDialog(string title, int w, int h)
        {
            var f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Theme.Bg;
            f.ForeColor = Theme.Text;
            f.Font = Theme.F(10);
            f.ClientSize = new Size(S(w), S(h));
            f.KeyPreview = true;
            f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { f.DialogResult = DialogResult.Cancel; f.Close(); } };
            f.HandleCreated += (s, e) => Native.DarkTitleBar(f.Handle);
            return f;
        }

        static Label AddLabel(Form f, string text, int x, int y, int w, int h, Font font, Color color)
        {
            var l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Theme.Bg;
            l.AutoSize = false;
            l.UseMnemonic = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(S(x), S(y), S(w), S(h));
            f.Controls.Add(l);
            return l;
        }

        static FlatButton AddButton(Form f, string text, int x, int y, int w, bool accent, EventHandler click)
        {
            var b = new FlatButton(text);
            if (accent) b.SetAccent(Theme.Focus);
            b.SetBounds(S(x), S(y), S(w), S(36));
            b.Click += click;
            f.Controls.Add(b);
            return b;
        }

        // Asks for a short name; null when cancelled.
        public static string AskText(IWin32Window owner, string title, string prompt, string initial)
        {
            using (var f = NewDialog(title, 380, 158))
            {
                AddLabel(f, prompt, 20, 14, 340, 26, Theme.F(10), Theme.Text);
                var input = new TextInput("");
                input.SetBounds(S(20), S(46), S(340), TextInput.PreferredHeightNow);
                input.Box.MaxLength = 30;
                input.Box.Text = initial ?? "";
                f.Controls.Add(input);
                string result = null;
                Action ok = () =>
                {
                    string t = input.Box.Text.Trim();
                    if (t.Length == 0) { input.Flash(); return; }
                    result = t;
                    f.DialogResult = DialogResult.OK;
                    f.Close();
                };
                input.Submitted += (s, e) => ok();
                AddButton(f, "Cancel", 172, 104, 90, false, (s, e) => f.Close());
                AddButton(f, "OK", 270, 104, 90, true, (s, e) => ok());
                f.Shown += (s, e) => { input.Box.Focus(); input.Box.SelectAll(); };
                f.ShowDialog(owner);
                return result;
            }
        }

        // Sets, changes or removes a subject's deadline. Returns true when something changed.
        public static bool EditDeadline(IWin32Window owner, Subject subj)
        {
            using (var f = NewDialog("Deadline", 400, 252))
            {
                AddLabel(f, "Deadline for “" + subj.Name + "”", 20, 12, 360, 28, Theme.FB(11), Theme.Text);
                AddLabel(f, "What is it?", 20, 52, 120, 36, Theme.F(10), Theme.Muted);
                var label = new TextInput("Exam");
                label.SetBounds(S(140), S(52), S(240), TextInput.PreferredHeightNow);
                label.Box.MaxLength = 20;
                label.Box.Text = subj.DeadlineLabel ?? "";
                f.Controls.Add(label);

                AddLabel(f, "Date", 20, 100, 120, 36, Theme.F(10), Theme.Muted);
                var date = new DateTimePicker();
                date.Format = DateTimePickerFormat.Long;
                date.Font = Theme.F(10);
                date.MinDate = DateTime.Today.AddYears(-1);
                date.MaxDate = DateTime.Today.AddYears(3);
                var current = subj.DeadlineDate;
                date.Value = current.HasValue && current.Value >= date.MinDate ? current.Value : DateTime.Today.AddDays(14);
                date.SetBounds(S(140), S(102), S(240), S(32));
                f.Controls.Add(date);

                var hint = AddLabel(f, "", 20, 144, 360, 40, Theme.F(8.5f), Theme.Muted);
                hint.TextAlign = ContentAlignment.TopLeft;
                EventHandler updateHint = (s, e) =>
                {
                    int days = (int)(date.Value.Date - DateTime.Today).TotalDays;
                    hint.Text = days < 0 ? "That day has passed." :
                        (days == 0 ? "Today. " : "In " + days + (days == 1 ? " day. " : " days. ")) +
                        "In the last week Fokkus suggests an extra pomodoro a day, in the last 3 days two.";
                };
                date.ValueChanged += updateHint;
                updateHint(null, null);

                bool changed = false;
                Action save = () =>
                {
                    subj.Deadline = date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string l = label.Box.Text.Trim();
                    subj.DeadlineLabel = l.Length > 0 ? l : null;
                    changed = true;
                    f.DialogResult = DialogResult.OK;
                    f.Close();
                };
                label.Submitted += (s, e) => save();
                if (current.HasValue)
                    AddButton(f, "Remove", 20, 196, 100, false, (s, e) =>
                    {
                        subj.Deadline = null;
                        subj.DeadlineLabel = null;
                        changed = true;
                        f.DialogResult = DialogResult.OK;
                        f.Close();
                    });
                AddButton(f, "Cancel", 192, 196, 90, false, (s, e) => f.Close());
                AddButton(f, "Save", 290, 196, 90, true, (s, e) => save());
                f.ShowDialog(owner);
                return changed;
            }
        }
    }
}

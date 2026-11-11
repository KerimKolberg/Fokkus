using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Fokkus
{
    // Sync with the phone, backups / export, and the safety tools.
    class DataPage : Page
    {
        readonly Label syncStatus, syncFolder;
        readonly FlatButton btnSyncNow, btnStopSync;

        public DataPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Sync & data", "Share your statistics and plan with your phone, and back up or export everything.");
            int full = Width - 2 * Pad;

            Section("Sync with your phone", Pad, S(98), full);
            syncStatus = AddLabel("", Theme.FB(9.5f), Theme.Text, Pad, S(122), full, S(22), ContentAlignment.MiddleLeft);
            syncFolder = AddLabel("", Theme.F(9), Theme.Muted, Pad, S(144), full, S(20), ContentAlignment.MiddleLeft);
            Button("Choose folder…", Glyph.Folder, Pad, S(172), S(170), S(36), (s, e) => ChooseFolder());
            btnSyncNow = Button("Sync now", Glyph.Refresh, Pad + S(178), S(172), S(130), S(36), (s, e) => Ctx.Sync.Run());
            btnStopSync = Button("Stop syncing", null, Pad + S(316), S(172), S(130), S(36), (s, e) =>
            {
                Ctx.Settings.SyncFolder = null;
                Save();
            });
            AddLabel("Pick a folder that Google Drive or OneDrive keeps in sync (e.g. G:\\My Drive). Fokkus keeps one file there, " +
                     "“Fokkus-sync.json”. On your phone: Settings → Sync → “Use an existing sync file” and pick that file. " +
                     "Statistics, notes and the study plan are shared (the newest plan wins); blocking lists stay on each device.",
                Theme.F(8.5f), Theme.Muted, Pad, S(216), full, S(54));

            Section("Backup & export", Pad, S(284), full);
            Button("Export statistics (CSV)…", Glyph.Chart, Pad, S(308), S(220), S(36), (s, e) => ExportCsv());
            Button("Back up everything…", Glyph.Folder, Pad + S(228), S(308), S(200), S(36), (s, e) => BackUp());
            Button("Restore a backup…", Glyph.Refresh, Pad + S(436), S(308), S(190), S(36), (s, e) => Restore());
            AddLabel("The CSV opens in Excel or Google Sheets (one row per focus block, with subject and note). A backup holds your settings, " +
                     "plan, statistics, notes and distraction log. Restoring keeps all the statistics you have now and adds the ones from the backup.",
                Theme.F(8.5f), Theme.Muted, Pad, S(352), full, S(40));

            Section("Safety", Pad, S(408), full);
            Button("Restore internet settings", Glyph.Refresh, Pad, S(432), S(250), S(38), (s, e) => Ctx.RestoreInternet());
            Button("Open data folder", Glyph.Folder, Pad + S(258), S(432), S(200), S(38), (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + AppSettings.Dir + "\""); } catch { }
            });
            AddLabel("If websites ever stay blocked after Fokkus closed unexpectedly, click “Restore internet settings” (or just start Fokkus — it repairs itself).",
                Theme.F(8.5f), Theme.Muted, Pad, S(476), full, S(40));

            Ctx.Sync.Changed += () => { if (!IsDisposed) RefreshView(); };
        }

        void ChooseFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose a folder your cloud app keeps in sync (Google Drive, OneDrive…)";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(Ctx.Settings.SyncFolder) && Directory.Exists(Ctx.Settings.SyncFolder)) dlg.SelectedPath = Ctx.Settings.SyncFolder;
                else if (Directory.Exists(@"G:\My Drive")) dlg.SelectedPath = @"G:\My Drive";
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Ctx.Settings.SyncFolder = dlg.SelectedPath;
                Save();
                Ctx.Sync.Run();
            }
        }

        void ExportCsv()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV (Excel, Google Sheets)|*.csv";
                dlg.FileName = "Fokkus-statistics-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    Backup.WriteText(dlg.FileName, Backup.Csv(Ctx.Settings, Ctx.Sessions), true);
                    Ctx.Notify("✅", "Statistics exported", Path.GetFileName(dlg.FileName), Theme.Focus, 5, null, null);
                }
                catch (Exception ex) { Fail("Couldn't export", ex); }
            }
        }

        void BackUp()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Fokkus backup|*.json";
                dlg.FileName = "Fokkus-backup-" + DateTime.Today.ToString("yyyy-MM-dd") + ".json";
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    Backup.WriteText(dlg.FileName, Backup.Create(Ctx.Settings, Ctx.Sessions, Ctx.Distractions), false);
                    Ctx.Notify("✅", "Backup saved", Path.GetFileName(dlg.FileName), Theme.Focus, 5, null, null);
                }
                catch (Exception ex) { Fail("Couldn't save the backup", ex); }
            }
        }

        void Restore()
        {
            if (Ctx.RefuseLoosening(true)) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Fokkus backup|*.json|All files|*.*";
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Backup.Parsed p;
                try { p = Backup.Parse(File.ReadAllText(dlg.FileName)); }
                catch (Exception ex) { Fail("This file can't be restored", ex); return; }
                string what = p.Settings != null
                    ? "Restore your settings and study plan from " + p.Created + "?\n\nStatistics, notes and distractions from the backup are added to the ones you have now — nothing is deleted."
                    : "This backup is from your phone (" + p.Created + "). Its statistics, notes and distractions will be added — your Windows settings stay as they are.";
                if (MessageBox.Show(FindForm(), what, "Fokkus", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                int added = Backup.Restore(p, Ctx.Settings, Ctx.Sessions, Ctx.Distractions);
                Ctx.SettingsChanged();
                Ctx.DataChanged();
                Ctx.Notify("✅", "Backup restored", added + " focus blocks added from the backup.", Theme.Focus, 6, null, null);
            }
        }

        void Fail(string what, Exception ex)
        {
            Log.Error(ex);
            MessageBox.Show(FindForm(), what + ":\n" + ex.Message, "Fokkus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            bool on = !string.IsNullOrEmpty(s.SyncFolder);
            if (!on)
            {
                syncStatus.Text = "Not set up yet";
                syncStatus.ForeColor = Theme.Muted;
                syncFolder.Text = "Choose a cloud folder to start.";
            }
            else
            {
                string st = Ctx.Sync.Status;
                if (st == null)
                    st = s.LastSyncMs > 0 ? "Last synced " + Insights.Local(s.LastSyncMs).ToString("ddd HH:mm") : "Not synced yet";
                syncStatus.Text = st;
                syncStatus.ForeColor = Ctx.Sync.LastFailed ? Theme.Warn : s.LastSyncMs > 0 ? Theme.Focus : Theme.Muted;
                syncFolder.Text = "File: " + SyncService.PathIn(s.SyncFolder);
            }
            btnSyncNow.Enabled = on && !Ctx.Sync.Busy;
            btnStopSync.Enabled = on;
        }
    }
}

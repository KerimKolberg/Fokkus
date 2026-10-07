using System;
using System.Windows.Forms;

namespace Fokkus
{
    public enum Phase { Focus, ShortBreak, LongBreak }

    // Idle = no session. Ready = session on, next phase waits for the user to press Start.
    public enum RunState { Idle, Running, Paused, Ready }

    public class PhaseEventArgs : EventArgs
    {
        public Phase From;
        public Phase To;
        public bool AutoStarted;
        public bool Natural;
        public int FocusMinutes;
    }

    public class FocusEndedArgs : EventArgs
    {
        public DateTime Start;
        public int Seconds;
        public bool Completed;
    }

    public class PomodoroEngine
    {
        readonly Func<AppSettings> settings;
        readonly Timer timer;
        DateTime endUtc;
        TimeSpan remaining;
        TimeSpan total;

        public Phase Phase { get; private set; }
        public RunState State { get; private set; }
        public int CompletedInCycle { get; private set; }

        public event EventHandler Tick;
        public event EventHandler StateChanged;
        public event EventHandler<PhaseEventArgs> PhaseCompleted;
        // A focus phase ended (completed, skipped or stopped) - used for the statistics.
        public event EventHandler<FocusEndedArgs> FocusEnded;

        public PomodoroEngine(Func<AppSettings> settings)
        {
            this.settings = settings;
            Phase = Phase.Focus;
            State = RunState.Idle;
            total = remaining = DurationOf(Phase.Focus);
            timer = new Timer();
            timer.Interval = 250;
            timer.Tick += OnTimer;
            timer.Start();
        }

        public TimeSpan Remaining
        {
            get
            {
                if (State != RunState.Running) return remaining;
                var r = endUtc - DateTime.UtcNow;
                return r < TimeSpan.Zero ? TimeSpan.Zero : r;
            }
        }

        public TimeSpan Total { get { return total; } }

        public double Progress
        {
            get
            {
                if (total.TotalSeconds <= 0) return 0;
                return Math.Max(0, Math.Min(1, 1 - Remaining.TotalSeconds / total.TotalSeconds));
            }
        }

        // Blocking is active for a started focus phase (also while paused, so pausing is no loophole).
        public bool IsLockedIn { get { return Phase == Phase.Focus && (State == RunState.Running || State == RunState.Paused); } }
        public bool InSession { get { return State != RunState.Idle; } }
        public int SessionsPerCycle { get { return Math.Max(1, settings().SessionsBeforeLongBreak); } }

        public TimeSpan DurationOf(Phase p)
        {
            var s = settings();
            int m = p == Phase.Focus ? s.FocusMinutes : p == Phase.ShortBreak ? s.ShortBreakMinutes : s.LongBreakMinutes;
            return TimeSpan.FromMinutes(m);
        }

        public void Start()
        {
            if (State == RunState.Running) return;
            if (State == RunState.Paused) { Resume(); return; }
            if (State == RunState.Idle)
            {
                Phase = Phase.Focus;
                CompletedInCycle = 0;
                total = remaining = DurationOf(Phase.Focus);
            }
            endUtc = DateTime.UtcNow + remaining;
            State = RunState.Running;
            Raise();
        }

        public void Pause()
        {
            if (State != RunState.Running) return;
            remaining = Remaining;
            State = RunState.Paused;
            Raise();
        }

        public void Resume()
        {
            if (State != RunState.Paused) return;
            endUtc = DateTime.UtcNow + remaining;
            State = RunState.Running;
            Raise();
        }

        public void Toggle()
        {
            if (State == RunState.Running) Pause();
            else Start();
        }

        public void Skip()
        {
            if (State == RunState.Idle) return;
            Advance(false, State == RunState.Running);
        }

        public void Stop()
        {
            ReportFocus(false);
            Phase = Phase.Focus;
            State = RunState.Idle;
            CompletedInCycle = 0;
            total = remaining = DurationOf(Phase.Focus);
            Raise();
        }

        // Used by the --screenshots self-test only.
        internal void DebugSetRemaining(TimeSpan r)
        {
            if (State == RunState.Running) endUtc = DateTime.UtcNow + r;
            else remaining = r;
            Raise();
        }

        public void SettingsChanged()
        {
            if (State == RunState.Idle || State == RunState.Ready) total = remaining = DurationOf(Phase);
            Raise();
        }

        void OnTimer(object sender, EventArgs e)
        {
            if (State == RunState.Running && DateTime.UtcNow >= endUtc) Advance(true, null);
            if (Tick != null) Tick(this, EventArgs.Empty);
        }

        void ReportFocus(bool completed)
        {
            if (Phase != Phase.Focus || !(State == RunState.Running || State == RunState.Paused)) return;
            TimeSpan elapsed = completed ? total : total - Remaining;
            int secs = (int)Math.Round(elapsed.TotalSeconds);
            if (!completed && secs < 60) return;
            if (FocusEnded != null)
                FocusEnded(this, new FocusEndedArgs { Start = DateTime.Now - elapsed, Seconds = secs, Completed = completed });
        }

        void Advance(bool natural, bool? forceStart)
        {
            ReportFocus(natural);
            var s = settings();
            Phase from = Phase;
            int focusMinutes = (int)Math.Round(total.TotalMinutes);
            Phase next;
            if (from == Phase.Focus)
            {
                CompletedInCycle++;
                next = CompletedInCycle >= SessionsPerCycle ? Phase.LongBreak : Phase.ShortBreak;
            }
            else
            {
                if (from == Phase.LongBreak) CompletedInCycle = 0;
                next = Phase.Focus;
            }
            bool auto = forceStart.HasValue ? forceStart.Value : (next == Phase.Focus ? s.AutoStartFocus : s.AutoStartBreaks);
            Phase = next;
            total = remaining = DurationOf(next);
            if (auto)
            {
                endUtc = DateTime.UtcNow + total;
                State = RunState.Running;
            }
            else State = RunState.Ready;

            if (PhaseCompleted != null)
                PhaseCompleted(this, new PhaseEventArgs { From = from, To = next, AutoStarted = auto, Natural = natural, FocusMinutes = focusMinutes });
            Raise();
        }

        void Raise()
        {
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }
    }
}

# Fokkus

A Pomodoro study app for **Windows** and **Android**. Plan your week per subject, lock in while you focus (everything except what you allowed is blocked), get small wellbeing nudges in between, and see where your time went.

```
Fokkus-Windows/   – C# source, build.cmd, installer/ (Inno Setup script)
Fokkus-Android/   – Kotlin / Jetpack Compose project (open in Android Studio)
dist/             – build output (not in git):
  Fokkus-Setup-1.5.0.exe – Windows installer
  Fokkus.exe             – Windows app, portable (no install)
  Fokkus.apk             – Android app (sideload)
```

**Downloads:** see [Releases](https://github.com/KerimKolberg/Fokkus/releases), or build them yourself (below).

---

## Features (both apps)

| | Windows | Android |
|---|---|---|
| Pomodoro: focus, short break, long break every N sessions, auto-start options | ✅ | ✅ |
| **Session profiles**: Study, Programming, Exam prep, Just the timer, or your own; one click sets all four blocking switches + strict mode, and a subject can switch its profile on automatically | Timer page + tray menu | Timer tab; subject → Edit |
| **Deadlines**: exam / due date per subject with a countdown; in the last week today's plan suggests +1 pomodoro (+2 in the last 3 days) and the nearest deadline goes first | Plan → Deadline | Plan → edit a subject |
| **One-click rhythms** in Settings: Pomodoro 25 (25/5, long 15 after 4), Pomodoro 45 (45/10, long 20 after 3), Ultradian 90 (90/20, long 30 after 2) | ✅ | ✅ |
| **Weekly study plan**: pomodoros per subject per weekday | grid: click +1, right-click −1 | tap +1, long-press −1 |
| **Subject picker** + today's plan progress beside the timer | ✅ | ✅ |
| Auto-switch to the next subject when today's target is reached | ✅ | ✅ |
| **Motivation lines** beside the timer (they change every minute and react to the last minutes or a finished plan) | card + mini timer | under the ring + bubble |
| **Statistics**: day / week / month / year charts, stacked by subject, with plan markers | hover for details | tap for details |
| Totals, pomodoros vs plan, daily average, streak, time per subject | ✅ | ✅ |
| **Strict mode** (no pause/skip/stop/quit during focus, lists only get stricter) | ✅ | ✅ |
| **Four separate on/off functions**: Allowed websites, Blocked websites, Allowed apps, Blocked apps (see below) | ✅ | ✅ |
| Website filtering | all major browsers | Chrome, Edge, Brave, Firefox, Samsung, Opera, DuckDuckGo, Vivaldi |
| Allowed apps | other windows are minimized (never closed) | everything else is locked |
| Blocked apps | closed | locked |
| **Quick add categories**: TUM, AI, Google Docs, Notes, M365, PDF, Reference, **GitHub & coding** (GitHub sign-in, VS Code, Copilot, pip/npm…), **YouTube (music)**, Essentials; Social, Video, YouTube, Chat, Games… YouTube is allowed by default for study music. | ✅ websites + apps | ✅ websites + blocked apps |
| **Quick switches on the Timer page** for all four functions (e.g. only *Blocked websites* for a programming session) | ✅ also in the tray menu | ✅ |
| Wellbeing tips (water, neck, eyes, posture, breathing) + break ideas | pop-up | notification + bubble |
| Small timer | tray icon + floating mini timer | countdown notification and/or floating bubble |
| **Notes**: "What did you finish?" after each pomodoro; listed and editable in Statistics, shown in the weekly review | small pop-up (Enter saves, Esc skips) | reply in the notification, or the card on the Timer tab |
| **Focus music**: SomaFM streams (lofi beats, chill grooves, ambient, deep space), offline brown noise, or your own stream link; plays only while you focus | music bar on the Timer page + tray menu | music row on the Timer tab |
| **Weekly review**: time vs plan per subject, your best hours and day, notes, distractions you resisted, the next deadline | offered once a week + Statistics → Weekly review | opens once a week + Stats → Weekly review |
| **Smarter breaks**: after a block of 50+ min, or after skipping 2 breaks, the break offers a longer one ("Make it 15 min") | break pop-up | break notification + card |
| **Distraction insights**: which blocked sites and apps you tried to open, how often, and at what hour | Statistics → Distractions | Stats tab |
| **Backup & export**: statistics as CSV (Excel / Google Sheets), full backup, restore (adds statistics, never deletes) | Sync & data page | Settings |
| **Sync between phone and PC** through one file in Google Drive / OneDrive (see below) | Sync & data page | Settings → Sync |
| **Global shortcuts**: Ctrl+Alt+F start/pause, Ctrl+Alt+S skip, Ctrl+Alt+T mini timer, Ctrl+Alt+O open Fokkus | ✅ | – |

### Study plan → timer → statistics
1. **Plan**: add your subjects and set how many pomodoros each gets on each weekday. 1 pomodoro is one focus block (25 min by default).
2. **Timer**: pick what you're studying, or just press *Lock in* and Fokkus picks the next subject from today's plan. When a subject's target for today is done, it moves on to the next one.
3. **Statistics**: every focus block is logged with its subject, including the focused part of blocks you skip or stop. The charts stack time per subject. The white marks show what you planned, counted from the day you created the plan.

Subject colors come from a color-blind-checked palette and stay fixed per subject, so they never change between screens.

### Allowed / blocked: four switches
Websites and apps each have two lists, and each list has its own switch. They only apply during focus; breaks are always free.

| Allowed | Blocked | During focus |
|---|---|---|
| ON | OFF | only what's on the allowed list works |
| OFF | ON | everything works except what's on the blocked list |
| ON | ON | only allowed things work, and blocked ones never do, even if they're on both lists |
| OFF | OFF | nothing is filtered |

Example: for a programming session where you only want to keep social media & co. away, switch **Blocked websites** on and **Allowed websites** off. In strict mode a switch can be turned on during focus, but not off.

### Sync between phone and PC
Both apps share one file, `Fokkus-sync.json`, in a folder your cloud app keeps in sync (Google Drive, OneDrive…).
1. **Windows:** *Sync & data → Choose folder…* and pick e.g. `G:\My Drive`. Fokkus creates the file there.
2. **Android:** *Settings → Sync → Use an existing sync file…* and pick that `Fokkus-sync.json` (in the Drive app's section of the file picker).

What's shared: statistics and notes from both devices are joined (nothing is ever deleted), and the study plan with deadlines (the newest version wins). The very first time, two plans you made separately are joined by subject name. Blocking lists and settings stay on each device. Windows syncs at start, every 10 minutes and after each focus block; Android when you open it and after each focus block.

---

## Windows

**Install:** run `Fokkus-Setup-1.5.0.exe`. It installs for your user only (no admin rights) into `%LOCALAPPDATA%\Programs\Fokkus` and adds:
- a desktop shortcut and a Start menu entry
- an entry in *Settings → Apps*, from where you can uninstall. The uninstaller asks whether to keep your plan and statistics.

Prefer no install? `Fokkus.exe` also runs on its own.

Windows may warn about an unknown publisher the first time, because the installer isn't code-signed. Choose *More info → Run anyway*.

Fokkus lives in the system tray and shows a small timer you can drag anywhere.

- **Tray icon**: a ring that shrinks as time passes (teal = focus, amber = short break, violet = long break). Left-click opens Fokkus; right-click opens the menu.
- **Mini timer**: drag to move, double-click to open, hover for ▶/⏸, skip and open. It shows the subject, and for a few seconds now and then a motivation line. It repairs itself if it gets lost: after *Show desktop*, a virtual-desktop switch, sleep, the lock screen or a monitor change it comes back on its own.
- **Space** starts or pauses the timer on the Timer page.
- **Minimize**: by default the window goes to the tray. Turn off *Settings → Minimizing hides it in the tray* to minimize to the taskbar like any other window.
- **Shortcuts** from any app: Ctrl+Alt+F start/pause, Ctrl+Alt+S skip, Ctrl+Alt+T show/hide the mini timer, Ctrl+Alt+O open Fokkus. The letters avoid AltGr characters on a German keyboard. Turn them off in *Settings → Global shortcuts*; it also tells you if another app already uses one.
- **Focus music** uses Windows Media Player's engine (part of Windows). While it plays, its stream is let through the website filter.

### How website blocking works
During focus, Fokkus runs a tiny filter on `127.0.0.1` and points the Windows proxy setting at it. Chrome, Edge, Brave and Opera use that setting, and so does Firefox with "Use system proxy settings". Allowed sites skip the filter entirely. Blocked `https://` pages show a connection error, plus a Fokkus pop-up. If a site you need breaks, allow it from **Websites → Recently blocked**. Nothing is decrypted or logged; only host names are checked.

**Safety net:** if the app is killed mid-focus, your proxy setting is restored at the next login or app start. You can also use **Sync & data → Restore internet settings**, or run:
```bash
Fokkus.exe --restore-internet
```

### Build
`Fokkus-Windows\build.cmd` uses the C# compiler built into Windows (.NET Framework 4.8), so no SDK is needed. The output is `Fokkus-Windows\bin\Fokkus.exe`.

`Fokkus-Windows\build-installer.cmd` builds the app and then the installer into `dist\`. It needs [Inno Setup 6](https://jrsoftware.org/isinfo.php), which you can get with:
```bash
winget install JRSoftware.InnoSetup
```
`Fokkus.exe --selftest out.txt` runs 79 built-in tests (all four filter combinations, the live proxy, plan and statistics math, sync between two devices, notes, breaks, review, backup and export).

Data lives in `%APPDATA%\Fokkus\`:
- `settings.json`
- `sessions.json`, the focus log behind the statistics (with your notes)
- `distractions.json`, the blocked attempts behind the distraction insights
- `brown-noise.wav`, generated the first time you play brown noise

Your settings from the old kk-Lock-in folder are carried over automatically.

---

## Android

### Install
1. Open `dist/Fokkus.apk` on the phone, or with USB debugging run `adb install -r dist/Fokkus.apk`. The package is `com.kk.fokkus`. Builds from before the rename used `com.kk.lockin`; if you still have that one, uninstall it.
2. The **Finish setup** card on the Timer tab walks you through the permissions:
   - Notifications
   - Accessibility → Fokkus (for app and website blocking). If it's greyed out: *App info → ⋮ → Allow restricted settings*.
   - Display over other apps (for the bubble)
   - Run in background (recommended)

### Tabs
- **Timer**: ring, subject chips, motivation line, profile, quick switches, focus music, today's plan. During a break: "What did you finish?" and, if earned, a longer break.
- **Plan**: the weekly table, one card per subject (edit it for the deadline and its profile).
- **Stats**: charts, the per-subject breakdown, your notes, distractions, and the weekly review.
- **Blocking**: Apps and Websites, each with *Allowed* and *Blocked* switches and lists.
- **Settings**: durations and rhythms, strict mode, smarter breaks, notes, timer display, tips, sync, backup & export.

### Build
Open `Fokkus-Android/` in Android Studio, or:
```bash
cd Fokkus-Android && ./gradlew assembleRelease
```
Built with AGP 9.4, Kotlin 2.4 and Compose BOM 2026.09. compileSdk 37, minSdk 26 (Android 8.0+).

---

## Honest limits
Fokkus is a focus tool, not parental control. You could get around it on purpose: turn off the accessibility service, kill the app, or edit the proxy setting. Strict mode removes the easy escape routes. On Windows, while *Allowed websites* is on, other programs that use the system proxy are also filtered during focus.

Privacy: everything stays on your devices. Data only leaves them if you set up sync (to your own cloud file), and focus music streams from SomaFM while it plays. YouTube links can't be played as focus music.

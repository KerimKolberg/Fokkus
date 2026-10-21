# Fokkus

A Pomodoro study app for **Windows** and **Android**. Plan your week per subject, lock in while you focus (everything except what you allowed is blocked), get small wellbeing nudges in between, and see where your time went.

```
Fokkus-Windows/   – C# source, build.cmd, installer/ (Inno Setup script)
Fokkus-Android/   – Kotlin / Jetpack Compose project (open in Android Studio)
dist/             – build output (not in git):
  Fokkus-Setup-1.2.0.exe – Windows installer
  Fokkus.exe             – Windows app, portable (no install)
  Fokkus.apk             – Android app (sideload)
```

**Downloads:** see [Releases](https://github.com/KerimKolberg/Fokkus/releases), or build them yourself (below).

---

## Features (both apps)

| | Windows | Android |
|---|---|---|
| Pomodoro: focus, short break, long break every N sessions, auto-start options | ✅ | ✅ |
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
| **Quick add categories**: TUM, AI, Google Docs, Notes, M365, PDF, Reference, **GitHub & coding** (GitHub sign-in, VS Code, Copilot, pip/npm…), Essentials; Social, Video, Chat, Games… | ✅ websites + apps | ✅ websites + blocked apps |
| **Quick switches on the Timer page** for all four functions (e.g. only *Blocked websites* for a programming session) | ✅ also in the tray menu | ✅ |
| Wellbeing tips (water, neck, eyes, posture, breathing) + break ideas | pop-up | notification + bubble |
| Small timer | tray icon + floating mini timer | countdown notification and/or floating bubble |

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

Example: for a programming session where you only want to keep YouTube & co. away, switch **Blocked websites** on and **Allowed websites** off. In strict mode a switch can be turned on during focus, but not off.

---

## Windows

**Install:** run `Fokkus-Setup-1.2.0.exe`. It installs for your user only (no admin rights) into `%LOCALAPPDATA%\Programs\Fokkus` and adds:
- a desktop shortcut and a Start menu entry
- an entry in *Settings → Apps*, from where you can uninstall. The uninstaller asks whether to keep your plan and statistics.

Prefer no install? `Fokkus.exe` also runs on its own.

Windows may warn about an unknown publisher the first time, because the installer isn't code-signed. Choose *More info → Run anyway*.

Fokkus lives in the system tray and shows a small timer you can drag anywhere.

- **Tray icon**: a ring that shrinks as time passes (teal = focus, amber = short break, violet = long break). Left-click opens Fokkus; right-click opens the menu.
- **Mini timer**: drag to move, double-click to open, hover for ▶/⏸, skip and open. It shows the subject, and for a few seconds now and then a motivation line.
- **Space** starts or pauses the timer on the Timer page.

### How website blocking works
During focus, Fokkus runs a tiny filter on `127.0.0.1` and points the Windows proxy setting at it. Chrome, Edge, Brave and Opera use that setting, and so does Firefox with "Use system proxy settings". Allowed sites skip the filter entirely. Blocked `https://` pages show a connection error, plus a Fokkus pop-up. If a site you need breaks, allow it from **Websites → Recently blocked**. Nothing is decrypted or logged; only host names are checked.

**Safety net:** if the app is killed mid-focus, your proxy setting is restored at the next login or app start. You can also use **Settings → Restore internet settings**, or run:
```bash
Fokkus.exe --restore-internet
```

### Build
`Fokkus-Windows\build.cmd` uses the C# compiler built into Windows (.NET Framework 4.8), so no SDK is needed. The output is `Fokkus-Windows\bin\Fokkus.exe`.

`Fokkus-Windows\build-installer.cmd` builds the app and then the installer into `dist\`. It needs [Inno Setup 6](https://jrsoftware.org/isinfo.php), which you can get with:
```bash
winget install JRSoftware.InnoSetup
```
`Fokkus.exe --selftest out.txt` runs 45 built-in tests (all four filter combinations, the live proxy, plan and statistics math).

Data lives in `%APPDATA%\Fokkus\`:
- `settings.json`
- `sessions.json`, the focus log behind the statistics

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
- **Timer**: ring, subject chips, motivation line, today's plan.
- **Plan**: the weekly table, one card per subject.
- **Stats**: charts and the per-subject breakdown.
- **Blocking**: Apps and Websites, each with *Allowed* and *Blocked* switches and lists.
- **Settings**: durations, strict mode, timer display (notification / floating / both), tips.

### Build
Open `Fokkus-Android/` in Android Studio, or:
```bash
cd Fokkus-Android && ./gradlew assembleRelease
```
Built with AGP 9.4, Kotlin 2.4 and Compose BOM 2026.09. compileSdk 37, minSdk 26 (Android 8.0+).

---

## Honest limits
Fokkus is a focus tool, not parental control. You could get around it on purpose: turn off the accessibility service, kill the app, or edit the proxy setting. Strict mode removes the easy escape routes. On Windows, while *Allowed websites* is on, other programs that use the system proxy are also filtered during focus.

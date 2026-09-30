<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/logo-light.png">
  <img alt="DockHub" src="docs/images/logo-light.png" width="300">
</picture>

### A glassy, widget-rich dock that replaces the Windows taskbar.

Start, search, running apps, the system tray and live widgets, all in one floating dock.<br>
No admin rights. Your original taskbar always comes back.

[![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&logo=windows11&logoColor=white)](#requirements)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-4CC2FF?style=flat-square)](#tech-stack)
[![Latest release](https://img.shields.io/github/v/release/sametgurtuna/DockHub?style=flat-square&color=4CC2FF&label=release)](https://github.com/sametgurtuna/DockHub/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/sametgurtuna/DockHub/ci.yml?branch=master&style=flat-square&label=build)](https://github.com/sametgurtuna/DockHub/actions/workflows/ci.yml)

[**Download**](https://github.com/sametgurtuna/DockHub/releases/latest) &nbsp;·&nbsp;
[**Live demo**](https://sametgurtuna.github.io/dockhub-website/) &nbsp;·&nbsp;
[Features](#features) &nbsp;·&nbsp;
[Build from source](#build-from-source) &nbsp;·&nbsp;
[FAQ](#faq)

<br>

<img src="docs/images/dock-overview.jpg" alt="DockHub running on a Windows 11 desktop with pinned apps, a sticky note, headset battery, media controls, clock, AI usage rings, an app folder and weather" width="100%">

</div>

<br>

> [!NOTE]
> Every screenshot below is a real capture of DockHub running on Windows 11. To try it without installing, open the [interactive web demo](https://sametgurtuna.github.io/dockhub-website/).

## Contents

- [Highlights](#highlights)
- [Screenshots](#screenshots)
- [Installation](#installation)
- [Features](#features)
- [Widgets](#widgets)
- [How the taskbar replacement works](#how-the-taskbar-replacement-works)
- [Build from source](#build-from-source)
- [Command line](#command-line)
- [Configuration and data](#configuration-and-data)
- [Permissions and privacy](#permissions-and-privacy)
- [Project structure](#project-structure)
- [Writing a widget](#writing-a-widget)
- [Performance](#performance)
- [Known limitations](#known-limitations)
- [FAQ](#faq)
- [Contributing](#contributing)
- [License and credits](#license-and-credits)

## Highlights

- **Replaces the taskbar, keeps Windows intact.** The Start button opens the real Windows Start menu, and the Windows key, search, notification center and quick settings work exactly as before.
- **28 widgets, many layouts each.** Clocks, timers, reminders, your calendar, a to-do list with Todoist, clipboard history, a Downloads stack, exchange rates, sticky notes, screenshots, now playing, audio, brightness, Wi-Fi and Bluetooth, CPU, GPU and network monitors, AI usage (Claude Code, Codex, Gemini CLI) and weather. Add the same widget as many times as you like; every copy keeps its own settings.
- **Every battery at a glance.** Bluetooth headsets, mice, keyboards and controllers, Xbox and PlayStation controllers, and Logitech, Razer, HyperX, SteelSeries and LAMZU receivers, with a warning before one runs flat.
- **Build your own widgets.** Web widgets in HTML and JavaScript install from a `.dockwidget` file, a link or the community list in the gallery.
- **Live app buttons.** Hover for a real window thumbnail, right-click for a Jump List, watch badge counts and progress, and drag apps together into a folder.
- **Fluent to the core.** Blurred glass, Acrylic or solid backgrounds, light and dark themes, and your Windows accent color. Popups, folders and widgets animate with macOS-inspired genie, zoom and fan effects, all at your display's refresh rate.
- **Any edge, any shape.** Bottom, top, left or right; floating or attached; small, medium or large. On vertical docks, widgets collapse into compact tiles.
- **Safe by design.** No admin rights. When DockHub exits, crashes, or the session ends, the Windows taskbar and its tray icons come back. After a crash Windows starts DockHub again, and the next start tells you what happened.
- **Starts with Windows.** The installer enables autostart by default, and you can turn it off at any time.
- **In your language.** English, Turkish, German and Spanish.
- **Keyboard first.** Win+1…9 open and switch dock apps like on the Windows taskbar, a Spotlight-style quick launcher (Win+Alt+Space) finds apps, settings and commands, and every DockHub action can get its own global shortcut.

## Screenshots

<table>
  <tr>
    <td width="50%"><img src="docs/images/groups-folder.jpg" alt="An app folder named AI opened from the dock, showing Claude, Codex and Antigravity with a color palette"><br><sub><b>Folders.</b> Drag items together, then click to fan them out. Rename and recolor from the popup.</sub></td>
    <td width="50%"><img src="docs/images/window-preview.jpg" alt="Live thumbnail preview of a File Explorer window above the dock"><br><sub><b>Live window previews.</b> Hover a running app for a real thumbnail with a close button.</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/jump-list.jpg" alt="Jump list for File Explorer with recent folders and app actions"><br><sub><b>Jump Lists.</b> Right-click an app for its recent items and tasks.</sub></td>
    <td width="50%"><img src="docs/images/widget-panel.jpg" alt="Clock widget panel showing seconds, the date and the time zone"><br><sub><b>Widget panels.</b> Click any widget for the full view.</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/start-menu.jpg" alt="The Windows 11 Start menu opened from the dock's Start button"><br><sub><b>Start menu.</b> The real Windows Start menu, opened from the dock.</sub></td>
    <td width="50%"><img src="docs/images/light-vertical.jpg" alt="Light theme with a vertical dock on the left edge showing compact widget tiles"><br><sub><b>Light theme, vertical dock.</b> Widgets become compact tiles.</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/widget-gallery.jpg" alt="Widget gallery in the settings window with live previews and add buttons"><br><sub><b>Widget gallery.</b> Live previews; press + and the widget grows into the dock.</sub></td>
    <td width="50%"><img src="docs/images/settings.jpg" alt="DockHub settings window showing appearance options"><br><sub><b>Settings.</b> A Mica window for every option.</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/explorer-pin.jpg" alt="File Explorer context menu with the Pin to DockHub command"><br><sub><b>Pin from File Explorer.</b> Right-click any .exe or shortcut.</sub></td>
    <td width="50%"><img src="docs/images/dock-menu.jpg" alt="Dock context menu with add widget, pin application, create group and position commands"><br><sub><b>Dock menu.</b> Right-click empty space for widgets, groups and position.</sub></td>
  </tr>
</table>

<p align="center">
  <img src="docs/images/widgets-grid.jpg" alt="Widget gallery previews for network speed, status rings, recycle bin, device batteries, weather and AI usage" width="100%">
</p>

## Installation

### Installer (recommended)

1. Download **`DockHub-Setup-<version>-x64.exe`** from the [latest release](https://github.com/sametgurtuna/DockHub/releases/latest).
2. Run it. DockHub installs for the current user, so no administrator prompt appears.
3. Keep **"Start DockHub automatically when I sign in to Windows"** checked (the default) to have the dock ready every time Windows starts.

The installer is self-contained. It ships the .NET runtime, so there is nothing else to install. It is available in English, Turkish, German and Spanish.

### winget

Once the package is published in the Windows Package Manager repository:

```powershell
winget install SametGurtuna.DockHub
```

**Uninstalling** from *Settings › Apps* closes DockHub, restores the Windows taskbar, and removes the autostart entry, the File Explorer menu command and the `.dockwidget` file type. Your settings in `%AppData%\DockHub` are kept.

### Requirements

| | |
|---|---|
| Operating system | Windows 10 version 1809 (build 17763) or later, x64 |
| Recommended | Windows 11 22H2 or later (rounded corners, Mica settings window) |
| Runtime | Bundled with the installer. Running a framework-dependent build requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

## Features

### Taskbar replacement

- **Start button** opens the Windows Start menu (`IImmersiveLauncher`); a second click closes it. Right-click opens the Win+X menu.
- **The Windows key** works as usual. Start, search, the notification center and quick settings open in Windows' own panels.
- **Search** and **Task View** buttons, each of which can be toggled. The search button can open Windows Search or DockHub's quick launcher (*Settings › Taskbar › Search button opens*).
- **Quick launcher** (Win+Alt+Space, or the search button): type to find apps, open windows, DockHub and Windows settings pages, dock commands (auto-hide, mute, lock, new virtual desktop, switch profile...), recent files, quick math (`200*15%`, `sqrt(2)`) and a web search. Enter opens the first result, arrow keys move, Esc closes.
- **Virtual desktops.** A number next to Task View shows which desktop you are on; scroll it to switch desktops, right-click to add or close one. *Apps from all desktops* lists windows of every desktop on the dock, and clicking one switches to its desktop.
- **Running apps**
  - Pinned apps are matched with their open windows, and windows of the same app are grouped into one button.
  - Other open apps are listed after a separator.
  - Active, attention-requesting and progress states (for example, download bars) are shown, along with taskbar-style badge counts.
  - Hovering a button shows a live thumbnail preview of its window(s), like the Windows taskbar. Resting the pointer on a thumbnail peeks at that window (Aero Peek), scrolling over the preview cycles through the app's windows, and apps that play media get a play/pause button in the preview.
  - Starting an app bounces its icon until the first window appears.
  - Right-click shows the window list, the app's Jump List (recent files and tasks, when the app provides one), plus *Run as administrator*, *Open file location*, *Pin/Unpin* and *Close all windows*.
  - Clicking brings a window to the front, minimizes it, or cycles through the app's windows. Shift+click or middle-click opens a new window.
  - Pin a running app by dragging it into the dock or with the *Pin to DockHub* command.
  - **One button per window:** set *Settings › Taskbar › Combine app buttons* to *Never*, like Windows' "Combine taskbar buttons: Never". Every window then gets a button of its own with its icon and title (just the icon on a side dock), and an app's windows stay side by side: a pinned app's button becomes its first window's, and its other windows follow right after it. Clicking a window's button brings that window forward or minimizes it, middle-click closes it, and its preview shows that window only. The icons don't grow under the pointer in this mode.
  - **Pin from File Explorer:** right-click an `.exe` or shortcut and choose *Pin to DockHub*. On Windows 11 the command appears under *Show more options* (or Shift+right-click), because the new compact menu only lists commands from packaged apps.
  - **Groups (folders):** drag an app or widget onto another to create a folder. Folders can be renamed, given a custom accent color, and open with a staggered fan animation.
- **System tray.** App icons live in the dock and receive clicks, right-clicks and hover. Hidden icons sit in the overflow menu, and you choose which icons are always visible. Your Windows tray preferences are imported on first launch.
- **Network, volume and battery icons.** DockHub draws its own status icons next to the tray (Windows 11 keeps its real ones inside Explorer). Scroll the volume icon to change the volume, middle-click to mute, right-click to switch the output device; the battery icon only appears on devices with a battery.
- **Input language.** With more than one keyboard language installed, a short code (TUR, ENG...) sits next to the tray. Click or scroll it to switch, right-click for the list and language settings.
- **Microphone.** A microphone icon appears while an app records (like Windows), or always if you prefer; click it to mute or unmute, and give it a global shortcut. It lists which apps use the microphone.
- **Notifications and Do Not Disturb.** The clock shows how many notifications wait in the notification center, and a moon while Do Not Disturb or Focus is on.
- **Win+1…9 and Win+0** open, switch to or minimize the dock's first ten apps. Add Shift for a new window, Ctrl+Shift to run as administrator, Alt for the jump list. Hold Win to see the numbers on the dock. With one button per window, the numbers follow the buttons, so each window has its own number.

  <img src="docs/images/tray-overflow.jpg" alt="Tray overflow flyout above the dock showing hidden tray icons" width="46%">
- **Clock.** Clicking it opens the notification center. Its context menu offers quick settings, date and time settings, and seconds and date options. A thin strip at the far end shows the desktop.
- **Reserved screen space.** Like the Windows taskbar, the dock keeps its band of the screen to itself: maximized windows stop at the dock instead of sliding under it, at any display scaling and on every display that has a dock.

### The dock

- **Look:** blurred glass, Acrylic, transparent or solid background with adjustable tint; dark, light or system theme; Windows accent color.
- **Size:** small (48 DIP, the same height as the Windows taskbar), medium (56 DIP) or large (66 DIP).
- **Shape:** *floating* (inset from the screen edges with rounded corners) or *attached* (a classic taskbar).
- **Width and alignment:** full width or fit to content; items centered or aligned to the start.
- **Position:** bottom, top, left or right. On vertical docks, widgets become compact tiles showing an icon or ring with a short value. Clicking a tile opens the full widget in a side panel, and timers, the water tracker and reminders run their main action directly.
- **Scrolling:** when items don't fit, the edge where more items wait fades out. The mouse wheel or touchpad scrolls smoothly and stops where an item begins, so nothing at the leading edge is cut in half; an item you drag to a faded edge scrolls the dock that way.
- **Auto-hide:** the dock slides off the edge and returns when the pointer reaches it. With *Only hide when a window overlaps*, it stays up over the desktop and small windows and slides away only while the active window covers it.
- **Profiles:** save your setup as a profile (Work, Gaming, Laptop...) with its own items and look, switch from the dock menu or a shortcut, or let a profile switch in by itself: when a given number of displays is connected, while an app runs (for example `steam` or `cs2`), or during set hours (weekdays only if you like). When the app closes or the hours end, DockHub goes back to the profile you had. Rules live in *Settings › Profiles*.
- **Open app indicator:** a line that widens for the active app and several windows, one dot per window, or none.
- **Hide in full screen:** the dock steps aside for games, videos and F11 mode.
- **Top bar:** a second, thin bar on the main display for widgets and a clock, like the menu bar on a Mac, for example widgets on top and apps below. Turn it on in *Settings › Appearance › Top bar* (or on the Overview), then move widgets there from their right-click menu (*Show on › Top bar*), by dragging them over in edit mode or from the gallery, or in *Settings › Dock items*. It has its own edge (never the dock's), size, shape, backdrop, auto-hide and clock, reserves its own screen space, and steps aside in full screen like the dock. Turning it off brings its widgets back to the dock. Profiles keep the bar; presets and theme files leave it alone.
- **Multi-monitor and DPI:** choose the monitor for the dock; per-monitor DPI (PerMonitorV2) is supported. With *Show on all displays*, every other display gets a dock too, with its own size; move a widget to one of them from its right-click menu (*Show on*) or in *Settings › Dock items*.
- **Drag and drop:** reorder apps, widgets and separators on the dock. Dropping an `.exe` or `.lnk` from File Explorer pins it.
- **Edit mode:** right-click an empty area of the dock (or any widget) and choose **Edit dock**, press the *Edit the dock* shortcut, or use *Settings › Dock items › Edit on the dock*. The items wiggle; drag them to reorder, press **×** to remove one, and drag the handle on a widget's edge to switch it to a narrower or wider layout. The **+** tile at the end adds a widget, a separator or an app. Press **Done**, Esc or Enter, or click outside DockHub (or on another dock) to finish. Everything you changed is one step that *Undo* reverts; leaving without a change leaves no step behind. With the keyboard, the arrow keys move the focus, Ctrl+arrow keys move the item, Delete removes it and + or - resize it. Start, search, the tray and the clock stay put, and with reduced motion the items don't wiggle.
- **Smooth motion:** scrolling, auto-hide and flyouts use frame-synchronized transitions at your display's refresh rate, including above 60 Hz. Hover highlights fade in, app icons grow slightly under the pointer, new items grow into place, and widget/folder popups open with macOS-style genie, zoom and shrink animations.
- **Accessibility:** *Settings › Appearance › Animations* reduces motion to short fades (or turns it off), following Windows' animation effects by default. Windows contrast themes are picked up automatically. Screen readers get names and states for every dock item, and *Move focus to the dock* (Win+Alt+T) lets you use the dock with the arrow keys, Enter, Shift+Enter (new window), the menu key and Esc.
- **Global shortcuts:** show the dock (Ctrl+Alt+D by default), open the quick launcher (Win+Alt+Space), open settings, edit the dock, pin an app, toggle auto-hide, mute the sound or the microphone, change the volume, switch profile, all configurable in *Settings › Keyboard shortcuts*. Shortcuts another app already uses are flagged there.
- **Text size:** *Settings › Appearance › Text size* scales the text of settings, widget panels and menus (System follows Windows' text size).
- **Widget style:** *Cards* puts every widget on a card of its own; *Seamless* sets widgets right on the dock with a thin line between two of them (sticky notes and the water tracker keep a fainter color). *Settings › Appearance › Widget style*; theme files, saved presets and profiles keep it.
- **Even widget widths:** widget cards round their width up to a common grid and are at least a tile, a standard card or a wide card wide, depending on the layout, so the dock keeps an even rhythm. The widget sits centered in its card; nothing is cut off (on by default, *Settings › Appearance*).
- **Theme files:** *Settings › Appearance › Theme file* exports your dock's look (colors, glass, size, shape) to a `.dockhub-theme` file that anyone can import. *Save current layout as a preset* keeps your look and widgets as a preset you can apply again later. Presets and themes never move the dock: the screen edge stays where you put it.
- **Menus:** context menus and widget panels always open outside the dock, next to the pointer or the item.
- **Settings window:** a grouped menu (Dock, Widgets, System, About) that opens on an *Overview*: a live preview of your dock, the settings you change most (theme, backdrop, size, edge, widget style), shortcuts to add a widget, edit the dock, undo or switch profile, and the state of updates, backups and profiles. *Ctrl+F* searches every setting and widget.
- **Dock menu** (right-click an empty area): Undo, Edit dock, Add widget, Pin app, Add separator, Task Manager, Quick settings, Auto-hide, Hide Windows taskbar, Position, Settings, Exit.

## Widgets

Every widget can be added more than once, and each copy has its own settings. Change a widget's layout from its right-click **Layout** menu or in *Settings › Dock items*.

| Category | Widget | Layouts | Notes |
|---|---|---|---|
| Clocks | **Clock** | Analog, Digital, Calendar | 12 or 24 hour format, seconds, date format. The calendar layout shows your next reminder. |
| | **World clock** | Single city, Multiple cities | Add and reorder cities; day and night dial. |
| | **Stopwatch** | Single | Click to start or pause, right-click to reset. |
| | **Focus timer** | Single | Pomodoro with focus and break lengths and a notification at the end. |
| | **Countdown** | Single | Presets, a label, and a notification at the end. |
| | **Alarm** | Single | Time, label, repeat daily. The alarm notification plays a sound and stays until dismissed. |
| | **Time progress** | Bar, Ring | How much of the day, week, month or year has passed. |
| Reminders | **Hydration** | Timer, Daily goal | Click to add a glass. Interval notifications include an "I drank" button. |
| | **Reminders** | List, Next, Count | Toast notifications with a "Snooze 10 min" action. |
| Notes | **Sticky note** | Single | A preview lives in the dock; clicking opens a large paper in six colors with adjustable text size. Saves automatically. |
| Media | **Now playing** | Full, Compact, Mini | Windows media controls (SMTC): Spotify, browsers, VLC and more. Cover art, progress, previous, play and next. |
| | **Audio device** | Compact, Slider | Switch between output devices, scroll to adjust volume, click to mute. |
| System | **CPU and memory** | Numbers, Rings, Bars | Refresh interval from 1 to 10 seconds. |
| | **Network** | Numbers only, With graph | Live download and upload speed. |
| | **Status** | Rings, Percentage ring, Icons only | Battery, disk, memory and processor. |
| | **Recycle bin** | Icon only, Detailed | Drag files onto it to delete them, click to open, right-click to empty. |
| | **Device batteries** | Single device, Multiple devices | Battery levels of connected Bluetooth (classic and Low Energy) headsets, mice, keyboards and controllers, Xbox and PlayStation controllers, and [USB receivers](docs/battery-devices.md): Logitech (Unifying, Lightspeed, Bolt), Razer, HyperX, SteelSeries Arctis 7 and LAMZU Atlantis Mini. Click it to see every device and choose which one the dock shows. A notification warns when a device drops to 15% (adjustable or off in its settings). |
| | **GPU** | Numbers, Rings, Bars | Graphics card load and video memory from the Task Manager counters. |
| | **Brightness** | Slider, Icon only | Scroll to change the brightness of laptop screens and DDC/CI monitors; shows when night light is on. |
| | **Wi-Fi and Bluetooth** | Buttons, Icon only | Turn Wi-Fi and Bluetooth on or off in one click, like Quick Settings. |
| AI | **AI usage** | Numbers, Rings, Bars | Limits of Claude Code (5-hour and weekly, from its CLI), OpenAI Codex (5-hour and weekly, read from its session logs) or Gemini CLI (requests today against your daily limit). Checked every 5 to 60 minutes. |
| Weather | **Weather** | Current, Condition, Hourly forecast | [Open-Meteo](https://open-meteo.com/), no API key needed. Uses a city or your Windows location. |
| Productivity | **Calendar** | Next event, Time until | Your next meeting from any iCal (.ics) link (Google, Outlook...), a **Join** button for Teams, Meet and Zoom, today's agenda and a reminder before events. |
| | **Clipboard history** | Latest item, Icon only | The last 25 copied texts and images; click to copy again, pin favorites. Memory only; password managers are skipped. |
| | **Folder stack** | Stack, Detailed | Newest files of Downloads (or any folder); drag files out into other apps. |
| | **Exchange rates** | Single pair, Several pairs | Daily ECB rates via [Frankfurter](https://frankfurter.dev/) and crypto prices (BTC, ETH, SOL and 16 more) via [CoinGecko](https://www.coingecko.com/), with the change since the previous day and a two-week trend. Mix both in one list, for example `TRY, EUR, BTC`. |
| | **To do** | List, Count | Today's tasks: a simple list kept on this PC or today's and overdue tasks from [Todoist](https://todoist.com/) (personal API token, stored encrypted). Tick to complete, type to add. |
| | **Screenshot** | Icon only, Buttons | Opens the Windows snipping overlay, or saves every screen to *Pictures › Screenshots* and copies it, with an optional delay. |

To add a widget, open *Settings › Widget gallery* and press **Add** on its card or drag the card onto the dock (to a spot between items, into a folder, or onto the tray area to pin it to the right edge). Each widget has one card with its layouts to pick from, its width and how many are already on the dock; search by name in any language or filter by category. You can also right-click an empty area of the dock and choose **Add widget**, or press the **+** tile in edit mode. Right-clicking a widget on the dock gives it its own actions, layouts and settings.

<p align="center">
  <img src="docs/images/widget-menu.jpg" alt="Right-click menu of the hydration widget with drink a glass, reset today, appearance and widget settings" width="62%">
</p>

## How the taskbar replacement works

**No administrator rights are needed.** DockHub runs `asInvoker`. Running it elevated is not recommended, because File Explorer cannot drag and drop into an elevated window (UIPI). Explorer is never closed; DockHub runs alongside it.

With *Settings › General › Replace the taskbar* set to **DockHub** (the default):

1. ManagedShell's task service tracks open windows, and its tray service takes over `Shell_TrayWnd` notifications so apps register their tray icons with the dock.
2. Explorer's "taskman" window is handed back, so the **Windows key** keeps opening Start.
3. The Windows taskbar state (`ABM_GETSTATE`) is written to `%AppData%\DockHub\session.json`. The taskbar is then switched to auto-hide and the `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` windows are hidden.
4. A lightweight watcher running every 250 ms hides the Explorer taskbar again if it reappears on its own. It stays out of the way while Start, search or a shell menu is open.
5. An invisible, click-through AppBar reserves the dock's thickness at the screen edge, sized with the DPI of the dock's display. It registers again when Explorer restarts, and if Windows still leaves the dock's band in the work area, DockHub trims the work area itself (and undoes that when it exits).

In **Both** mode, the Windows taskbar is left untouched and the tray is not taken over; the dock sits above the taskbar. Switching modes restarts the app.

<p align="center">
  <img src="docs/images/settings-general.jpg" alt="DockHub settings General page with the replace taskbar switch, autostart options and the restore taskbar button" width="88%">
</p>

### The Windows taskbar always comes back

The taskbar is restored automatically when:

- the mode is switched to **Both**,
- DockHub exits normally (dock menu, tray menu, `--exit`),
- the Windows session ends (`SessionEnding`),
- an unhandled error occurs (`UnhandledException` / `ProcessExit`),
- DockHub is uninstalled.

If DockHub is killed (Task Manager, power loss), the taskbar stays in auto-hide mode and still appears when the pointer reaches the bottom edge. On the next launch, or with `--restore-taskbar`, `session.json` is read and the original state is restored. A `TaskbarCreated` broadcast makes apps register their tray icons with Explorer again.

**In an emergency**, use any of these:

```powershell
DockHub.exe --restore-taskbar
```

- *Settings › General › Restore the Windows taskbar*
- Tray menu › *Restore taskbar*
- Last resort: restart `explorer.exe` and turn off *Settings › Personalization › Taskbar › Automatically hide the taskbar* in Windows.

### When DockHub crashes

- **Windows starts it again.** DockHub asks Windows Error Reporting to restart it after a crash or a hang. Windows only does this for a process that has run for at least a minute, and never after an update or a restart of the PC.
- **No endless loop.** If Windows has to restart DockHub a third time within 10 minutes, DockHub stops instead, gives the taskbar back and turns the automatic restart off (a notification says so). Starting DockHub yourself still works; turn the restart back on in *Settings › Backup and troubleshooting*. Ending DockHub in Task Manager does not count as a crash here.
- **You hear about it.** When the previous session did not end cleanly (a crash, or ending it in Task Manager), the next start shows a notification with *Report a problem* and *Open log*. A PC that restarted in the meantime (shutdown, power loss) is not reported.
- **The report says where, nothing personal.** DockHub reads what Windows recorded in the Application event log: the faulting module, exception code and offset, the .NET exception type and the first stack frames. Exception messages, file paths and window titles are left out. *Report a problem* adds this to the bug report, and *Settings › About › Last crash* shows it.

> [!IMPORTANT]
> Windows 11's network, volume and battery icons live inside Explorer, so the dock shows its own equivalents. Reach the quick settings panel by right-clicking the clock or the dock, or with Win+A. Do not run other taskbar replacements (StartAllBack, ExplorerPatcher, RetroBar and similar) at the same time.

## Build from source

### Prerequisites

- Windows 10 1809 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or newer
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) to build the installer (`winget install JRSoftware.InnoSetup`)

NuGet dependencies are restored automatically:

- [`ManagedShell`](https://www.nuget.org/packages/ManagedShell) 0.0.372: task list, system tray, AppBar and full screen detection (Apache-2.0)
- [`Microsoft.Toolkit.Uwp.Notifications`](https://www.nuget.org/packages/Microsoft.Toolkit.Uwp.Notifications) 7.1.3: toast notifications (MIT)

### Build and run

```powershell
git clone https://github.com/sametgurtuna/DockHub.git
cd DockHub
dotnet build CustomDock.sln -c Release
dotnet run --project src/CustomDock -c Release
```

You can also open `CustomDock.sln` in Visual Studio 2026 and press F5.

### Running tests

```powershell
dotnet test CustomDock.sln
```

The unit tests (`tests/CustomDock.Tests`) cover config loading and migration, dock item operations, app matching and pin repair, and parsing. They use a throwaway `DOCKHUB_HOME`, so your own settings are never touched. GitHub Actions runs the build and tests on every push, and a `v*` tag builds the installer into a draft release.

### Portable folder

```powershell
dotnet publish src/CustomDock -c Release -r win-x64 --self-contained false -o publish
```

`publish\DockHub.exe` runs as is. Use `--self-contained true` to bundle the .NET runtime.

### Installer

```powershell
pwsh installer/build.ps1
```

The script publishes a self-contained ReadyToRun build to `artifacts/publish/win-x64` and compiles [`installer/DockHub.iss`](installer/DockHub.iss) into `installer/Output/DockHub-Setup-<version>-x64.exe`. The version comes from `src/CustomDock/CustomDock.csproj`.

What the installer does:

| | |
|---|---|
| Install scope | Per user under `%LocalAppData%\Programs\DockHub` (admin install is available from the dialog) |
| Autostart | `HKCU\...\Run\DockHub` = `"...\DockHub.exe" --startup`, enabled by default |
| Upgrades | Closes the running dock with `--exit` before replacing files |
| Uninstall | Runs `--exit` and `--restore-taskbar`, then removes the autostart entry and the File Explorer verbs |
| Languages | English, Turkish, German, Spanish |

## Command line

| Command | Description |
|---|---|
| `DockHub.exe` | Starts the dock. If it is already running, brings the settings window to the front. |
| `DockHub.exe --exit` | Closes the running instance cleanly (the taskbar comes back). |
| `DockHub.exe --pin "<file>"` | Pins an app to the dock (used by the File Explorer command). Starts the dock if needed. |
| `DockHub.exe --install-widget "<file>"` | Asks to install a `.dockwidget` package (used when you open one in File Explorer). Starts the dock if needed. |
| `DockHub.exe --restore-taskbar` | **Emergency:** restores the Windows taskbar under any circumstances. |
| `DockHub.exe --startup` | Used by the autostart entry. |
| `DockHub.exe --restarted-after-crash` | Used by Windows when it restarts DockHub after a crash. |
| `DockHub.exe --crash-test` | Only with `"debugLogging": true`: crashes on purpose 65 seconds after starting, to try out the restart and the crash notice. |

The `DOCKHUB_HOME` environment variable changes the settings and data folder, which is useful for portable use or testing, for example `set DOCKHUB_HOME=D:\DockTest`.

## Configuration and data

| File | Contents |
|---|---|
| `%AppData%\DockHub\config.json` | All settings and dock items (order, layout, per-item settings) |
| `%AppData%\DockHub\data\notes-<item>.json` | Text of each sticky note |
| `%AppData%\DockHub\data\reminders.json` | Pending reminders |
| `%AppData%\DockHub\data\hydration.json` | Daily water counter |
| `%AppData%\DockHub\data\weather-cache.json` | Latest weather per location, for an instant first paint |
| `%AppData%\DockHub\data\trash\` | Data of removed widgets (for example sticky notes), kept for 7 days |
| `%AppData%\DockHub\session.json` | Taskbar restore information, whether the last session ended cleanly, recent crashes and the last crash report |
| `%AppData%\DockHub\pin-requests.txt`, `widget-requests.txt` | Pending pin and widget install requests from File Explorer (temporary) |
| `%AppData%\DockHub\log.txt` | Log file (rotates at 512 KB) |
| `%AppData%\DockHub\backups\` | A copy of `config.json` from each of the last 7 days, plus the state saved before an import |
| `%AppData%\DockHub\widgets\` | Installed web widgets, one folder each |
| `%AppData%\DockHub\data\todo-<item>.json` | Tasks of a local To do widget |
| `%AppData%\DockHub\battery-devices.json` | Optional: extra devices for the Device batteries widget ([format](docs/battery-devices.md#adding-a-device)) |

- Files are written to a temporary file first and then moved into place, so an interrupted write never corrupts them.
- A corrupt `config.json` is backed up as `config.json.corrupt-<date>` and DockHub loads the newest daily backup (and tells you); only without one does it start with defaults.
- *Settings › Backup and troubleshooting* exports everything (dock, widgets, notes, reminders) to a `.zip` and imports it again.
- Removing items, deleting folders and applying a layout preset can be undone for a few seconds from the toast next to the dock, from the dock's right-click menu, or with Ctrl+Z in Settings.
- Settings from the project's previous name (`%AppData%\CustomDock`, `CUSTOMDOCK_HOME`) are migrated automatically on first launch.
- Version 1 `config.json` files are upgraded to version 2 automatically.

<details>
<summary>Example <code>config.json</code> (shortened)</summary>

```json
{
  "version": 2,
  "taskbarMode": "Replace",
  "hideOnFullscreen": true,
  "startWithWindows": true,
  "showStartButton": true,
  "showSearchButton": true,
  "showRunningApps": true,
  "showTray": true,
  "pinnedTrayIcons": ["c:\\program files\\spotify\\spotify.exe"],
  "edge": "Bottom",
  "autoHide": false,
  "theme": "Dark",
  "backdrop": "Blur",
  "tintOpacity": 0.35,
  "size": "Small",
  "layout": "Floating",
  "widthMode": "Full",
  "alignment": "Center",
  "edgeMargin": 6,
  "items": [
    { "id": "e0e4424de2", "kind": "App", "path": "C:\\WINDOWS\\explorer.exe" },
    { "id": "d09f6ec024", "kind": "Separator" },
    { "id": "521927ad3f", "kind": "Widget", "widget": "clock", "variant": "analog",
      "settings": { "use24Hour": true, "showSeconds": false, "dateFormat": "Short" } },
    { "id": "2f50f40ec4", "kind": "Widget", "widget": "weather", "variant": "hourly",
      "settings": { "locationMode": "City", "cityName": "Istanbul", "latitude": 41.0138, "longitude": 28.9497 } }
  ]
}
```

An app item's `path` can be an `.exe`, an `.lnk` shortcut, any file, or a Store or PWA app in the form `shell:AppsFolder\<AUMID>` (*Settings › Dock items › Add app* lists these).

</details>

## Permissions and privacy

- **File Explorer command.** *Pin to DockHub* is registered under `HKCU\Software\Classes\exefile\shell` and `lnkfile\shell`. No admin rights are needed, turning the setting off removes the keys, and the path is updated if the app moves.
- **Autostart.** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DockHub`, toggled from *Settings › General*.
- **Location** (weather set to *Automatic*). Both location services and *Let desktop apps access your location* must be on in *Windows Settings › Privacy & security › Location*. Otherwise the widget asks for a city.
- **Notifications.** No extra permission. The app registers its AUMID under HKCU on first use. With *Do not disturb* on, notifications collect in the notification center; if a toast cannot be shown, a tray balloon is used instead.
- **Media (SMTC).** No permission or account. The player only needs to support Windows media controls.
- **Microphone indicator.** DockHub reads which apps use the microphone from the same Windows privacy records as the Windows microphone icon; it never opens the microphone itself.
- **Notification count.** Read from the Windows notification database of your account (read-only), only while the option is on.
- **Wi-Fi and Bluetooth widget.** Uses the Windows radio API; if Windows doesn't allow it, the buttons open the matching Settings page instead.
- **Network.** DockHub itself only contacts `api.open-meteo.com` (weather), `geocoding-api.open-meteo.com` (city search), `api.frankfurter.dev` and, for crypto symbols, `api.coingecko.com` (only with the Exchange rates widget), the calendar links you enter (Calendar widget) and, once a day unless you turn it off, `api.github.com` to look for a newer release. The *AI usage* widget, if you add it, runs your locally installed Claude CLI (`claude -p /usage`) at the interval you choose, and that CLI talks to Anthropic with your own login; for Codex and Gemini CLI it only reads the logs those tools keep in your user folder (`~/.codex/sessions`, `~/.gemini/tmp`). The *To do* widget talks to `api.todoist.com` only when you set it to Todoist, installing a web widget from a link downloads it from that link, and while *Settings › Widget gallery* is open DockHub fetches the community widget list from `raw.githubusercontent.com` (at most once a day). Web widgets reach only the hosts they declare, or a server address you enter in their settings. There is no telemetry.

## Project structure

```text
CustomDock.sln
installer/
├── DockHub.iss              Inno Setup script (per-user install, autostart, clean uninstall)
└── build.ps1                Publishes the app and compiles the installer
src/CustomDock/              Produces DockHub.exe
├── App.xaml(.cs)            Entry point: single instance, --exit / --restore-taskbar, crash safety, shell and dock setup
├── app.manifest             PerMonitorV2 DPI, asInvoker
├── Assets/DockHub.ico       App and tray icon (generated by tools/generate-icon.ps1)
├── Core/                    AppConfig (v2 item model), ConfigService (v1 to v2 migration), JSON storage, theme, log
├── Native/                  P/Invoke, DWM and glass effects, monitors, high resolution shell icons
├── Shell/                   ManagedShell integration
│   ├── ShellHost.cs           Tasks, tray, Start, search and notification center commands
│   ├── TaskbarController.cs   Hiding and restoring the Windows taskbar, crash recovery
│   ├── StartMenuLauncher.cs   Start menu through IImmersiveLauncher
│   ├── RunningAppsService.cs  Grouping windows by app
│   ├── JumpListService.cs     Reads an app's Jump List (recent/pinned tasks) for the right-click menu
│   └── AppKeys.cs, TrayPreferences.cs, DefaultItems.cs
├── Services/                Clock, system, GPU and network monitors, media (SMTC), weather (with WeatherHub cache),
│                            notifications, reminders, hydration, app launcher, keyboard layouts, microphone,
│                            notification center, virtual desktops, brightness, radios, AI usage, Todoist,
│                            Battery/ (device batteries: Bluetooth, HID protocols, device catalog)
├── Dock/                    DockWindow (zones, scrolling, drag and drop, position, auto-hide), AppButton,
│                            WidgetItemView (card, vertical tile and panel), GroupItemView (folders),
│                            WindowPreviewWindow (live thumbnails), GenieEffectHelper, PopupAnimationHelper,
│                            TrayIconView, SpaceReserver, EdgeTriggerWindow, TrayIconManager, LauncherWindow
│                            (quick launcher), keyboard language, microphone and virtual desktop indicators
├── Controls/                WidgetCard, DockZonesPanel, RingGauge, AnalogClock, WeatherIcon, TickBar, Sparkline,
│                            Glyphs, SettingRow
├── Settings/                Settings window (Mica), widget gallery, app picker, widget settings templates
├── Themes/                  Dark.xaml, Light.xaml (colors), Controls.xaml (styles)
└── Widgets/                 WidgetBase, WidgetRegistry, CompactTile and one folder per widget
    ├── Clock/  WorldClock/  Timers/ (stopwatch, focus, countdown, alarm)  TimeProgress/
    ├── Hydration/  Reminders/  Notes/  Media/  Audio/
    ├── System/ (CPU and memory, network, status)  Gpu/  Display/ (brightness)  Radios/  RecycleBin/  BatteryDevices/
    ├── Calendar/  Clipboard/  Stack/  Currency/  Todo/  Screenshot/
    └── AI/ (AI usage)  Weather/  Web/ (HTML/JavaScript widgets, link installs)
packaging/winget/            winget manifest templates (submitted by .github/workflows/winget.yml)
packaging/signing/           Optional code signing of releases (Azure Artifact Signing) and how to set it up
samples/widgets/             Web widget examples (hello-world, github-stars, github-pulls, github-actions, home-assistant)
                             and index.json, the community list shown in the gallery
tools/generate-icon.ps1      Renders Assets/DockHub.ico
tools/measure-idle.ps1       Idle CPU, memory and start-up measurement
docs/                        Widget SDK and supported battery devices; images/ holds the README artwork
```

## Web widgets (HTML and JavaScript)

Anyone can build a widget with HTML, CSS and JavaScript, no C# needed. Web widgets run in Microsoft Edge WebView2, follow the dock theme and use a small `window.dockhub` API for settings, storage, notifications and their right-click menu. Each widget only reaches the internet hosts its manifest lists, and users see those permissions before installing a `.dockwidget` package (open the file, or use *Settings › Widget gallery › Install widget…*) or a link (a `manifest.json`, a `.dockwidget` file or a GitHub folder) with *Install from link…*. The gallery also lists community widgets from a list kept in this repository (GitHub pull requests, GitHub Actions, Home Assistant and more) that install with one click; anyone can add one with a pull request.

With `"debugLogging": true`, a widget reloads by itself whenever you save one of its files. See the [widget SDK guide](docs/widget-sdk.md) and the examples in [`samples/widgets`](samples/widgets).

## Writing a widget

1. Add `Widgets/Sample/SampleWidget.xaml`. The root element must be `w:WidgetBase`. Use elements named `Layout_<variant>` for different layouts:

   ```xml
   <w:WidgetBase x:Class="CustomDock.Widgets.SampleWidget"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:w="clr-namespace:CustomDock.Widgets">
       <Grid>
           <TextBlock x:Name="Layout_big" Style="{StaticResource ValueText}" VerticalAlignment="Center" />
           <TextBlock x:Name="Layout_small" Style="{StaticResource TitleText}" VerticalAlignment="Center" />
       </Grid>
   </w:WidgetBase>
   ```

2. Implement the lifecycle in code-behind. Subscribe to services in `OnAttached`, unsubscribe in `OnDetached`, and fill in `UpdateCompact` for vertical docks:

   ```csharp
   public sealed class SampleSettings : ObservableObject
   {
       private string _text = "Hello";
       public string Text { get => _text; set => Set(ref _text, value); }
   }

   public partial class SampleWidget : WidgetBase
   {
       private SampleSettings _settings = new();

       public SampleWidget() => InitializeComponent();

       protected override void OnAttached()
       {
           _settings = GetSettings<SampleSettings>();     // per-item settings in config.json, saved on change
           _settings.PropertyChanged += (_, _) => Render();
           AppServices.Clock.MinuteTick += OnTick;       // shared, aligned timer
       }

       protected override void OnDetached() => AppServices.Clock.MinuteTick -= OnTick;

       protected override void OnVariantChanged()        // on first attach and whenever the layout changes
       {
           ShowLayout(Layout_big, Layout_small);
           Render();
       }

       private void OnTick(object? s, DateTime now) => Render();

       private void Render()
       {
           Layout_big.Text = Layout_small.Text = _settings.Text;
           RefreshCompact();                             // keep the vertical tile in sync
       }

       protected override void UpdateCompact(CompactTile tile)
       {
           tile.ShowGlyph(Descriptor.Icon, Descriptor.AccentKey);
           tile.Text = _settings.Text[..Math.Min(4, _settings.Text.Length)];
       }
   }
   ```

3. Register the widget in `Widgets/WidgetRegistry.cs`:

   ```csharp
   new()
   {
       Id = "sample", Name = "Sample", Category = WidgetCategories.Notes, Description = "...",
       IconPath = "M4,4 H20 V20 H4 Z", AccentKey = "AccentGreenBrush",
       Variants = new[] { new WidgetVariant("big", "Big"), new WidgetVariant("small", "Small") },
       Factory = () => new SampleWidget(), SettingsType = typeof(SampleSettings),
   },
   ```

4. Optionally, add a `DataTemplate` with `DataType="{x:Type w:SampleSettings}"` to `Settings/WidgetSettingsTemplates.xaml` for its settings UI. For richer UIs, return a `UserControl` from `SettingsViewFactory` (see `WeatherSettingsView`).

The widget then shows up in the gallery automatically. Other helpers:

- **Popups:** `OpenPopup(popup)` opens a popup on the correct side of the dock, pauses auto-hide while it is open, and aligns to the tile on vertical docks.
- **Hiding:** a widget that sets its own `Visibility` to `Collapsed` is removed from the dock as well.
- **Tile clicks:** if `OnCompactClick` returns `true`, clicking the tile on a vertical dock runs that action instead of opening the panel.

## Performance

- **Timers.** Clock-based widgets share a single timer aligned to seconds or minutes. It ticks once a minute when no widget needs seconds and stops when nothing is subscribed. No timer runs more often than once per second, except the taskbar watcher (event driven, with a 2-second safety check that runs every 250 ms for 5 seconds after Explorer shows its taskbar) and the Start menu visibility poll (200 ms).
- **Only what's on the dock.** Services start the first time something uses them: without a Device batteries widget nothing scans for devices, without a Brightness widget nothing asks the monitors.
- **Monitoring.** System monitoring uses `GetSystemTimes` and `GlobalMemoryStatusEx`; disk usage is sampled once a minute. Network measurement runs only while a network widget is on the dock.
- **Event driven.** The window list and full screen detection update through ManagedShell's shell hooks. Media updates through SMTC events, and the progress bar ticks once per second only while playing.
- **Weather.** Widgets that share a location share one request and cache. Data refreshes every 30 minutes and retries after 3 minutes on failure.
- **Scrolling.** The scroll animation runs only while scrolling.
- **Start-up.** The dock is drawn first; the first weather, device battery and AI usage requests follow 2 to 8 seconds later instead of all at once. `log.txt` and *Settings › About › Copy diagnostics* show the time from launch to the first dock frame.
- **Measuring.** `tools/measure-idle.ps1` records DockHub's CPU, memory and garbage collector counters while the computer is idle (it needs [dotnet-counters](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters)) and compares them with the targets: under 0.1% CPU and under 10% memory growth.

## Known limitations

- **Window effects.** The blurred glass effect uses `SetWindowCompositionAttribute`, so it works even when the window is inactive. Corner rounding comes from DWM (about 8 px); corners stay square on Windows 10.
- **Shell integration.** Windows 11's XAML tray items (network, volume and battery quick settings, language bar) belong to Explorer, so the dock shows its own network, volume and battery icons and opens Windows' quick settings panel from them. The per-app taskbar progress indicator may not appear for some apps, because the Windows key stays with Explorer.
- **Multiple monitors.** The tray icons live on the main dock. With *Show on all displays* the other displays get a dock with apps, and any widget can be moved to one of them; each widget sits on one dock only, and a widget whose display is disconnected waits on the main dock until it is back. The Windows taskbar is hidden on every display.
- **Language.** The interface is available in English, Turkish, German and Spanish (*Settings › General › Language*; System follows your Windows display language). Dates and numbers follow your Windows locale. Text that has no translation yet appears in English. Translations live in `src/CustomDock/Resources/Strings_<code>.json`, with the English text as the key; a test checks that every language has the same keys and placeholders.

## FAQ

<details>
<summary><b>What happens to my Windows taskbar?</b></summary>

In the default mode it is hidden and switched to auto-hide; Explorer keeps running. It returns when DockHub exits, crashes, is uninstalled, or when the session ends. In *Both* mode the taskbar is never touched.
</details>

<details>
<summary><b>Does DockHub start with Windows?</b></summary>

Yes. The installer enables it by default. You can turn it off during setup or later in *Settings › General*.
</details>

<details>
<summary><b>Does it need administrator rights?</b></summary>

No. DockHub installs and runs as a normal user.
</details>

<details>
<summary><b>Does it send any data?</b></summary>

Only weather and city search requests to Open-Meteo, plus whatever the widgets you add need (exchange rates and crypto prices, your calendar links, Todoist, web widgets' declared hosts) and a daily update check you can turn off. If you add the AI usage widget with Claude Code, your local Claude CLI checks your usage with your own account. There is no DockHub account and no telemetry.
</details>

<details>
<summary><b>An app doesn't show up on the dock, or its icon is missing. What can I do?</b></summary>

Open *Settings › About › Report a problem*. It opens a bug report with your DockHub and Windows versions, language and widgets filled in, and copies a diagnostics report you can paste into it (*Copy diagnostics* copies the report alone). The report lists every window DockHub knows about, why it is or isn't shown, and whether each pinned app's icon and path were found. From a terminal, `plans/tools/window-diag.ps1 -ProcessName <name>` shows the same window details for one app. Setting `"debugLogging": true` in `config.json` (or `DOCKHUB_DEBUG=1`) writes extra detail to `log.txt`.

Pins of apps that update themselves into versioned folders (Discord, Slack, Microsoft Store apps) are repaired automatically on start.
</details>

<details>
<summary><b>Can I use it with StartAllBack or ExplorerPatcher?</b></summary>

No. Taskbar replacements should not run at the same time.
</details>

## Contributing

Bug reports, widget ideas and pull requests are welcome. Please open an [issue](https://github.com/sametgurtuna/DockHub/issues) first for larger changes so the approach can be discussed. When filing a bug, attach `%AppData%\DockHub\log.txt` if possible.

## License and credits

This repository does not include a license file yet. Until one is added, all rights are reserved by the author.

DockHub builds on excellent open source work:

- [ManagedShell](https://github.com/cairoshell/ManagedShell) (Apache-2.0)
- The Start menu (`IImmersiveLauncher`), tray icon mouse forwarding and "show desktop" techniques are adapted from [RetroBar](https://github.com/dremin/RetroBar) (Apache-2.0)
- [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit) (MIT)
- Weather data by [Open-Meteo](https://open-meteo.com/) (CC BY 4.0)

Windows is a trademark of Microsoft Corporation. DockHub is not affiliated with Microsoft.

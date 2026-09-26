# DockHub widget SDK

Build a DockHub widget with HTML, CSS and JavaScript. It runs in Microsoft Edge WebView2 inside a dock card, follows the dock's theme, and talks to DockHub through a small `window.dockhub` API. No C#, no build step.

Two complete examples live in [`samples/widgets`](../samples/widgets):

- `hello-world`: settings, per-widget storage, a context menu item and a notification.
- `github-stars`: network access to one declared host.

## Try it in two minutes

1. Copy `samples/widgets/hello-world` to `%AppData%\DockHub\widgets\hello-world`.
2. Restart DockHub (tray menu › Restart, or *Settings › General › Restart*).
3. Open *Settings › Widget gallery*: your widget is listed under **Web widgets**. Press **+**.

While developing, set `"debugLogging": true` in `%AppData%\DockHub\config.json`. The widget's right-click menu then has **Developer tools** (the Edge DevTools for that widget), and **Reload** is always there.

## Package and share

A `.dockwidget` file is a zip of the widget folder (the files can be at the zip's root or inside one folder). Users install it with *Settings › Widget gallery › Install widget…*. DockHub shows the name, version, author and the permissions before installing. Installing a package with the same `id` replaces the older version and keeps each widget's settings and storage.

## manifest.json

```json
{
  "id": "com.example.pomodoro-plus",
  "name": "Pomodoro+",
  "version": "1.0.0",
  "author": "Your name",
  "description": "What the widget shows, in one or two sentences.",
  "entry": "index.html",
  "minDockHubVersion": "0.7.0",
  "variants": [
    { "id": "standard", "name": "Standard", "size": "standard" },
    { "id": "mini", "name": "Mini", "size": "compact" }
  ],
  "settings": [
    { "key": "minutes", "type": "number", "label": "Focus length", "default": 25, "min": 5, "max": 90 },
    { "key": "sound", "type": "toggle", "label": "Play a sound", "default": true },
    { "key": "mode", "type": "choice", "label": "Mode", "options": ["Work", "Study"], "default": "Work" },
    { "key": "label", "type": "text", "label": "Label", "description": "Shown under the timer." }
  ],
  "permissions": { "network": ["api.example.com", "*.example.org"], "notifications": true }
}
```

| Field | Notes |
|---|---|
| `id` | Lower-case letters, digits, dots and dashes (3 to 64 characters). Reverse domain style is recommended. |
| `entry` | The HTML file to load, relative to the widget folder. |
| `variants` | Layouts users pick from the widget's *Appearance* menu. `size` is `compact` (44×44), `standard` (170×44) or `wide` (260×44), in dock units. |
| `settings` | Shown in *Settings › Dock items* when the widget is selected. Types: `text`, `number` (`min`, `max`), `toggle`, `choice` (`options`). |
| `permissions.network` | The only hosts `fetch`, images and scripts may load from (HTTPS or WSS). Everything else is blocked. `*.example.org` also matches `example.org`. |
| `permissions.notifications` | Allows `dockhub.notify`. |

## The widget's page

- Files are served from a private `https://<id>.widget.dockhub/` address. There is no access to local files, other sites' cookies, downloads, pop-up windows, the camera, the microphone or location.
- The page background is transparent: the dock card shows through. Size your content to the variant's size and don't scroll.
- Before your scripts run, DockHub sets these CSS variables on `<html>` and keeps them in sync with the dock's theme:

| Variable | Meaning |
|---|---|
| `--dh-text` | Primary text color |
| `--dh-text-secondary` | Secondary text color |
| `--dh-accent` | Windows accent color |
| `--dh-card` | Card background |
| `--dh-font` | Segoe UI Variable font stack |

`<html data-theme="dark">` (or `light`) is set too.

## window.dockhub

All calls return promises. `apiVersion` is `1`.

```ts
dockhub.settings.get(): Promise<Record<string, unknown>>   // manifest defaults + the user's values
dockhub.settings.onChange(handler: (values) => void)

dockhub.storage.get(key: string): Promise<unknown>         // per widget copy, survives restarts
dockhub.storage.set(key: string, value: unknown): Promise<void>   // 256 KB per widget copy

dockhub.notify({ title, body }): Promise<void>             // needs permissions.notifications
dockhub.openUrl(url: string): Promise<void>                // http(s) only, opens the default browser

dockhub.contextMenu.set(items: { id: string, label: string }[]): Promise<void>   // up to 8 items
dockhub.contextMenu.onSelect(handler: (id: string) => void)

dockhub.onTheme(handler: ({ mode, vars }) => void)
dockhub.onSize(handler: (size: 'compact' | 'standard' | 'wide') => void)
dockhub.size                                                // current size
```

Unknown methods, missing permissions and full storage reject the promise with an `Error`.

## Good practice

- Keep the page light: every widget is part of a shared WebView2 process, but timers and animations still cost battery. Use `setInterval` sparingly and pause work when nothing changes.
- Show something useful at 44 DIP height. Put details in the tooltip (`title` attribute) or open a web page with `dockhub.openUrl`.
- Follow the theme variables instead of hard-coded colors, so the widget looks right in light, dark and high-contrast themes.
- Ask only for the hosts you need. Users see the list before installing.

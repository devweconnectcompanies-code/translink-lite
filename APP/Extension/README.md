# TransLink Lite browser extension

This package is the Manifest V3 thin-client foundation for TransLink-Lite. EXT-001A discovers real Chrome tabs and lets the user select one; it does not capture audio or communicate with the backend.

## Architecture boundary

The Extension is independently built and deployed from the .NET API. A local adapter maps Chrome API objects into presentation-safe `BrowserTab` models. Backend business logic, credentials, database access, AWS access, and translation orchestration do not belong here.

## Requirements

- Node.js compatible with Vite 8 (Node 20.19+ or 22.12+)
- npm
- Google Chrome with Developer mode enabled

## Install and develop

From `APP/Extension`:

```bash
npm install
npm run dev
```

The Vite development page can validate rendering, but cannot discover tabs outside the Chrome extension runtime. It shows a safe explanatory state instead.

## Validate and build

```bash
npm run typecheck
npm run build
```

The ignored `dist/` directory is the unpacked extension package. It contains `manifest.json`, `index.html`, and local compiled assets.

## Load unpacked in Chrome

1. Run `npm run build`.
2. Open `chrome://extensions`.
3. Enable **Developer mode**.
4. Select **Load unpacked**.
5. Choose `APP/Extension/dist`.
6. Pin and open **TransLink Lite**.

## Current capabilities

- queries tabs across open Chrome windows;
- shows safe title, host, favicon fallback, and unsupported state;
- permits exactly one supported HTTP(S) tab selection;
- refreshes manually and reacts to tab lifecycle changes while open;
- stores a selected tab ID locally, then revalidates and clears it when stale;
- provides loading, ready, empty, error, and selected states.

Chrome tab IDs are transient. The stored value is only a convenience and is never treated as durable identity.

## Permissions

- `tabs`: reads tab titles, URLs, and favicon metadata through `chrome.tabs.query({})` without broad host permissions.
- `storage`: stores only the transient selected tab ID in `chrome.storage.local`.

The manifest has no host permissions, `tabCapture`, microphone, remote-code, or backend permissions.

## Deferred

EXT-001B or later will address `tabCapture`, audio capture and 100–200 ms chunking decisions, WebSocket transport, backend sessions, authentication, supported-language translation flows, AWS services, and realtime translated subtitles.

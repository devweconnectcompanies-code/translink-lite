# EXT-001A — Chrome Integration Foundation

## Status

Completed, manually validated in Google Chrome, published, and verified by Backend CI.

## Scope and architecture

EXT-001A replaces the Extension placeholder with a React, TypeScript, Vite, and Manifest V3 popup. It discovers real browser tabs, presents single selection, and establishes typed state and Chrome API boundaries. It has no backend integration.

```text
React popup -> typed UI state -> chromeTabs adapter -> chrome.tabs / chrome.storage
```

The adapter converts `chrome.tabs.Tab` into the application-owned `BrowserTab`; raw Chrome objects do not reach presentation components. The Extension has no source or project dependency on API projects.

## Folder structure

```text
APP/Extension/
├── public/manifest.json
├── src/
│   ├── components/TabCard.tsx
│   ├── models/BrowserTab.ts
│   ├── services/chromeTabs.ts
│   ├── styles/index.css
│   ├── App.tsx
│   └── main.tsx
├── index.html
├── package.json
├── package-lock.json
├── tsconfig.json
└── vite.config.ts
```

Vite copies the manifest to ignored `dist/`, producing a directory Chrome can load unpacked.

## Chrome permissions

| Permission | Rationale |
|---|---|
| `tabs` | Allows `chrome.tabs.query({})` to return title, URL, and favicon metadata across windows without broad host permissions. |
| `storage` | Stores only the selected transient tab ID in `chrome.storage.local`. |

There are no host permissions, `<all_urls>`, `tabCapture`, microphone permissions, or remote code.

## Tab discovery behavior

- Queries all accessible tabs across Chrome windows and discards entries without numeric IDs.
- Supplies untitled-tab and favicon fallbacks.
- Only HTTP(S) pages are selectable; internal and other schemes remain visible but unavailable.
- Sorts by window, active state, then title and supports one selection.
- Revalidates persisted selection and clears stale/unsupported IDs.
- Debounces refreshes for created, removed, updated, attached, and detached events.

## State and errors

`TabListState` is a discriminated union with `loading`, `ready`, `empty`, and `error`. Selection uses text, `aria-pressed`, and styling, not color alone. Outside an extension runtime, the UI explains how it must be opened. Unexpected Chrome/storage errors receive safe messages; undefined fields and favicon failures have deterministic fallbacks.

## Security

- Manifest V3 and locally bundled JavaScript only; no remote code, inline executable script, or `eval`.
- No secrets, tokens, backend configuration, direct PostgreSQL, or AWS access.
- React escapes tab-derived text; there is no unsafe HTML rendering.
- Permissions are limited to current metadata and local-selection requirements.

## Validation

```bash
npm run typecheck
npm run build
dotnet build TransLink.Lite.slnx
dotnet test TransLink.Lite.slnx --no-build
```

`dist/manifest.json` must parse as MV3 and contain only `tabs` and `storage`. Manual Chrome behavior is not validated until the owner completes the unpacked-extension test plan.

Automated results on 2026-09-01:

- Node `v26.0.0` and npm `11.12.1`;
- strict TypeScript check passed;
- Vite production build passed;
- generated MV3 manifest validation passed;
- npm audit reported zero vulnerabilities;
- .NET Release build passed with zero warnings and zero errors;
- 30 unit and 26 integration tests passed with no failures or skips.

## Deferred work

EXT-001B or later: `tabCapture`, audio capture, 100–200 ms audio chunking, WebSocket connection, backend session integration, authentication, language translation, AWS services, and realtime translated subtitles.

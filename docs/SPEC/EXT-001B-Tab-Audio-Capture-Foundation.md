# EXT-001B — Tab Audio Capture Foundation

## Status

Completed, manually validated in Google Chrome, published, and verified by Backend CI.

## Scope

EXT-001B captures audio from exactly one user-selected HTTP(S) Chrome tab, keeps capture alive when the popup closes, restores local playback, measures a lightweight audio level, and cleans up deterministically. Audio remains local and in memory.

Excluded: transport chunking, WebSockets, backend endpoints/sessions, authentication, language selection, AWS services, translation, subtitles, and recording.

## Manifest V3 architecture

```text
React popup
  -> typed runtime command
  -> service worker coordinator
  -> chrome.tabCapture.getMediaStreamId({ targetTabId })
  -> typed offscreen command
  -> offscreen document getUserMedia()
  -> MediaStreamAudioSourceNode
       |-> AudioContext.destination (local playback)
       `-> AnalyserNode -> muted GainNode -> destination (level analysis)
```

The popup is not a capture owner because Chrome closes it when focus changes. The service worker validates the tab, serializes lifecycle operations, owns safe state, and creates/closes the offscreen document. The offscreen document owns DOM media APIs and the live audio graph.

Chrome 116 is the minimum: stream IDs created by a service worker can be consumed by an offscreen document from that version onward.

## Project structure

```text
src/
├── background/serviceWorker.ts
├── components/CaptureControl.tsx
├── messaging/messages.ts
├── models/CaptureState.ts
├── models/tabEligibility.ts
├── offscreen/main.ts
└── services/captureClient.ts
```

## Permissions

| Permission | Purpose |
|---|---|
| `tabs` | Read metadata and validate the selected tab without broad host access. |
| `storage` | Store transient selection and a minimal lifecycle snapshot. |
| `tabCapture` | Obtain audio for the explicitly selected tab. |
| `offscreen` | Run MediaStream and Web Audio beyond popup lifetime. |

There are no host permissions, `<all_urls>`, microphone, or network permissions. `minimum_chrome_version` is `116`.

## Capture lifecycle

The typed states are `idle`, `starting`, `capturing`, `stopping`, and `error`.

1. Selection is revalidated with `chrome.tabs.get`.
2. A user click sends `START_CAPTURE` with one transient tab ID.
3. The service worker rejects concurrent capture and enters `starting`.
4. It creates the offscreen document and requests a stream ID.
5. The offscreen document consumes the ID, creates the audio graph, and reports `CAPTURE_STARTED`.
6. RMS-derived `AUDIO_LEVEL` messages update the popup without persisting audio.
7. Explicit stop, tab closure, unsupported navigation, stream termination, extension reload, or failure releases resources and reconciles state.

Only status, tab ID, and a safe error code are stored in `chrome.storage.session`. Stream IDs are never persisted. A restarted worker checks for the actual offscreen document; incomplete transitions or a missing capture document reset instead of appearing active.

## Audio processing and playback

The offscreen document calls `getUserMedia` with the tab stream ID. An `AnalyserNode` computes an RMS-derived decibel level approximately every 150 ms using a timer that does not depend on hidden-document rendering. The UI maps the range from -60 dB to -12 dB onto its activity meter. This produces activity feedback but no recorded samples or transport chunks.

Chrome suppresses normal local playback during tab capture. The graph connects the source directly to `AudioContext.destination`, restoring playback without microphone input or a feedback loop. A separate analyser branch terminates at a zero-gain node connected to the destination so Chrome continues processing it without duplicating audible output. If the context unexpectedly becomes suspended, the offscreen owner attempts one deterministic resume for that state transition and fails safely if it cannot return to `running`.

Stopping cancels the level timer, clears callbacks, stops tracks, disconnects nodes, closes `AudioContext`, resets memory, and closes the offscreen document.

## Internal messages

`messaging/messages.ts` centralizes discriminated contracts:

- `GET_CAPTURE_STATE`, `START_CAPTURE`, `STOP_CAPTURE`;
- `OFFSCREEN_START_CAPTURE`, `OFFSCREEN_STOP_CAPTURE`;
- `CAPTURE_STARTED`, `CAPTURE_STOPPED`, `CAPTURE_ERROR`;
- `AUDIO_LEVEL`, `CAPTURE_STATE_CHANGED`.

Messages target `background`, `offscreen`, or `popup`. They never include title, URL, media data, token, or secret.

## Errors and tab lifecycle

Unsupported/missing tabs, permission failure, stream failure, audio-context failure, closure, competing capture, and unexpected termination map to safe codes. The popup never renders raw exceptions.

Supported navigation keeps capture attached to the same tab. Navigation to non-HTTP(S) stops capture. Closing before start fails safely; closing during capture stops it. Extension reload/update destroys transient execution and reconciliation returns to idle. Development logs contain only failure categories, not browsing metadata or stream IDs.

## Privacy and security

- Explicit user action is mandatory; capture never auto-starts.
- Exactly one selected tab is captured.
- No microphone, audio persistence, recording, upload, remote script, `eval`, or unsafe HTML.
- Audio stays in the local Web Audio graph and is discarded on stop.
- Active capture is visible and has an explicit stop control.
- No API, PostgreSQL, AWS, or external-host connection exists.

## Build output

Vite produces `manifest.json`, popup assets, stable `service-worker.js`, `offscreen.html`, and local chunks. No runtime file depends on a development server. Ignored `dist/` remains loadable unpacked.

## Automated validation

```bash
npm install
npm run typecheck
npm run build
npm audit
dotnet build TransLink.Lite.slnx --configuration Release
dotnet test TransLink.Lite.slnx --no-build --configuration Release
git diff --check
```

Manual Chrome capture was validated by the owner after the audio graph correction.

Automated results on 2026-09-01:

- Node `v26.0.0` and npm `11.12.1`;
- npm install and audit completed with zero reported vulnerabilities;
- strict TypeScript check and Vite production build passed;
- generated manifest, service-worker, offscreen page, permissions, and absence of development URLs validated;
- .NET Release build passed with zero warnings and zero errors;
- 30 unit and 26 integration tests passed with no failures or skips.

## Manual validation

1. Run `npm run build` from `APP/Extension`.
2. Open `chrome://extensions`, enable Developer mode, and reload TransLink Lite.
3. Open an HTTPS YouTube/video tab and start audible playback.
4. Select it and click **Start capture**.
5. Confirm active state, responsive audio meter, and continued unduplicated playback.
6. Close/reopen the popup and confirm state restoration.
7. Click **Stop capture** and confirm idle state.
8. Start again, close the captured tab, and confirm safe termination.
9. Test a tab closed before start and an internal Chrome page.
10. Inspect service-worker and offscreen logs from `chrome://extensions`; confirm no errors or sensitive data.

## Deferred work

- 100–200 ms or other transport-ready chunking;
- WebSocket transport and reconnection;
- backend stream endpoint and session orchestration;
- authentication and supported-language selection;
- AWS Transcribe/Translate and realtime subtitles.

## Official Chrome references

- [chrome.tabCapture](https://developer.chrome.com/docs/extensions/reference/api/tabCapture)
- [chrome.offscreen](https://developer.chrome.com/docs/extensions/reference/api/offscreen)

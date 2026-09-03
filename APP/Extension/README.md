# TransLink Lite browser extension

This Manifest V3 thin client discovers Chrome tabs, captures one explicitly selected tab, and streams bounded PCM audio to the authenticated backend. It does not record, persist, translate, or send audio directly to external providers.

## Architecture boundary

The Extension is independently built and deployed from the .NET API. The popup owns user selection and controls, a service worker coordinates lifecycle, and an offscreen document owns the long-lived MediaStream, Web Audio graph, AudioWorklet, and WebSocket.

```text
Popup user action
  -> service worker
  -> chrome.tabCapture stream ID
  -> offscreen document
  -> MediaStreamAudioSourceNode
       |-> AudioContext.destination (local playback)
       |-> AnalyserNode -> muted GainNode -> destination (level analysis)
       `-> AudioWorklet -> PCM16 chunks -> authenticated WebSocket
```

Capture and streaming continue when the popup closes. Routing the captured stream once to `AudioContext.destination` restores local playback that Chrome suppresses during tab capture. Backend business logic, database access, AWS access, and translation orchestration do not belong here.

## Requirements

- Node.js compatible with Vite 8 (Node 20.19+ or 22.12+)
- npm
- Google Chrome 116 or later with Developer mode enabled

## Install and develop

From `APP/Extension`:

```bash
npm install
npm run dev
```

The Vite development page can validate rendering, but Chrome tab discovery and capture require the unpacked extension runtime.

## Validate and build

```bash
npm run typecheck
npm run build
```

The ignored `dist/` directory contains the popup, `service-worker.js`, `offscreen.html`, `manifest.json`, and local compiled assets.

## Load unpacked in Chrome

1. Run `npm run build`.
2. Open `chrome://extensions`.
3. Enable **Developer mode**.
4. Select **Load unpacked**.
5. Choose `APP/Extension/dist`.
6. Pin and open **TransLink Lite**.

## Current capabilities

- discovers tabs across Chrome windows with safe metadata fallbacks;
- permits exactly one supported HTTP(S) tab selection;
- revalidates transient selection and reacts to tab lifecycle changes;
- starts capture only from an explicit popup action;
- keeps capture alive after the popup closes;
- exposes idle, starting, capturing, stopping, and safe error states;
- displays a lightweight RMS-derived audio-level indicator;
- restores local source-tab playback through Web Audio;
- converts mono audio to 150 ms PCM16LE chunks in an AudioWorklet;
- streams versioned binary frames to the authenticated backend endpoint;
- stops safely on protocol, network, or bounded-backpressure failure;
- sends an initial `en-US` (locally configurable) source speech language using protocol v2;
- receives generic partial/final transcript events and shows only active state and counters;
- stops and cleans up on request, tab closure, unsupported navigation, or stream failure.

## Permissions

- `tabs`: reads tab metadata and validates selection without broad host permissions.
- `storage`: stores the transient selection locally and a minimal capture snapshot in session storage.
- `tabCapture`: obtains audio from the one tab selected by the user.
- `offscreen`: hosts MediaStream and Web Audio outside the short-lived popup.
- `http://localhost/*` and `https://localhost/*`: restrict the development backend connection to localhost; production endpoint permissions remain deployment work.

Chrome 116 is required so a stream ID created by the service worker can be consumed by the offscreen document. There are no broad host permissions, microphone permissions, remote code, or direct AWS permissions.

## Capture lifecycle and privacy

Only one tab can be captured. The service worker rejects competing starts and reconciles minimal session state after restart. The offscreen document never persists raw audio: it measures signal level, routes audio directly to local playback, sends bounded binary chunks, and releases tracks, nodes, timers, WebSocket, and `AudioContext` on stop.

Tab IDs and stream IDs are transient. Stream IDs are passed directly to the offscreen document and never stored. No title, URL, audio sample, or browsing content is uploaded.

The backend requires JWT authentication. Until production login UX exists, local testing stores a short-lived development token in `chrome.storage.local` under `developmentAccessToken`; follow the EXT-001D specification and remove it afterward. A source speech language may be set under `sourceLanguage` (`en-US` is the fallback). No token or AWS credential is bundled or committed.

The unpacked extension defaults to `ws://localhost:5221/api/realtime/audio`, matching the API's `http` launch profile. A custom deployment endpoint can be supplied through the documented local setting and must use `wss://` outside Development. Safe localhost connection lifecycle diagnostics are mirrored to the service-worker console so they remain inspectable if the offscreen document closes after a failure; they never contain the JWT or captured-page metadata.

## Manual capture validation

1. Build and reload the unpacked extension.
2. Open an HTTPS YouTube/video tab with audible audio.
3. Select it and choose **Start capture**.
4. Confirm the capture state and audio meter react while source audio remains audible.
5. Close and reopen the popup and confirm active state is restored.
6. Choose **Stop capture** and confirm idle state.
7. Start again, close the source tab, and confirm safe termination.
8. On `chrome://extensions`, select the extension's **service worker** link for coordinator logs. While capture is active, Chrome also exposes the offscreen document under inspectable views.

## Deferred

Later phases will implement production authentication UX, complete language selection, translation, Web realtime subtitles, reconnect/resume semantics, and measured production scaling. AWS Transcribe is invoked only by the backend; the Extension never receives AWS credentials or SDK logic.

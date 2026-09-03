import type { BrowserTab } from "../models/BrowserTab";
import type { CaptureErrorCode, CaptureSnapshot } from "../models/CaptureState";

interface CaptureControlProps {
  capture: CaptureSnapshot;
  selectedTab: BrowserTab | null;
  ready: boolean;
  onStart: () => void;
  onStop: () => void;
}

const ERROR_MESSAGES: Record<CaptureErrorCode, string> = {
  "unsupported-tab": "This page cannot be captured. Select a normal HTTP or HTTPS tab.",
  "missing-tab": "The selected tab no longer exists. Refresh and select another tab.",
  "capture-permission": "Chrome could not start tab capture. Keep the source tab open and retry.",
  "stream-acquisition": "The tab audio stream could not be opened. Retry while the tab is playing audio.",
  "audio-context": "The local audio processing path could not start.",
  "tab-closed": "Capture stopped because the selected tab was closed.",
  "unexpected-termination": "The tab audio stream ended unexpectedly. You can retry.",
  "capture-busy": "Another capture operation is already active.",
  "transport-authentication": "A development access token is required before streaming.",
  "transport-connection": "The realtime audio connection could not be established or was lost.",
  "transport-protocol": "The server rejected the realtime audio protocol.",
  "transport-backpressure": "Streaming stopped because the network could not keep up safely.",
  "internal-error": "Capture is temporarily unavailable. Reload the extension and retry.",
};

export function CaptureControl({
  capture,
  selectedTab,
  ready,
  onStart,
  onStop,
}: CaptureControlProps) {
  const canStart = ready && selectedTab !== null;
  const levelPercent = `${Math.round(capture.audioLevel * 100)}%`;

  if (capture.status === "capturing") {
    return (
      <div className="capture-control">
        <div className="capture-status capture-status--active" role="status">
          <span className="capture-dot" aria-hidden="true" />
          <div>
            <strong>Capturing tab audio</strong>
            <span>
              {capture.transport.status === "streaming"
                ? capture.transport.transcriptionActive
                  ? `Transcription active · ${capture.transport.finalTranscriptsReceived} final results`
                  : `Streaming · ${capture.transport.chunksSent} chunks`
                : capture.transport.status === "connected"
                  ? "Transport connected"
                  : capture.hasSignal
                    ? "Audio signal detected"
                    : "Listening for audio…"}
            </span>
          </div>
        </div>
        <div
          className="audio-meter"
          role="meter"
          aria-label="Captured audio level"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(capture.audioLevel * 100)}
        >
          <span style={{ width: levelPercent }} />
        </div>
        <button className="capture-button capture-button--stop" type="button" onClick={onStop}>
          Stop capture
        </button>
      </div>
    );
  }

  if (capture.status === "starting" || capture.status === "stopping") {
    return (
      <div className="capture-control">
        <div className="capture-status" role="status">
          <span className="spinner spinner--small" aria-hidden="true" />
          <strong>{capture.status === "starting" ? "Starting capture…" : "Stopping capture…"}</strong>
        </div>
        <button className="capture-button" type="button" disabled>
          {capture.status === "starting" ? "Starting…" : "Stopping…"}
        </button>
      </div>
    );
  }

  if (capture.status === "error") {
    return (
      <div className="capture-control">
        <p className="capture-error" role="alert">
          {capture.errorCode ? ERROR_MESSAGES[capture.errorCode] : ERROR_MESSAGES["internal-error"]}
        </p>
        <button className="capture-button" type="button" onClick={onStart} disabled={!canStart}>
          Retry capture
        </button>
      </div>
    );
  }

  return (
    <div className="capture-control">
      <p className="capture-hint">
        {selectedTab ? "Ready to capture this tab locally." : "Select a supported tab to continue."}
      </p>
      <button className="capture-button" type="button" onClick={onStart} disabled={!canStart}>
        Start capture
      </button>
    </div>
  );
}

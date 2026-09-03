import type { CaptureErrorCode, CaptureSnapshot } from "../models/CaptureState";
import type { TransportSnapshot } from "../models/TransportState";
import type {
  RealtimeTransportConfiguration,
  RealtimeTransportDiagnostic,
} from "../realtime/RealtimeAudioTransport";

export const MessageType = {
  GetCaptureState: "GET_CAPTURE_STATE",
  StartCapture: "START_CAPTURE",
  StopCapture: "STOP_CAPTURE",
  OffscreenStartCapture: "OFFSCREEN_START_CAPTURE",
  OffscreenStopCapture: "OFFSCREEN_STOP_CAPTURE",
  CaptureStarted: "CAPTURE_STARTED",
  CaptureStopped: "CAPTURE_STOPPED",
  CaptureError: "CAPTURE_ERROR",
  AudioLevel: "AUDIO_LEVEL",
  CaptureStateChanged: "CAPTURE_STATE_CHANGED",
  TransportStateChanged: "TRANSPORT_STATE_CHANGED",
  TransportDiagnostic: "TRANSPORT_DIAGNOSTIC",
} as const;

type MessageTarget = "background" | "offscreen" | "popup";

export type ExtensionMessage =
  | { target: "background"; type: typeof MessageType.GetCaptureState }
  | { target: "background"; type: typeof MessageType.StartCapture; tabId: number }
  | { target: "background"; type: typeof MessageType.StopCapture }
  | {
      target: "offscreen";
      type: typeof MessageType.OffscreenStartCapture;
      tabId: number;
      streamId: string;
      transport: RealtimeTransportConfiguration;
    }
  | { target: "offscreen"; type: typeof MessageType.OffscreenStopCapture }
  | { target: "background"; type: typeof MessageType.CaptureStarted; tabId: number }
  | { target: "background"; type: typeof MessageType.CaptureStopped; errorCode: CaptureErrorCode | null }
  | { target: "background"; type: typeof MessageType.CaptureError; errorCode: CaptureErrorCode }
  | { target: "background"; type: typeof MessageType.AudioLevel; level: number; hasSignal: boolean }
  | { target: "background"; type: typeof MessageType.TransportStateChanged; state: TransportSnapshot }
  | {
      target: "background";
      type: typeof MessageType.TransportDiagnostic;
      diagnostic: RealtimeTransportDiagnostic;
    }
  | { target: "popup"; type: typeof MessageType.CaptureStateChanged; state: CaptureSnapshot };

export type CommandResponse =
  | { ok: true; state: CaptureSnapshot }
  | { ok: false; state: CaptureSnapshot; errorCode: CaptureErrorCode };

export type OffscreenResponse =
  | { ok: true }
  | { ok: false; errorCode: CaptureErrorCode };

export function isTargetedMessage(
  value: unknown,
  target: MessageTarget,
): value is ExtensionMessage {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as { target?: unknown; type?: unknown };
  return candidate.target === target && typeof candidate.type === "string";
}

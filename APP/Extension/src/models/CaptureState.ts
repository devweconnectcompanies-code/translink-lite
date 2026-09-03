import {
  DISCONNECTED_TRANSPORT_STATE,
  type TransportSnapshot,
} from "./TransportState";

export type CaptureStatus = "idle" | "starting" | "capturing" | "stopping" | "error";

export type CaptureErrorCode =
  | "unsupported-tab"
  | "missing-tab"
  | "capture-permission"
  | "stream-acquisition"
  | "audio-context"
  | "tab-closed"
  | "unexpected-termination"
  | "capture-busy"
  | "transport-authentication"
  | "transport-connection"
  | "transport-protocol"
  | "transport-backpressure"
  | "internal-error";

export interface CaptureSnapshot {
  status: CaptureStatus;
  tabId: number | null;
  audioLevel: number;
  hasSignal: boolean;
  errorCode: CaptureErrorCode | null;
  transport: TransportSnapshot;
}

export const IDLE_CAPTURE_STATE: CaptureSnapshot = {
  status: "idle",
  tabId: null,
  audioLevel: 0,
  hasSignal: false,
  errorCode: null,
  transport: { ...DISCONNECTED_TRANSPORT_STATE },
};

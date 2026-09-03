export type TransportStatus =
  | "disconnected"
  | "connecting"
  | "connected"
  | "streaming"
  | "stopping"
  | "error";

export type TransportErrorCode =
  | "authentication-required"
  | "connection-failed"
  | "server-rejected"
  | "protocol-error"
  | "transcription-failed"
  | "backpressure"
  | "connection-closed";

export interface TransportSnapshot {
  status: TransportStatus;
  chunksSent: number;
  bytesSent: number;
  transcriptionActive: boolean;
  partialTranscriptsReceived: number;
  finalTranscriptsReceived: number;
  errorCode: TransportErrorCode | null;
}

export const DISCONNECTED_TRANSPORT_STATE: TransportSnapshot = {
  status: "disconnected",
  chunksSent: 0,
  bytesSent: 0,
  transcriptionActive: false,
  partialTranscriptsReceived: 0,
  finalTranscriptsReceived: 0,
  errorCode: null,
};

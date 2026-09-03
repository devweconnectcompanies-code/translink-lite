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
  | "backpressure"
  | "connection-closed";

export interface TransportSnapshot {
  status: TransportStatus;
  chunksSent: number;
  bytesSent: number;
  errorCode: TransportErrorCode | null;
}

export const DISCONNECTED_TRANSPORT_STATE: TransportSnapshot = {
  status: "disconnected",
  chunksSent: 0,
  bytesSent: 0,
  errorCode: null,
};

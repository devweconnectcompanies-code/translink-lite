export const REALTIME_PROTOCOL_VERSION = 1;
export const BINARY_HEADER_LENGTH = 24;
export const AUDIO_ENCODING = "pcm_s16le";
export const DEFAULT_CHUNK_DURATION_MS = 150;
export const TRANSPORT_SAMPLE_RATE_HZ = 48_000;
export const DEFAULT_REALTIME_ENDPOINT = "ws://localhost:5221/api/realtime/audio";
export const WEBSOCKET_SUBPROTOCOL = "translink.realtime.v1";
export const BEARER_SUBPROTOCOL_PREFIX = "translink.bearer.";

export interface RealtimeEndpointDetails {
  url: URL;
  safeTarget: string;
  developmentDiagnostics: boolean;
}

export function parseRealtimeEndpoint(value: string): RealtimeEndpointDetails {
  const url = new URL(value);
  if (
    !["ws:", "wss:"].includes(url.protocol) ||
    url.username.length > 0 ||
    url.password.length > 0 ||
    url.search.length > 0 ||
    url.hash.length > 0
  ) {
    throw new Error("invalid-realtime-endpoint");
  }

  return {
    url,
    safeTarget: `${url.protocol}//${url.host}${url.pathname}`,
    developmentDiagnostics: ["localhost", "127.0.0.1", "[::1]"].includes(
      url.hostname,
    ),
  };
}

export function sanitizeWebSocketCloseReason(reason: string): string {
  return /^[a-z0-9-]{1,64}$/.test(reason) ? reason : "unavailable";
}

export interface SessionStartMessage {
  type: "session.start";
  protocolVersion: number;
  audio: {
    encoding: typeof AUDIO_ENCODING;
    sampleRateHz: number;
    channelCount: 1;
    chunkDurationMs: number;
  };
}

export interface ServerControlMessage {
  type: "session.accepted" | "session.rejected" | "session.stopped" | "transport.error";
  protocolVersion: number;
  sessionId?: string;
  code?: string;
}

export function createSessionStart(
  sampleRateHz: number,
  chunkDurationMs: number,
): SessionStartMessage {
  return {
    type: "session.start",
    protocolVersion: REALTIME_PROTOCOL_VERSION,
    audio: {
      encoding: AUDIO_ENCODING,
      sampleRateHz,
      channelCount: 1,
      chunkDurationMs,
    },
  };
}

export function parseServerControl(value: unknown): ServerControlMessage | null {
  if (typeof value !== "object" || value === null) return null;
  const candidate = value as Partial<ServerControlMessage>;
  if (
    ![
      "session.accepted",
      "session.rejected",
      "session.stopped",
      "transport.error",
    ].includes(candidate.type ?? "") ||
    candidate.protocolVersion !== REALTIME_PROTOCOL_VERSION
  ) {
    return null;
  }
  return candidate as ServerControlMessage;
}

export function encodePcm16(floatSamples: Float32Array): ArrayBuffer {
  const payload = new ArrayBuffer(floatSamples.length * Int16Array.BYTES_PER_ELEMENT);
  const view = new DataView(payload);
  for (let index = 0; index < floatSamples.length; index += 1) {
    const sample = Math.max(-1, Math.min(1, floatSamples[index] ?? 0));
    const integer = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
    view.setInt16(index * 2, Math.round(integer), true);
  }
  return payload;
}

export function createAudioFrame(
  pcmPayload: ArrayBuffer,
  sequence: bigint,
  elapsedMilliseconds: bigint,
): ArrayBuffer {
  const frame = new ArrayBuffer(BINARY_HEADER_LENGTH + pcmPayload.byteLength);
  const view = new DataView(frame);
  view.setUint8(0, "T".charCodeAt(0));
  view.setUint8(1, "L".charCodeAt(0));
  view.setUint8(2, REALTIME_PROTOCOL_VERSION);
  view.setUint8(3, 0);
  view.setBigUint64(4, sequence, true);
  view.setBigUint64(12, elapsedMilliseconds, true);
  view.setUint32(20, pcmPayload.byteLength, true);
  new Uint8Array(frame, BINARY_HEADER_LENGTH).set(new Uint8Array(pcmPayload));
  return frame;
}

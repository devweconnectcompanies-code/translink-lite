import type { TransportErrorCode, TransportSnapshot } from "../models/TransportState";
import {
  BEARER_SUBPROTOCOL_PREFIX,
  createAudioFrame,
  createSessionStart,
  parseRealtimeEndpoint,
  parseServerControl,
  REALTIME_PROTOCOL_VERSION,
  sanitizeWebSocketCloseReason,
  WEBSOCKET_SUBPROTOCOL,
} from "./protocol";

const CONNECT_TIMEOUT_MS = 10_000;
const STOP_TIMEOUT_MS = 2_000;
const MAX_BUFFERED_CHUNKS = 4;

export interface RealtimeTransportConfiguration {
  endpoint: string;
  accessToken: string;
  chunkDurationMs: number;
}

export interface RealtimeTransportDiagnostic {
  event: string;
  target?: string;
  protocol?: string;
  code?: number | string;
  reason?: string;
  clean?: boolean;
}

export class RealtimeAudioTransport {
  private socket: WebSocket | null = null;
  private sequence = 0n;
  private chunksSent = 0;
  private bytesSent = 0;
  private stopping = false;
  private maxBufferedBytes = 0;
  private lastProgressUpdate = 0;
  private failed = false;

  constructor(
    private readonly configuration: RealtimeTransportConfiguration,
    private readonly onState: (state: TransportSnapshot) => void,
    private readonly onFailure: (errorCode: TransportErrorCode) => void,
    private readonly onDiagnostic: (diagnostic: RealtimeTransportDiagnostic) => void,
  ) {}

  async connect(sampleRateHz: number): Promise<void> {
    this.onState(this.snapshot("connecting"));
    let endpoint;
    try {
      endpoint = parseRealtimeEndpoint(this.configuration.endpoint);
    } catch {
      this.onDiagnostic({ event: "endpoint.invalid" });
      throw new Error("invalid-realtime-endpoint");
    }
    const logDevelopmentEvent = (event: string, details?: object): void => {
      if (!endpoint.developmentDiagnostics) return;
      this.onDiagnostic({
        event,
        target: endpoint.safeTarget,
        ...details,
      });
    };

    await new Promise<void>((resolve, reject) => {
      logDevelopmentEvent("socket.create");
      const socket = new WebSocket(endpoint.url, [
        WEBSOCKET_SUBPROTOCOL,
        `${BEARER_SUBPROTOCOL_PREFIX}${this.configuration.accessToken}`,
      ]);
      this.socket = socket;
      socket.binaryType = "arraybuffer";
      const timeout = setTimeout(() => {
        socket.close(1000, "connect-timeout");
        reject(new Error("connect-timeout"));
      }, CONNECT_TIMEOUT_MS);
      let accepted = false;

      socket.onopen = () => {
        logDevelopmentEvent("socket.open", { protocol: socket.protocol });
        socket.send(JSON.stringify(createSessionStart(
          sampleRateHz,
          this.configuration.chunkDurationMs,
        )));
        logDevelopmentEvent("session.start.sent");
      };
      socket.onmessage = (event) => {
        if (typeof event.data !== "string") {
          clearTimeout(timeout);
          reject(new Error("protocol-error"));
          return;
        }

        let parsed: unknown;
        try {
          parsed = JSON.parse(event.data);
        } catch {
          clearTimeout(timeout);
          reject(new Error("protocol-error"));
          return;
        }

        const message = parseServerControl(parsed);
        if (message?.type === "session.accepted") {
          clearTimeout(timeout);
          accepted = true;
          logDevelopmentEvent("session.accepted");
          this.onState(this.snapshot("connected"));
          resolve();
        } else if (message?.type === "session.rejected") {
          clearTimeout(timeout);
          logDevelopmentEvent("session.rejected", {
            code: sanitizeWebSocketCloseReason(message.code ?? ""),
          });
          reject(new Error("server-rejected"));
        } else if (message?.type === "transport.error") {
          clearTimeout(timeout);
          logDevelopmentEvent("transport.error", {
            code: sanitizeWebSocketCloseReason(message.code ?? ""),
          });
          if (accepted && !this.failed) {
            this.failed = true;
            this.onFailure("protocol-error");
          } else {
            reject(new Error("server-rejected"));
          }
        } else if (message?.type === "session.stopped") {
          socket.close(1000, "session-stopped");
        }
      };
      socket.onerror = () => {
        clearTimeout(timeout);
        logDevelopmentEvent("socket.error");
        reject(new Error("connection-failed"));
      };
      socket.onclose = (event) => {
        clearTimeout(timeout);
        logDevelopmentEvent("socket.close", {
          code: event.code,
          reason: sanitizeWebSocketCloseReason(event.reason),
          clean: event.wasClean,
        });
        if (!accepted) {
          reject(new Error("connection-closed"));
        } else if (!this.stopping && !this.failed) {
          this.failed = true;
          this.onFailure("connection-closed");
        }
      };
    });

    const samplesPerChunk = Math.round(
      sampleRateHz * this.configuration.chunkDurationMs / 1_000,
    );
    this.maxBufferedBytes = samplesPerChunk * 2 * MAX_BUFFERED_CHUNKS;
  }

  send(pcmPayload: ArrayBuffer): void {
    const socket = this.socket;
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    if (socket.bufferedAmount > this.maxBufferedBytes) {
      if (!this.failed) {
        this.failed = true;
        this.onFailure("backpressure");
      }
      return;
    }

    const elapsedMilliseconds =
      this.sequence * BigInt(this.configuration.chunkDurationMs);
    const frame = createAudioFrame(pcmPayload, this.sequence, elapsedMilliseconds);
    socket.send(frame);
    this.sequence += 1n;
    this.chunksSent += 1;
    this.bytesSent += pcmPayload.byteLength;
    const now = performance.now();
    if (this.chunksSent === 1 || now - this.lastProgressUpdate >= 1_000) {
      this.lastProgressUpdate = now;
      this.onState(this.snapshot("streaming"));
    }
  }

  async stop(): Promise<void> {
    this.stopping = true;
    const socket = this.socket;
    if (!socket || socket.readyState > WebSocket.OPEN) return;

    this.onState(this.snapshot("stopping"));
    if (socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({
        type: "session.stop",
        protocolVersion: REALTIME_PROTOCOL_VERSION,
      }));
    }

    await new Promise<void>((resolve) => {
      const timeout = setTimeout(() => {
        socket.close(1000, "client-stop");
        resolve();
      }, STOP_TIMEOUT_MS);
      socket.addEventListener("close", () => {
        clearTimeout(timeout);
        resolve();
      }, { once: true });
    });
  }

  close(): void {
    this.stopping = true;
    this.socket?.close(1000, "capture-ended");
    this.socket = null;
  }

  private snapshot(status: TransportSnapshot["status"]): TransportSnapshot {
    return {
      status,
      chunksSent: this.chunksSent,
      bytesSent: this.bytesSent,
      errorCode: null,
    };
  }
}

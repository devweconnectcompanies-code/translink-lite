import { describe, expect, it } from "vitest";
import {
  AUDIO_ENCODING,
  BINARY_HEADER_LENGTH,
  createAudioFrame,
  createSessionStart,
  DEFAULT_REALTIME_ENDPOINT,
  encodePcm16,
  parseRealtimeEndpoint,
  parseServerControl,
  sanitizeWebSocketCloseReason,
} from "./protocol";

describe("realtime protocol", () => {
  it("creates the versioned session start contract", () => {
    expect(createSessionStart(48_000, 150)).toEqual({
      type: "session.start",
      protocolVersion: 1,
      audio: {
        encoding: AUDIO_ENCODING,
        sampleRateHz: 48_000,
        channelCount: 1,
        chunkDurationMs: 150,
      },
    });
  });

  it("encodes clipped signed PCM16 little endian samples", () => {
    const payload = encodePcm16(new Float32Array([-2, -0.5, 0, 0.5, 2]));
    const view = new DataView(payload);

    expect(view.getInt16(0, true)).toBe(-32_768);
    expect(view.getInt16(2, true)).toBe(-16_384);
    expect(view.getInt16(4, true)).toBe(0);
    expect(view.getInt16(6, true)).toBe(16_384);
    expect(view.getInt16(8, true)).toBe(32_767);
  });

  it("creates an ordered binary frame header", () => {
    const payload = new ArrayBuffer(320);
    const frame = createAudioFrame(payload, 9n, 1_350n);
    const view = new DataView(frame);

    expect(frame.byteLength).toBe(BINARY_HEADER_LENGTH + 320);
    expect(String.fromCharCode(view.getUint8(0), view.getUint8(1))).toBe("TL");
    expect(view.getUint8(2)).toBe(1);
    expect(view.getBigUint64(4, true)).toBe(9n);
    expect(view.getBigUint64(12, true)).toBe(1_350n);
    expect(view.getUint32(20, true)).toBe(320);
  });

  it("rejects unknown or mismatched server controls", () => {
    expect(parseServerControl({ type: "session.accepted", protocolVersion: 1 }))
      .not.toBeNull();
    expect(parseServerControl({ type: "session.accepted", protocolVersion: 2 }))
      .toBeNull();
    expect(parseServerControl({ type: "translation.complete", protocolVersion: 1 }))
      .toBeNull();
  });

  it("targets the API default HTTP development profile", () => {
    expect(DEFAULT_REALTIME_ENDPOINT).toBe(
      "ws://localhost:5221/api/realtime/audio",
    );
    expect(parseRealtimeEndpoint(DEFAULT_REALTIME_ENDPOINT)).toMatchObject({
      safeTarget: "ws://localhost:5221/api/realtime/audio",
      developmentDiagnostics: true,
    });
  });

  it("rejects unsafe endpoint components and sanitizes close reasons", () => {
    expect(() => parseRealtimeEndpoint("https://localhost/api/realtime/audio"))
      .toThrow("invalid-realtime-endpoint");
    expect(() => parseRealtimeEndpoint("ws://user:secret@localhost/audio"))
      .toThrow("invalid-realtime-endpoint");
    expect(() => parseRealtimeEndpoint("ws://localhost/audio?token=secret"))
      .toThrow("invalid-realtime-endpoint");
    expect(sanitizeWebSocketCloseReason("idle-timeout")).toBe("idle-timeout");
    expect(sanitizeWebSocketCloseReason("unsafe details: token-like value"))
      .toBe("unavailable");
  });
});

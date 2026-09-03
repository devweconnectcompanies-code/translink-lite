import type { CaptureErrorCode } from "../models/CaptureState";
import {
  isTargetedMessage,
  MessageType,
  type ExtensionMessage,
  type OffscreenResponse,
} from "../messaging/messages";
import type { TransportErrorCode, TransportSnapshot } from "../models/TransportState";
import {
  RealtimeAudioTransport,
  type RealtimeTransportConfiguration,
  type RealtimeTransportDiagnostic,
} from "../realtime/RealtimeAudioTransport";
import { encodePcm16, TRANSPORT_SAMPLE_RATE_HZ } from "../realtime/protocol";

const LEVEL_UPDATE_INTERVAL_MS = 150;
const LEVEL_FLOOR_DB = -60;
const LEVEL_CEILING_DB = -12;
const SIGNAL_THRESHOLD_DB = -55;

let mediaStream: MediaStream | null = null;
let audioContext: AudioContext | null = null;
let sourceNode: MediaStreamAudioSourceNode | null = null;
let analyserNode: AnalyserNode | null = null;
let analysisSinkNode: GainNode | null = null;
let transportNode: AudioWorkletNode | null = null;
let transportSinkNode: GainNode | null = null;
let realtimeTransport: RealtimeAudioTransport | null = null;
let levelMonitorId: ReturnType<typeof setInterval> | null = null;
let suppressTrackEnd = false;
let resumeInProgress = false;
let smoothedLevel = 0;

async function sendBackgroundEvent(message: ExtensionMessage): Promise<void> {
  try {
    await chrome.runtime.sendMessage(message);
  } catch {
    // The service worker may be restarting; persisted state is reconciled later.
  }
}

function reportTransportState(state: TransportSnapshot): void {
  void sendBackgroundEvent({
    target: "background",
    type: MessageType.TransportStateChanged,
    state,
  });
}

function reportTransportDiagnostic(diagnostic: RealtimeTransportDiagnostic): void {
  console.info("[realtime]", diagnostic);
  void sendBackgroundEvent({
    target: "background",
    type: MessageType.TransportDiagnostic,
    diagnostic,
  });
}

function mapTransportError(errorCode: TransportErrorCode): CaptureErrorCode {
  if (errorCode === "authentication-required") return "transport-authentication";
  if (errorCode === "backpressure") return "transport-backpressure";
  if (errorCode === "protocol-error" || errorCode === "server-rejected")
    return "transport-protocol";
  return "transport-connection";
}

function stopLevelMonitor(): void {
  if (levelMonitorId !== null) {
    clearInterval(levelMonitorId);
    levelMonitorId = null;
  }
}

async function releaseCaptureResources(): Promise<void> {
  stopLevelMonitor();
  suppressTrackEnd = true;

  for (const track of mediaStream?.getTracks() ?? []) {
    track.onended = null;
    track.stop();
  }

  sourceNode?.disconnect();
  analyserNode?.disconnect();
  analysisSinkNode?.disconnect();
  transportNode?.disconnect();
  if (transportNode) transportNode.port.onmessage = null;
  transportSinkNode?.disconnect();
  realtimeTransport?.close();

  if (audioContext && audioContext.state !== "closed") {
    audioContext.onstatechange = null;
    await audioContext.close();
  }

  mediaStream = null;
  audioContext = null;
  sourceNode = null;
  analyserNode = null;
  analysisSinkNode = null;
  transportNode = null;
  transportSinkNode = null;
  realtimeTransport = null;
  resumeInProgress = false;
  smoothedLevel = 0;
}

function startLevelMonitor(analyser: AnalyserNode): void {
  const samples = new Float32Array(analyser.fftSize);

  levelMonitorId = setInterval(() => {
    analyser.getFloatTimeDomainData(samples);

    let sumSquares = 0;
    for (const sample of samples) {
      sumSquares += sample * sample;
    }

    const rms = Math.sqrt(sumSquares / samples.length);
    const levelDb = rms > 0 ? 20 * Math.log10(rms) : Number.NEGATIVE_INFINITY;
    const normalizedLevel = Math.max(
      0,
      Math.min(1, (levelDb - LEVEL_FLOOR_DB) / (LEVEL_CEILING_DB - LEVEL_FLOOR_DB)),
    );
    smoothedLevel = smoothedLevel * 0.55 + normalizedLevel * 0.45;

    void sendBackgroundEvent({
      target: "background",
      type: MessageType.AudioLevel,
      level: smoothedLevel,
      hasSignal: levelDb >= SIGNAL_THRESHOLD_DB,
    });
  }, LEVEL_UPDATE_INTERVAL_MS);
}

async function resumeSuspendedContext(context: AudioContext): Promise<void> {
  if (resumeInProgress || suppressTrackEnd || context !== audioContext) return;

  resumeInProgress = true;
  try {
    await context.resume();
    if (context.state !== "running") {
      await handleUnexpectedTermination("audio-context");
    }
  } catch {
    await handleUnexpectedTermination("audio-context");
  } finally {
    resumeInProgress = false;
  }
}

function mapStreamError(error: unknown): CaptureErrorCode {
  if (error instanceof DOMException) {
    if (error.name === "NotAllowedError") return "capture-permission";
  }
  return "stream-acquisition";
}

async function handleUnexpectedTermination(
  errorCode: CaptureErrorCode = "unexpected-termination",
): Promise<void> {
  if (suppressTrackEnd) return;
  await releaseCaptureResources();
  await sendBackgroundEvent({
    target: "background",
    type: MessageType.CaptureStopped,
    errorCode,
  });
}

async function handleTransportFailure(errorCode: TransportErrorCode): Promise<void> {
  if (suppressTrackEnd) return;
  reportTransportState({
    status: "error",
    chunksSent: 0,
    bytesSent: 0,
    errorCode,
  });
  await handleUnexpectedTermination(mapTransportError(errorCode));
}

async function startCapture(
  tabId: number,
  streamId: string,
  transportConfiguration: RealtimeTransportConfiguration,
): Promise<OffscreenResponse> {
  if (mediaStream !== null) {
    return { ok: false, errorCode: "capture-busy" };
  }

  suppressTrackEnd = false;

  let stream: MediaStream;

  try {
    const audioConstraints = {
      mandatory: {
        chromeMediaSource: "tab",
        chromeMediaSourceId: streamId,
      },
    } as MediaTrackConstraints;

    stream = await navigator.mediaDevices.getUserMedia({
      audio: audioConstraints,
      video: false,
    });
    mediaStream = stream;
  } catch (error) {
    console.warn(
      "[capture] Stream acquisition failed:",
      error instanceof Error ? error.name : "UnknownError",
    );
    await releaseCaptureResources();
    return { ok: false, errorCode: mapStreamError(error) };
  }

  try {
    const audioTrack = stream.getAudioTracks()[0];
    if (!audioTrack) {
      await releaseCaptureResources();
      return { ok: false, errorCode: "stream-acquisition" };
    }

    const context = new AudioContext({
      latencyHint: "interactive",
      sampleRate: TRANSPORT_SAMPLE_RATE_HZ,
    });
    audioContext = context;
    await context.resume();
    if (context.state !== "running") {
      await releaseCaptureResources();
      return { ok: false, errorCode: "audio-context" };
    }
    context.onstatechange = () => {
      if (context.state === "suspended") {
        void resumeSuspendedContext(context);
      }
    };

    const source = context.createMediaStreamSource(stream);
    sourceNode = source;
    const analyser = context.createAnalyser();
    analyserNode = analyser;
    const analysisSink = context.createGain();
    analysisSinkNode = analysisSink;
    analyser.fftSize = 512;
    analyser.smoothingTimeConstant = 0.65;
    analysisSink.gain.value = 0;

    // Keep playback independent from analysis. The silent destination branch
    // keeps the analyser in Chrome's actively processed audio graph.
    source.connect(context.destination);
    source.connect(analyser);
    analyser.connect(analysisSink);
    analysisSink.connect(context.destination);

    const transport = new RealtimeAudioTransport(
      transportConfiguration,
      reportTransportState,
      (errorCode) => void handleTransportFailure(errorCode),
      reportTransportDiagnostic,
    );
    realtimeTransport = transport;
    try {
      await transport.connect(context.sampleRate);
    } catch (error) {
      const category = error instanceof Error ? error.message : "connection-failed";
      console.warn("[realtime] connection.failed", { category });
      const errorCode: TransportErrorCode =
        category === "server-rejected" || category === "protocol-error"
          ? "server-rejected"
          : "connection-failed";
      await releaseCaptureResources();
      return { ok: false, errorCode: mapTransportError(errorCode) };
    }

    await context.audioWorklet.addModule(chrome.runtime.getURL("audio-worklet.js"));
    const chunker = new AudioWorkletNode(context, "translink-pcm-chunker", {
      numberOfInputs: 1,
      numberOfOutputs: 1,
      channelCount: 1,
      outputChannelCount: [1],
      processorOptions: {
        chunkDurationMs: transportConfiguration.chunkDurationMs,
      },
    });
    transportNode = chunker;
    const transportSink = context.createGain();
    transportSinkNode = transportSink;
    transportSink.gain.value = 0;
    chunker.port.onmessage = (event: MessageEvent<unknown>) => {
      if (event.data instanceof Float32Array) {
        transport.send(encodePcm16(event.data));
      }
    };
    source.connect(chunker);
    chunker.connect(transportSink);
    transportSink.connect(context.destination);

    audioTrack.onended = () => void handleUnexpectedTermination();

    startLevelMonitor(analyser);
    await sendBackgroundEvent({
      target: "background",
      type: MessageType.CaptureStarted,
      tabId,
    });
    return { ok: true };
  } catch (error) {
    console.warn(
      "[capture] Audio graph failed:",
      error instanceof Error ? error.name : "UnknownError",
    );
    await releaseCaptureResources();
    return { ok: false, errorCode: "audio-context" };
  }
}

async function stopCapture(): Promise<OffscreenResponse> {
  if (realtimeTransport) {
    await realtimeTransport.stop();
  }
  await releaseCaptureResources();
  return { ok: true };
}

chrome.runtime.onMessage.addListener(
  (
    message: unknown,
    _sender,
    sendResponse: (response: OffscreenResponse) => void,
  ) => {
    if (!isTargetedMessage(message, "offscreen")) return false;

    void (async () => {
      switch (message.type) {
        case MessageType.OffscreenStartCapture:
          sendResponse(await startCapture(
            message.tabId,
            message.streamId,
            message.transport,
          ));
          break;
        case MessageType.OffscreenStopCapture:
          sendResponse(await stopCapture());
          break;
      }
    })();

    return true;
  },
);

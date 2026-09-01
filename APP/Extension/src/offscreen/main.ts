import type { CaptureErrorCode } from "../models/CaptureState";
import {
  isTargetedMessage,
  MessageType,
  type ExtensionMessage,
  type OffscreenResponse,
} from "../messaging/messages";

const LEVEL_UPDATE_INTERVAL_MS = 150;
const LEVEL_FLOOR_DB = -60;
const LEVEL_CEILING_DB = -12;
const SIGNAL_THRESHOLD_DB = -55;

let mediaStream: MediaStream | null = null;
let audioContext: AudioContext | null = null;
let sourceNode: MediaStreamAudioSourceNode | null = null;
let analyserNode: AnalyserNode | null = null;
let analysisSinkNode: GainNode | null = null;
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

  if (audioContext && audioContext.state !== "closed") {
    audioContext.onstatechange = null;
    await audioContext.close();
  }

  mediaStream = null;
  audioContext = null;
  sourceNode = null;
  analyserNode = null;
  analysisSinkNode = null;
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

async function startCapture(tabId: number, streamId: string): Promise<OffscreenResponse> {
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

    const context = new AudioContext({ latencyHint: "interactive" });
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
          sendResponse(await startCapture(message.tabId, message.streamId));
          break;
        case MessageType.OffscreenStopCapture:
          sendResponse(await stopCapture());
          break;
      }
    })();

    return true;
  },
);

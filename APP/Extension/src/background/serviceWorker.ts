import {
  IDLE_CAPTURE_STATE,
  type CaptureErrorCode,
  type CaptureSnapshot,
} from "../models/CaptureState";
import { isSupportedTabUrl } from "../models/tabEligibility";
import type { TransportSnapshot } from "../models/TransportState";
import {
  DEFAULT_CHUNK_DURATION_MS,
  DEFAULT_REALTIME_ENDPOINT,
} from "../realtime/protocol";
import {
  isTargetedMessage,
  MessageType,
  type CommandResponse,
  type ExtensionMessage,
  type OffscreenResponse,
} from "../messaging/messages";

const CAPTURE_STATE_KEY = "captureState";
const OFFSCREEN_DOCUMENT_PATH = "offscreen.html";
const DEVELOPMENT_ACCESS_TOKEN_KEY = "developmentAccessToken";
const REALTIME_ENDPOINT_KEY = "realtimeEndpoint";

let captureState: CaptureSnapshot = { ...IDLE_CAPTURE_STATE };
let stateLoaded = false;

function isTransportSnapshot(value: unknown): value is TransportSnapshot {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Partial<TransportSnapshot>;
  return (
    ["disconnected", "connecting", "connected", "streaming", "stopping", "error"]
      .includes(candidate.status ?? "") &&
    typeof candidate.chunksSent === "number" &&
    Number.isSafeInteger(candidate.chunksSent) &&
    candidate.chunksSent >= 0 &&
    typeof candidate.bytesSent === "number" &&
    Number.isSafeInteger(candidate.bytesSent) &&
    candidate.bytesSent >= 0 &&
    (candidate.errorCode === null || typeof candidate.errorCode === "string")
  );
}

function isCaptureSnapshot(value: unknown): value is CaptureSnapshot {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Partial<CaptureSnapshot>;
  return (
    ["idle", "starting", "capturing", "stopping", "error"].includes(
      candidate.status ?? "",
    ) &&
    (candidate.tabId === null || typeof candidate.tabId === "number") &&
    typeof candidate.audioLevel === "number" &&
    typeof candidate.hasSignal === "boolean" &&
    isTransportSnapshot(candidate.transport)
  );
}

async function loadCaptureState(): Promise<void> {
  if (stateLoaded) return;

  const stored = await chrome.storage.session.get(CAPTURE_STATE_KEY);
  const candidate: unknown = stored[CAPTURE_STATE_KEY];
  captureState = isCaptureSnapshot(candidate)
    ? candidate
    : { ...IDLE_CAPTURE_STATE };
  stateLoaded = true;
}

async function broadcastCaptureState(): Promise<void> {
  const message: ExtensionMessage = {
    target: "popup",
    type: MessageType.CaptureStateChanged,
    state: captureState,
  };

  try {
    await chrome.runtime.sendMessage(message);
  } catch {
    // The popup is normally closed while capture continues.
  }
}

async function setCaptureState(state: CaptureSnapshot): Promise<void> {
  captureState = state;
  stateLoaded = true;
  await chrome.storage.session.set({ [CAPTURE_STATE_KEY]: state });
  await broadcastCaptureState();
}

async function closeOffscreenDocument(): Promise<void> {
  if (await chrome.offscreen.hasDocument()) {
    await chrome.offscreen.closeDocument();
  }
}

async function reconcileCaptureState(): Promise<void> {
  await loadCaptureState();
  const hasOffscreenDocument = await chrome.offscreen.hasDocument();

  if (
    captureState.status === "starting" ||
    captureState.status === "stopping" ||
    (captureState.status === "capturing" && !hasOffscreenDocument)
  ) {
    if (hasOffscreenDocument) await chrome.offscreen.closeDocument();
    await setCaptureState({ ...IDLE_CAPTURE_STATE });
  } else if (captureState.status === "idle" && hasOffscreenDocument) {
    await chrome.offscreen.closeDocument();
  }
}

async function ensureOffscreenDocument(): Promise<void> {
  if (await chrome.offscreen.hasDocument()) return;

  await chrome.offscreen.createDocument({
    url: OFFSCREEN_DOCUMENT_PATH,
    reasons: [chrome.offscreen.Reason.USER_MEDIA],
    justification: "Capture and locally process audio from one user-selected browser tab.",
  });
}

async function getTabStreamId(tabId: number): Promise<string> {
  return new Promise((resolve, reject) => {
    chrome.tabCapture.getMediaStreamId({ targetTabId: tabId }, (streamId) => {
      const errorMessage = chrome.runtime.lastError?.message;
      if (errorMessage || !streamId) {
        reject(new Error(errorMessage ?? "No stream identifier was returned."));
        return;
      }
      resolve(streamId);
    });
  });
}

async function getRealtimeConfiguration() {
  const stored = await chrome.storage.local.get([
    DEVELOPMENT_ACCESS_TOKEN_KEY,
    REALTIME_ENDPOINT_KEY,
  ]);
  const accessToken = stored[DEVELOPMENT_ACCESS_TOKEN_KEY];
  if (typeof accessToken !== "string" || accessToken.trim().length === 0) {
    return null;
  }

  const configuredEndpoint = stored[REALTIME_ENDPOINT_KEY];
  return {
    accessToken: accessToken.trim(),
    endpoint:
      typeof configuredEndpoint === "string" && configuredEndpoint.trim().length > 0
        ? configuredEndpoint.trim()
        : DEFAULT_REALTIME_ENDPOINT,
    chunkDurationMs: DEFAULT_CHUNK_DURATION_MS,
  };
}

async function sendToOffscreen(message: ExtensionMessage): Promise<OffscreenResponse> {
  try {
    const response: unknown = await chrome.runtime.sendMessage(message);
    if (typeof response === "object" && response !== null && "ok" in response) {
      return response as OffscreenResponse;
    }
  } catch {
    // A safe failure is returned below.
  }
  return { ok: false, errorCode: "internal-error" };
}

async function failCapture(errorCode: CaptureErrorCode): Promise<CommandResponse> {
  try {
    await closeOffscreenDocument();
  } catch {
    // State still transitions to a safe error if cleanup itself fails.
  }

  const transportError = errorCode.startsWith("transport-");
  const state: CaptureSnapshot = {
    status: "error",
    tabId: null,
    audioLevel: 0,
    hasSignal: false,
    errorCode,
    transport: transportError
      ? {
          ...captureState.transport,
          status: "error",
          errorCode:
            errorCode === "transport-authentication"
              ? "authentication-required"
              : errorCode === "transport-backpressure"
                ? "backpressure"
                : errorCode === "transport-protocol"
                  ? "protocol-error"
                  : "connection-failed",
        }
      : {
          status: "disconnected",
          chunksSent: captureState.transport.chunksSent,
          bytesSent: captureState.transport.bytesSent,
          errorCode: null,
        },
  };
  await setCaptureState(state);
  return { ok: false, state, errorCode };
}

async function startCapture(tabId: number): Promise<CommandResponse> {
  await reconcileCaptureState();

  if (["starting", "capturing", "stopping"].includes(captureState.status)) {
    return { ok: false, state: captureState, errorCode: "capture-busy" };
  }

  let tab: chrome.tabs.Tab;
  try {
    tab = await chrome.tabs.get(tabId);
  } catch {
    return failCapture("missing-tab");
  }

  if (!isSupportedTabUrl(tab.url)) {
    return failCapture("unsupported-tab");
  }


  const transport = await getRealtimeConfiguration();
  if (!transport) {
    return failCapture("transport-authentication");
  }

  await setCaptureState({
    status: "starting",
    tabId,
    audioLevel: 0,
    hasSignal: false,
    errorCode: null,
    transport: {
      status: "connecting",
      chunksSent: 0,
      bytesSent: 0,
      errorCode: null,
    },
  });

  try {
    await ensureOffscreenDocument();
    const streamId = await getTabStreamId(tabId);
    const response = await sendToOffscreen({
      target: "offscreen",
      type: MessageType.OffscreenStartCapture,
      tabId,
      streamId,
      transport,
    });

    if (!response.ok) return failCapture(response.errorCode);

    const state: CaptureSnapshot = {
      status: "capturing",
      tabId,
      audioLevel: 0,
      hasSignal: false,
      errorCode: null,
      transport: captureState.transport,
    };
    await setCaptureState(state);
    return { ok: true, state };
  } catch (error) {
    console.warn(
      "[capture] Start failed:",
      error instanceof Error ? error.name : "UnknownError",
    );
    return failCapture("capture-permission");
  }
}

async function stopCapture(): Promise<CommandResponse> {
  await reconcileCaptureState();

  if (captureState.status === "idle") {
    return { ok: true, state: captureState };
  }

  if (captureState.status === "error") {
    await closeOffscreenDocument();
    await setCaptureState({ ...IDLE_CAPTURE_STATE });
    return { ok: true, state: captureState };
  }

  await setCaptureState({
    ...captureState,
    status: "stopping",
    audioLevel: 0,
    hasSignal: false,
    transport: { ...captureState.transport, status: "stopping" },
  });
  const response = await sendToOffscreen({
    target: "offscreen",
    type: MessageType.OffscreenStopCapture,
  });

  try {
    await closeOffscreenDocument();
  } catch {
    // The offscreen document may already have closed after track termination.
  }

  if (!response.ok) return failCapture(response.errorCode);

  await setCaptureState({ ...IDLE_CAPTURE_STATE });
  return { ok: true, state: captureState };
}

async function handleOffscreenEvent(message: ExtensionMessage): Promise<void> {
  await loadCaptureState();

  switch (message.type) {
    case MessageType.CaptureStarted:
      if (captureState.tabId === message.tabId) {
        await setCaptureState({ ...captureState, status: "capturing", errorCode: null });
      }
      break;
    case MessageType.AudioLevel:
      if (captureState.status === "capturing") {
        captureState = {
          ...captureState,
          audioLevel: Math.max(0, Math.min(1, message.level)),
          hasSignal: message.hasSignal,
        };
        await broadcastCaptureState();
      }
      break;
    case MessageType.TransportStateChanged:
      if (captureState.status === "starting" || captureState.status === "capturing") {
        captureState = { ...captureState, transport: message.state };
        await broadcastCaptureState();
      }
      break;
    case MessageType.TransportDiagnostic:
      console.info("[realtime]", message.diagnostic);
      break;
    case MessageType.CaptureStopped:
      if (message.errorCode) {
        await failCapture(message.errorCode);
      } else {
        await setCaptureState({ ...IDLE_CAPTURE_STATE });
      }
      break;
    case MessageType.CaptureError:
      await failCapture(message.errorCode);
      break;
  }
}

chrome.runtime.onMessage.addListener(
  (message: unknown, _sender, sendResponse: (response: CommandResponse) => void) => {
    if (!isTargetedMessage(message, "background")) return false;

    void (async () => {
      switch (message.type) {
        case MessageType.GetCaptureState:
          await reconcileCaptureState();
          sendResponse({ ok: true, state: captureState });
          break;
        case MessageType.StartCapture:
          sendResponse(await startCapture(message.tabId));
          break;
        case MessageType.StopCapture:
          sendResponse(await stopCapture());
          break;
        default:
          await handleOffscreenEvent(message);
          sendResponse({ ok: true, state: captureState });
      }
    })();

    return true;
  },
);

chrome.tabs.onRemoved.addListener((tabId) => {
  void (async () => {
    await loadCaptureState();
    if (captureState.tabId === tabId && captureState.status !== "idle") {
      await failCapture("tab-closed");
    }
  })();
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  void (async () => {
    await loadCaptureState();
    if (
      captureState.tabId === tabId &&
      changeInfo.url !== undefined &&
      !isSupportedTabUrl(tab.url)
    ) {
      await failCapture("unsupported-tab");
    }
  })();
});

chrome.runtime.onStartup.addListener(() => void reconcileCaptureState());
chrome.runtime.onInstalled.addListener(() => {
  void (async () => {
    await closeOffscreenDocument();
    await setCaptureState({ ...IDLE_CAPTURE_STATE });
  })();
});

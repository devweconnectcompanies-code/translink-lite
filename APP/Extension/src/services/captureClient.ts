import {
  IDLE_CAPTURE_STATE,
  type CaptureSnapshot,
} from "../models/CaptureState";
import {
  isTargetedMessage,
  MessageType,
  type CommandResponse,
  type ExtensionMessage,
} from "../messaging/messages";

function hasExtensionRuntime(): boolean {
  return typeof chrome !== "undefined" && chrome.runtime?.id !== undefined;
}

async function sendCommand(message: ExtensionMessage): Promise<CommandResponse> {
  if (!hasExtensionRuntime()) {
    return { ok: true, state: IDLE_CAPTURE_STATE };
  }

  try {
    const response: unknown = await chrome.runtime.sendMessage(message);
    if (
      typeof response === "object" &&
      response !== null &&
      "ok" in response &&
      "state" in response
    ) {
      return response as CommandResponse;
    }
  } catch {
    // A safe internal error is returned below.
  }

  return {
    ok: false,
    state: { ...IDLE_CAPTURE_STATE, status: "error", errorCode: "internal-error" },
    errorCode: "internal-error",
  };
}

export async function getCaptureState(): Promise<CommandResponse> {
  return sendCommand({ target: "background", type: MessageType.GetCaptureState });
}

export async function startCapture(tabId: number): Promise<CommandResponse> {
  return sendCommand({ target: "background", type: MessageType.StartCapture, tabId });
}

export async function stopCapture(): Promise<CommandResponse> {
  return sendCommand({ target: "background", type: MessageType.StopCapture });
}

export function subscribeToCaptureState(
  listener: (state: CaptureSnapshot) => void,
): () => void {
  if (!hasExtensionRuntime()) return () => undefined;

  const handleMessage = (message: unknown) => {
    if (
      isTargetedMessage(message, "popup") &&
      message.type === MessageType.CaptureStateChanged
    ) {
      listener(message.state);
    }
  };

  chrome.runtime.onMessage.addListener(handleMessage);
  return () => chrome.runtime.onMessage.removeListener(handleMessage);
}

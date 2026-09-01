import { useCallback, useEffect, useRef, useState } from "react";
import { TabCard } from "./components/TabCard";
import { CaptureControl } from "./components/CaptureControl";
import type { TabListState } from "./models/BrowserTab";
import { IDLE_CAPTURE_STATE, type CaptureSnapshot } from "./models/CaptureState";
import {
  getCaptureState,
  startCapture,
  stopCapture,
  subscribeToCaptureState,
} from "./services/captureClient";
import {
  ChromeTabsUnavailableError,
  loadSelectedTabId,
  queryBrowserTabs,
  saveSelectedTabId,
  subscribeToTabChanges,
} from "./services/chromeTabs";

function getSafeErrorMessage(error: unknown): string {
  if (error instanceof ChromeTabsUnavailableError) return error.message;
  return "Tabs could not be loaded. Close and reopen the popup, then try again.";
}

export default function App() {
  const [state, setState] = useState<TabListState>({ status: "loading" });
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const [captureState, setCaptureState] = useState<CaptureSnapshot>(IDLE_CAPTURE_STATE);
  const [captureReady, setCaptureReady] = useState(false);
  const refreshSequence = useRef(0);

  const refreshTabs = useCallback(async () => {
    const sequence = ++refreshSequence.current;
    setSelectionError(null);

    try {
      const [tabs, storedTabId] = await Promise.all([
        queryBrowserTabs(),
        loadSelectedTabId(),
      ]);

      if (sequence !== refreshSequence.current) return;

      if (tabs.length === 0) {
        if (storedTabId !== null) {
          await saveSelectedTabId(null);
        }
        setState({ status: "empty" });
        return;
      }

      const selectedTabId = tabs.some(
        (tab) => tab.id === storedTabId && tab.supported,
      )
        ? storedTabId
        : null;

      if (storedTabId !== null && selectedTabId === null) {
        await saveSelectedTabId(null);
      }

      setState({ status: "ready", tabs, selectedTabId });
    } catch (error) {
      if (sequence === refreshSequence.current) {
        setState({ status: "error", message: getSafeErrorMessage(error) });
      }
    }
  }, []);

  useEffect(() => {
    void refreshTabs();

    let refreshTimer: ReturnType<typeof setTimeout> | undefined;
    const unsubscribe = subscribeToTabChanges(() => {
      clearTimeout(refreshTimer);
      refreshTimer = setTimeout(() => void refreshTabs(), 150);
    });

    return () => {
      clearTimeout(refreshTimer);
      unsubscribe();
    };
  }, [refreshTabs]);

  useEffect(() => {
    const unsubscribe = subscribeToCaptureState(setCaptureState);
    void getCaptureState().then((response) => {
      setCaptureState(response.state);
      setCaptureReady(true);
    });
    return unsubscribe;
  }, []);

  const selectionLocked = ["starting", "capturing", "stopping"].includes(
    captureState.status,
  );

  const selectTab = async (tabId: number) => {
    if (state.status !== "ready" || selectionLocked) return;

    const tab = state.tabs.find((candidate) => candidate.id === tabId);
    if (!tab?.supported) return;

    setState({ ...state, selectedTabId: tabId });
    setSelectionError(null);

    try {
      await saveSelectedTabId(tabId);
    } catch {
      setSelectionError("The selection could not be saved. You can still continue in this popup.");
    }
  };

  const effectiveSelectedTabId =
    selectionLocked && captureState.tabId !== null
      ? captureState.tabId
      : state.status === "ready"
        ? state.selectedTabId
        : null;

  const selectedTab =
    state.status === "ready"
      ? state.tabs.find((tab) => tab.id === effectiveSelectedTabId) ?? null
      : null;

  const beginCapture = async () => {
    if (!selectedTab) return;
    setCaptureState({
      status: "starting",
      tabId: selectedTab.id,
      audioLevel: 0,
      hasSignal: false,
      errorCode: null,
    });
    const response = await startCapture(selectedTab.id);
    setCaptureState(response.state);
  };

  const endCapture = async () => {
    setCaptureState({ ...captureState, status: "stopping", audioLevel: 0, hasSignal: false });
    const response = await stopCapture();
    setCaptureState(response.state);
  };

  return (
    <main className="popup-shell">
      <header className="header">
        <div className="brand-mark" aria-hidden="true">TL</div>
        <div>
          <p className="eyebrow">Browser extension</p>
          <h1>TransLink Lite</h1>
        </div>
        <span className="status-pill"><span aria-hidden="true" />Foundation</span>
      </header>

      <section className="content" aria-labelledby="tabs-heading">
        <div className="section-heading">
          <div>
            <p className="step-label">Step 1</p>
            <h2 id="tabs-heading">Select a tab</h2>
            <p>Choose the browser tab you want to translate later.</p>
          </div>
          <button
            className="refresh-button"
            type="button"
            onClick={() => void refreshTabs()}
            disabled={state.status === "loading"}
            aria-label="Refresh open tabs"
          >
            Refresh
          </button>
        </div>

        <div className="tab-panel" aria-live="polite" aria-busy={state.status === "loading"}>
          {state.status === "loading" && (
            <div className="message-state">
              <span className="spinner" aria-hidden="true" />
              <p>Discovering open tabs…</p>
            </div>
          )}

          {state.status === "empty" && (
            <div className="message-state">
              <strong>No tabs found</strong>
              <p>Open a browser tab and refresh this list.</p>
            </div>
          )}

          {state.status === "error" && (
            <div className="message-state message-state--error" role="alert">
              <strong>Tab discovery unavailable</strong>
              <p>{state.message}</p>
            </div>
          )}

          {state.status === "ready" && (
            <ul className="tab-list" aria-label="Open browser tabs">
              {state.tabs.map((tab) => (
                <TabCard
                  key={tab.id}
                  tab={tab}
                  selected={tab.id === effectiveSelectedTabId}
                  selectionLocked={selectionLocked}
                  onSelect={(tabId) => void selectTab(tabId)}
                />
              ))}
            </ul>
          )}
        </div>

        {selectionError && <p className="inline-error" role="alert">{selectionError}</p>}
      </section>

      <footer className="footer">
        <div className="selection-summary">
          <span>Selected tab</span>
          <strong>{selectedTab?.title ?? "None selected"}</strong>
        </div>
        <CaptureControl
          capture={captureState}
          selectedTab={selectedTab}
          ready={captureReady}
          onStart={() => void beginCapture()}
          onStop={() => void endCapture()}
        />
      </footer>
    </main>
  );
}

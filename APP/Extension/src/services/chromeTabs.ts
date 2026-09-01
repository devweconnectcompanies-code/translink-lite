import type { BrowserTab } from "../models/BrowserTab";
import { isSupportedTabUrl } from "../models/tabEligibility";

const SELECTED_TAB_KEY = "selectedTabId";

export class ChromeTabsUnavailableError extends Error {
  constructor() {
    super("Open TransLink Lite from its Chrome extension popup to discover tabs.");
    this.name = "ChromeTabsUnavailableError";
  }
}

function hasChromeTabsApi(): boolean {
  return typeof chrome !== "undefined" && chrome.tabs !== undefined;
}

function hasChromeStorageApi(): boolean {
  return typeof chrome !== "undefined" && chrome.storage?.local !== undefined;
}

function mapTab(tab: chrome.tabs.Tab): BrowserTab | null {
  if (tab.id === undefined) return null;

  return {
    id: tab.id,
    title: tab.title?.trim() || "Untitled tab",
    url: tab.url ?? null,
    favIconUrl: tab.favIconUrl ?? null,
    windowId: tab.windowId,
    active: tab.active,
    supported: isSupportedTabUrl(tab.url),
  };
}

function getRuntimeErrorMessage(): string | null {
  return chrome.runtime.lastError?.message ?? null;
}

export async function queryBrowserTabs(): Promise<BrowserTab[]> {
  if (!hasChromeTabsApi()) throw new ChromeTabsUnavailableError();

  const tabs = await new Promise<chrome.tabs.Tab[]>((resolve, reject) => {
    chrome.tabs.query({}, (result) => {
      const errorMessage = getRuntimeErrorMessage();
      if (errorMessage) {
        reject(new Error(errorMessage));
        return;
      }
      resolve(result);
    });
  });

  return tabs
    .map(mapTab)
    .filter((tab): tab is BrowserTab => tab !== null)
    .sort((left, right) => {
      if (left.windowId !== right.windowId) return left.windowId - right.windowId;
      if (left.active !== right.active) return left.active ? -1 : 1;
      return left.title.localeCompare(right.title);
    });
}

export async function loadSelectedTabId(): Promise<number | null> {
  if (!hasChromeStorageApi()) return null;

  const result = await chrome.storage.local.get(SELECTED_TAB_KEY);
  const value: unknown = result[SELECTED_TAB_KEY];
  return typeof value === "number" ? value : null;
}

export async function saveSelectedTabId(tabId: number | null): Promise<void> {
  if (!hasChromeStorageApi()) return;

  if (tabId === null) {
    await chrome.storage.local.remove(SELECTED_TAB_KEY);
    return;
  }

  await chrome.storage.local.set({ [SELECTED_TAB_KEY]: tabId });
}

export function subscribeToTabChanges(onChange: () => void): () => void {
  if (!hasChromeTabsApi()) return () => undefined;

  const handleUpdated = () => onChange();
  const handleCreated = () => onChange();
  const handleRemoved = () => onChange();
  const handleAttached = () => onChange();
  const handleDetached = () => onChange();

  chrome.tabs.onUpdated.addListener(handleUpdated);
  chrome.tabs.onCreated.addListener(handleCreated);
  chrome.tabs.onRemoved.addListener(handleRemoved);
  chrome.tabs.onAttached.addListener(handleAttached);
  chrome.tabs.onDetached.addListener(handleDetached);

  return () => {
    chrome.tabs.onUpdated.removeListener(handleUpdated);
    chrome.tabs.onCreated.removeListener(handleCreated);
    chrome.tabs.onRemoved.removeListener(handleRemoved);
    chrome.tabs.onAttached.removeListener(handleAttached);
    chrome.tabs.onDetached.removeListener(handleDetached);
  };
}

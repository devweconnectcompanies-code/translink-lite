export interface BrowserTab {
  id: number;
  title: string;
  url: string | null;
  favIconUrl: string | null;
  windowId: number;
  active: boolean;
  supported: boolean;
}

export type TabListState =
  | { status: "loading" }
  | { status: "empty" }
  | { status: "error"; message: string }
  | {
      status: "ready";
      tabs: BrowserTab[];
      selectedTabId: number | null;
    };

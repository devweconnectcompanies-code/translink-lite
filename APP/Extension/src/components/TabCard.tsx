import { useState } from "react";
import type { BrowserTab } from "../models/BrowserTab";

interface TabCardProps {
  tab: BrowserTab;
  selected: boolean;
  selectionLocked: boolean;
  onSelect: (tabId: number) => void;
}

function getDisplayUrl(url: string | null): string {
  if (!url) return "URL unavailable";

  try {
    const parsedUrl = new URL(url);
    return parsedUrl.hostname || parsedUrl.protocol;
  } catch {
    return "URL unavailable";
  }
}

export function TabCard({ tab, selected, selectionLocked, onSelect }: TabCardProps) {
  const [faviconFailed, setFaviconFailed] = useState(false);
  const showFavicon = tab.favIconUrl !== null && !faviconFailed;

  return (
    <li>
      <button
        className={`tab-card${selected ? " tab-card--selected" : ""}`}
        type="button"
        disabled={!tab.supported || selectionLocked}
        aria-pressed={selected}
        aria-label={`${selected ? "Selected: " : "Select "}${tab.title}`}
        onClick={() => onSelect(tab.id)}
      >
        <span className="tab-card__icon" aria-hidden="true">
          {showFavicon ? (
            <img
              src={tab.favIconUrl ?? undefined}
              alt=""
              onError={() => setFaviconFailed(true)}
            />
          ) : (
            <span>{tab.title.slice(0, 1).toUpperCase()}</span>
          )}
        </span>
        <span className="tab-card__content">
          <span className="tab-card__title">{tab.title}</span>
          <span className="tab-card__url">{getDisplayUrl(tab.url)}</span>
        </span>
        <span className="tab-card__state" aria-hidden="true">
          {!tab.supported ? "Unavailable" : selected ? "Selected ✓" : "Select"}
        </span>
      </button>
    </li>
  );
}

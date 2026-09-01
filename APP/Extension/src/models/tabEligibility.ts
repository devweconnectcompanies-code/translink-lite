const SUPPORTED_PROTOCOLS = new Set(["http:", "https:"]);

export function isSupportedTabUrl(url: string | undefined): boolean {
  if (!url) return false;

  try {
    return SUPPORTED_PROTOCOLS.has(new URL(url).protocol);
  } catch {
    return false;
  }
}

import type { CloudServiceAvailabilityMode } from "../../api/types";

export interface ReadOnlyBannerProps {
  readonly mode: CloudServiceAvailabilityMode;
}

const MESSAGE_BY_MODE: Partial<Record<CloudServiceAvailabilityMode, string>> = {
  ReadOnly:
    "The Cloud database is temporarily unavailable, so this session is read-only: browsing continues from cached data, but no changes can be saved right now.",
  VersionIncompatible: "This server is running an incompatible version. Every action is temporarily unavailable.",
  WorldBoundaryUnavailable:
    "The ACE world server is temporarily offline. Everything except withdrawal creation and redemption continues normally.",
};

export function ReadOnlyBanner({ mode }: ReadOnlyBannerProps) {
  const message = MESSAGE_BY_MODE[mode];
  if (!message) {
    return null;
  }

  return (
    <div role="status" className="read-only-banner">
      {message}
    </div>
  );
}

import { useEffect, useState } from "react";

/**
 * AUTH-007's "approximately 10-second delayed accept control" for a destructive action (linking,
 * unlinking): the returned `ready` flag stays false for `delayMs` after `active` first becomes
 * true, so a confirmation button bound to it cannot be clicked the instant the warning appears.
 * Resets whenever `active` goes back to false (the dialog closes/reopens).
 */
export function useDelayedConfirmation(active: boolean, delayMs = 10000): boolean {
  const [ready, setReady] = useState(false);

  useEffect(() => {
    if (!active) {
      setReady(false);
      return;
    }

    const timer = setTimeout(() => setReady(true), delayMs);
    return () => clearTimeout(timer);
  }, [active, delayMs]);

  return ready;
}

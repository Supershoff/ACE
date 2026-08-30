import { vi } from "vitest";

/** Makes `window.matchMedia(query)` report a match for every query in `matchingQueries`. */
export function mockMatchMedia(matchingQueries: readonly string[]) {
  const listenersByQuery = new Map<string, Set<(event: { matches: boolean }) => void>>();

  const matchMedia = vi.fn((query: string) => {
    const matches = matchingQueries.some((candidate) => query.includes(candidate));
    return {
      matches,
      media: query,
      onchange: null,
      addEventListener: (_type: string, listener: (event: { matches: boolean }) => void) => {
        const listeners = listenersByQuery.get(query) ?? new Set();
        listeners.add(listener);
        listenersByQuery.set(query, listeners);
      },
      removeEventListener: (_type: string, listener: (event: { matches: boolean }) => void) => {
        listenersByQuery.get(query)?.delete(listener);
      },
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    };
  });

  vi.stubGlobal("matchMedia", matchMedia);
  return { matchMedia };
}

import { useEffect, useState } from "react";
import { breakpointTokens } from "../design-system/tokens";

const NARROW_VIEWPORT_QUERY = `(max-width: ${breakpointTokens.narrowMaxWidth})`;

export function useIsNarrowViewport(): boolean {
  const [isNarrow, setIsNarrow] = useState(() => window.matchMedia(NARROW_VIEWPORT_QUERY).matches);

  useEffect(() => {
    const mediaQueryList = window.matchMedia(NARROW_VIEWPORT_QUERY);
    const handleChange = (event: { matches: boolean }) => setIsNarrow(event.matches);

    mediaQueryList.addEventListener("change", handleChange);
    setIsNarrow(mediaQueryList.matches);

    return () => mediaQueryList.removeEventListener("change", handleChange);
  }, []);

  return isNarrow;
}

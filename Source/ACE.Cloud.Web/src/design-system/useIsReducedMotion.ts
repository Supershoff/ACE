import { useEffect, useState } from "react";

const QUERY = "(prefers-reduced-motion: reduce)";

export function useIsReducedMotion(): boolean {
  const [prefersReducedMotion, setPrefersReducedMotion] = useState(() => window.matchMedia(QUERY).matches);

  useEffect(() => {
    const mediaQueryList = window.matchMedia(QUERY);
    const handleChange = (event: { matches: boolean }) => setPrefersReducedMotion(event.matches);

    mediaQueryList.addEventListener("change", handleChange);
    setPrefersReducedMotion(mediaQueryList.matches);

    return () => mediaQueryList.removeEventListener("change", handleChange);
  }, []);

  return prefersReducedMotion;
}

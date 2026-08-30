import { useIsReducedMotion } from "../useIsReducedMotion";

export interface LoadingStateProps {
  readonly label?: string;
}

export function LoadingState({ label = "Loading…" }: LoadingStateProps) {
  const isReducedMotion = useIsReducedMotion();
  const spinnerClassName = ["loading-state__spinner", !isReducedMotion && "loading-state__spinner--spin"]
    .filter(Boolean)
    .join(" ");

  return (
    <div role="status" aria-live="polite" className="loading-state">
      <span data-testid="loading-spinner" className={spinnerClassName} aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}

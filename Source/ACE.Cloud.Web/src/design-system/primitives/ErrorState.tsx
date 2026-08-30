import { Button } from "./Button";

export interface ErrorStateProps {
  readonly title: string;
  readonly description?: string;
  readonly onRetry?: () => void;
}

export function ErrorState({ title, description, onRetry }: ErrorStateProps) {
  return (
    <div role="alert" className="error-state">
      <p className="error-state__title">{title}</p>
      {description ? <p className="error-state__description">{description}</p> : null}
      {onRetry ? (
        <Button variant="secondary" onClick={onRetry}>
          Try again
        </Button>
      ) : null}
    </div>
  );
}

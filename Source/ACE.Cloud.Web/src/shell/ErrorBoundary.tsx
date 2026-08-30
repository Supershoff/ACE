import { Component, type ReactNode } from "react";
import { ErrorState } from "../design-system/primitives/ErrorState";

export interface ErrorBoundaryProps {
  readonly children: ReactNode;
}

interface ErrorBoundaryState {
  readonly error: Error | null;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error): void {
    // eslint-disable-next-line no-console
    console.error("AppShell caught a rendering error:", error);
  }

  private handleReset = () => {
    this.setState({ error: null });
  };

  render(): ReactNode {
    if (this.state.error) {
      return (
        <ErrorState
          title="Something went wrong displaying this page."
          description="Try again, or reload the page if the problem continues."
          onRetry={this.handleReset}
        />
      );
    }

    return this.props.children;
  }
}

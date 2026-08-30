import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { EmptyState } from "./EmptyState";
import { ErrorState } from "./ErrorState";
import { LoadingState } from "./LoadingState";
import { ReadOnlyBanner } from "./ReadOnlyBanner";

describe("LoadingState", () => {
  it("announces itself politely to assistive technology", async () => {
    const { container } = render(<LoadingState label="Loading inventory…" />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveTextContent("Loading inventory…");
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("EmptyState", () => {
  it("renders a title and optional description", async () => {
    const { container } = render(<EmptyState title="No items here yet" description="Deposit an item to get started." />);
    expect(screen.getByText("No items here yet")).toBeInTheDocument();
    expect(screen.getByText("Deposit an item to get started.")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("ErrorState", () => {
  it("announces itself as an alert and offers a retry action", async () => {
    const onRetry = vi.fn();
    const user = userEvent.setup();
    const { container } = render(<ErrorState title="Could not load inventory" onRetry={onRetry} />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Could not load inventory");
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getByRole("button", { name: /try again/i }));
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it("omits the retry control when no retry handler is given", () => {
    render(<ErrorState title="Could not load inventory" />);
    expect(screen.queryByRole("button", { name: /try again/i })).not.toBeInTheDocument();
  });
});

describe("ReadOnlyBanner", () => {
  it("explains a ReadOnly database outage", async () => {
    const { container } = render(<ReadOnlyBanner mode="ReadOnly" />);
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent(/read-only/i);
    expect(await axe(container)).toHaveNoViolations();
  });

  it("explains a WorldBoundaryUnavailable outage without implying every feature is down", () => {
    render(<ReadOnlyBanner mode="WorldBoundaryUnavailable" />);
    expect(screen.getByRole("status")).toHaveTextContent(/withdrawal/i);
  });

  it("renders nothing when the service is Operational", () => {
    render(<ReadOnlyBanner mode="Operational" />);
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});

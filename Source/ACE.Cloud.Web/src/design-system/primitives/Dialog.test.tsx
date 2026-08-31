import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { Dialog } from "./Dialog";

function Harness({ open, onClose = vi.fn() }: { open: boolean; onClose?: () => void }) {
  return (
    <div>
      <button>trigger</button>
      <Dialog open={open} onClose={onClose} titleId="dialog-title" title="Confirm withdrawal">
        <button>first action</button>
        <button>second action</button>
      </Dialog>
    </div>
  );
}

describe("Dialog", () => {
  it("renders nothing when closed", () => {
    render(<Harness open={false} />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("exposes an accessible modal dialog labelled by its title", () => {
    render(<Harness open={true} />);
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAttribute("aria-labelledby", "dialog-title");
    expect(screen.getByText("Confirm withdrawal").id).toBe("dialog-title");
  });

  it("has no detectable axe violations while open", async () => {
    const { container } = render(<Harness open={true} />);
    expect(await axe(container)).toHaveNoViolations();
  });

  it("moves focus inside the dialog when it opens", () => {
    render(<Harness open={true} />);
    expect(screen.getByRole("button", { name: "first action" })).toHaveFocus();
  });

  it("closes on Escape", async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(<Harness open={true} onClose={onClose} />);

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalled();
  });

  it("closes when the overlay (outside the dialog content) is clicked", async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(<Harness open={true} onClose={onClose} />);

    await user.click(screen.getByTestId("dialog-overlay"));

    expect(onClose).toHaveBeenCalled();
  });

  it("does not close when content inside the dialog is clicked", async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(<Harness open={true} onClose={onClose} />);

    await user.click(screen.getByRole("button", { name: "first action" }));

    expect(onClose).not.toHaveBeenCalled();
  });

  it("traps Tab focus within the dialog, wrapping from the last to the first focusable element", async () => {
    const user = userEvent.setup();
    render(<Harness open={true} />);

    await user.tab();
    expect(screen.getByRole("button", { name: "second action" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "first action" })).toHaveFocus();
  });

  it("wraps Shift+Tab from the first focusable element to the last", async () => {
    const user = userEvent.setup();
    render(<Harness open={true} />);

    await user.tab({ shift: true });
    expect(screen.getByRole("button", { name: "second action" })).toHaveFocus();
  });

  it("applies optional per-instance style/titleStyle without affecting consumers that omit them", () => {
    render(
      <Dialog
        open={true}
        onClose={() => {}}
        titleId="styled-title"
        title="Styled dialog"
        style={{ backgroundColor: "rgb(1, 2, 3)" }}
        titleStyle={{ color: "rgb(4, 5, 6)" }}
      >
        <p>content</p>
      </Dialog>,
    );

    expect(screen.getByRole("dialog")).toHaveStyle({ backgroundColor: "rgb(1, 2, 3)" });
    expect(screen.getByText("Styled dialog")).toHaveStyle({ color: "rgb(4, 5, 6)" });
  });

  it("restores focus to the previously focused element after closing", () => {
    const { rerender } = render(<Harness open={false} />);
    screen.getByText("trigger").focus();
    expect(screen.getByText("trigger")).toHaveFocus();

    rerender(<Harness open={true} />);
    expect(screen.getByText("trigger")).not.toHaveFocus();

    rerender(<Harness open={false} />);
    expect(screen.getByText("trigger")).toHaveFocus();
  });
});

import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Button } from "./Button";

describe("Button", () => {
  it("renders its label and responds to activation", async () => {
    const onClick = vi.fn();
    const user = userEvent.setup();
    render(<Button onClick={onClick}>Deposit</Button>);

    await user.click(screen.getByRole("button", { name: "Deposit" }));

    expect(onClick).toHaveBeenCalledOnce();
  });

  it("defaults to type=button so it never submits an enclosing form by accident", () => {
    render(<Button>Deposit</Button>);
    expect(screen.getByRole("button")).toHaveAttribute("type", "button");
  });

  it("does not fire onClick while disabled", async () => {
    const onClick = vi.fn();
    const user = userEvent.setup();
    render(
      <Button onClick={onClick} disabled>
        Deposit
      </Button>,
    );

    await user.click(screen.getByRole("button", { name: "Deposit" }));

    expect(onClick).not.toHaveBeenCalled();
  });

  it("applies the shared minimum touch-target size token", () => {
    render(<Button>Deposit</Button>);
    const button = screen.getByRole("button");
    expect(button.style.minHeight).toBe("var(--touch-target-min-size)");
    expect(button.style.minWidth).toBe("var(--touch-target-min-size)");
  });

  it("supports a danger variant for destructive actions", () => {
    render(<Button variant="danger">Cancel listing</Button>);
    expect(screen.getByRole("button")).toHaveClass("button--danger");
  });
});

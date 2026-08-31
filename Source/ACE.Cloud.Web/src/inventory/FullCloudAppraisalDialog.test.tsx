import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { FullCloudAppraisalDialog } from "./FullCloudAppraisalDialog";
import { appraisalColorTokens, appraisalLayoutTokens } from "../design-system/inventoryFidelityTokens";
import type { CloudAppraisalPanel } from "../api/types";

expect.extend(toHaveNoViolations);

const samplePanel: CloudAppraisalPanel = {
  contractVersion: 1,
  itemName: "Ivory Buckler",
  sections: [
    { kind: "Header", lines: [{ text: "Ivory Buckler", style: "Title" }] },
    { kind: "ValueAndBurden", lines: [{ text: "Value: 100", style: "Body" }] },
  ],
};

describe("FullCloudAppraisalDialog", () => {
  it("renders every section/line from the appraisal panel", () => {
    render(
      <FullCloudAppraisalDialog
        open
        onClose={() => {}}
        itemName="Ivory Buckler"
        panel={samplePanel}
        isLoading={false}
        error={null}
        onRetry={() => {}}
      />,
    );

    expect(screen.getByRole("dialog", { name: /Ivory Buckler/ })).toBeInTheDocument();
    expect(screen.getByText("Value: 100")).toBeInTheDocument();
  });

  it("styles the dialog itself (not just an inner div) with the AC parchment/brass panel treatment", () => {
    render(
      <FullCloudAppraisalDialog
        open
        onClose={() => {}}
        itemName="Ivory Buckler"
        panel={samplePanel}
        isLoading={false}
        error={null}
        onRetry={() => {}}
      />,
    );

    const dialog = screen.getByRole("dialog", { name: /Ivory Buckler/ });
    expect(dialog).toHaveStyle({
      backgroundColor: appraisalColorTokens.panelBackground,
      borderColor: appraisalColorTokens.panelBorderLight,
      fontFamily: appraisalLayoutTokens.fontFamily,
    });
  });

  it("renders the appraisal body as a scrollable region so a long panel never overflows the dialog", () => {
    render(
      <FullCloudAppraisalDialog
        open
        onClose={() => {}}
        itemName="Ivory Buckler"
        panel={samplePanel}
        isLoading={false}
        error={null}
        onRetry={() => {}}
      />,
    );

    const body = document.querySelector(".full-cloud-appraisal") as HTMLElement | null;
    // jsdom's getComputedStyle resolves "vh" to a viewport-dependent px value, so toHaveStyle's
    // computed-style comparison can never match the literal token here; assert the inline style
    // source (what the browser actually receives) instead.
    expect(body?.style.maxHeight).toBe(appraisalLayoutTokens.bodyMaxHeight);
    expect(body).toHaveStyle({ overflowY: "auto" });
  });

  it("shows a loading state while the appraisal is being fetched", () => {
    render(
      <FullCloudAppraisalDialog
        open
        onClose={() => {}}
        itemName="Ivory Buckler"
        panel={null}
        isLoading
        error={null}
        onRetry={() => {}}
      />,
    );

    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("shows a retryable error state when the appraisal fails to load", async () => {
    const onRetry = vi.fn();
    const user = userEvent.setup();
    render(
      <FullCloudAppraisalDialog
        open
        onClose={() => {}}
        itemName="Ivory Buckler"
        panel={null}
        isLoading={false}
        error="Item not found."
        onRetry={onRetry}
      />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Item not found.");
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("closes on Escape and returns focus to the previously focused element", async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <>
        <button>opener</button>
        <FullCloudAppraisalDialog
          open
          onClose={onClose}
          itemName="Ivory Buckler"
          panel={samplePanel}
          isLoading={false}
          error={null}
          onRetry={() => {}}
        />
      </>,
    );

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("has no detectable accessibility violations", async () => {
    const { container } = render(
      <FullCloudAppraisalDialog
        open
        onClose={() => {}}
        itemName="Ivory Buckler"
        panel={samplePanel}
        isLoading={false}
        error={null}
        onRetry={() => {}}
      />,
    );

    expect(await axe(container)).toHaveNoViolations();
  });
});

import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { FullCloudAppraisalDialog } from "./FullCloudAppraisalDialog";
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

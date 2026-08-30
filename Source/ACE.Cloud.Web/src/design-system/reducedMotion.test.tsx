import { render, screen } from "@testing-library/react";
import { renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useIsReducedMotion } from "./useIsReducedMotion";
import { LoadingState } from "./primitives/LoadingState";
import { mockMatchMedia } from "../test/mockMatchMedia";

describe("useIsReducedMotion", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("is false when the user has not requested reduced motion", () => {
    mockMatchMedia([]);
    const { result } = renderHook(() => useIsReducedMotion());
    expect(result.current).toBe(false);
  });

  it("is true when prefers-reduced-motion: reduce matches", () => {
    mockMatchMedia(["prefers-reduced-motion"]);
    const { result } = renderHook(() => useIsReducedMotion());
    expect(result.current).toBe(true);
  });
});

describe("LoadingState respects reduced motion", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders an animated spinner by default", () => {
    mockMatchMedia([]);
    render(<LoadingState label="Loading" />);
    expect(screen.getByTestId("loading-spinner")).toHaveClass("loading-state__spinner--spin");
  });

  it("renders a static indicator instead of animating when reduced motion is requested", () => {
    mockMatchMedia(["prefers-reduced-motion"]);
    render(<LoadingState label="Loading" />);
    expect(screen.getByTestId("loading-spinner")).not.toHaveClass("loading-state__spinner--spin");
  });
});

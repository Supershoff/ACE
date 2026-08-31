import { useEffect, useId } from "react";
import { appraisalColorTokens, appraisalLayoutTokens } from "../design-system/inventoryFidelityTokens";
import { Button } from "../design-system/primitives/Button";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { ErrorState } from "../design-system/primitives/ErrorState";
import type { CloudAppraisalPanel, CloudAppraisalTextStyle } from "../api/types";

export interface FullCloudAppraisalDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly itemName: string;
  readonly panel: CloudAppraisalPanel | null;
  readonly isLoading: boolean;
  readonly error: string | null;
  readonly onRetry: () => void;
}

const textStyleColor: Record<CloudAppraisalTextStyle, string> = {
  Title: appraisalColorTokens.titleText,
  Body: appraisalColorTokens.bodyText,
  Muted: appraisalColorTokens.mutedText,
  Positive: appraisalColorTokens.positiveText,
  Negative: appraisalColorTokens.negativeText,
};

/**
 * Applied to the dialog box itself (not just an inner content div) so the AC parchment/brass
 * treatment covers the whole panel, including its title -- matching the native ID panel's compact
 * typography and dark translucent surface instead of the shell's generic dialog chrome. The "brass
 * double edge" is a solid brass border plus an inset dark ring drawn via box-shadow.
 */
const dialogStyle = {
  backgroundColor: appraisalColorTokens.panelBackground,
  borderColor: appraisalColorTokens.panelBorderLight,
  borderWidth: appraisalLayoutTokens.doubleEdgeBorderWidth,
  borderStyle: "solid",
  boxShadow: `inset 0 0 0 ${appraisalLayoutTokens.doubleEdgeInsetWidth} ${appraisalColorTokens.panelBorderDark}`,
  fontFamily: appraisalLayoutTokens.fontFamily,
  width: `min(${appraisalLayoutTokens.panelWidth}, 100%)`,
  minWidth: 0,
  alignSelf: "start",
} as const;

const dialogTitleStyle = {
  color: appraisalColorTokens.titleText,
  fontFamily: appraisalLayoutTokens.fontFamily,
  fontSize: appraisalLayoutTokens.titleFontSize,
} as const;

/** Scrollable so a long panel (armor/weapon stats, spells, requirements, ...) never overflows the dialog. */
const bodyStyle = {
  padding: appraisalLayoutTokens.bodyPadding,
  maxHeight: appraisalLayoutTokens.bodyMaxHeight,
  overflowY: "auto",
} as const;

const sectionStyle = {
  marginBottom: appraisalLayoutTokens.sectionSpacing,
} as const;

const lineStyle = (color: string) =>
  ({
    color,
    fontSize: appraisalLayoutTokens.bodyFontSize,
    margin: 0,
  }) as const;

/**
 * UI-004's Full Cloud Appraisal: a faithful, always-complete, character-independent reconstruction
 * of the in-game ID panel's player-facing content. `panel` is rendered exactly as the server built
 * it -- this component makes no examiner-skill or Display Character decision of its own, matching
 * `CloudAppraisalProjector.Build`'s own "always a complete successful appraisal" guarantee.
 */
export function FullCloudAppraisalDialog({
  open,
  onClose,
  itemName,
  panel,
  isLoading,
  error,
  onRetry,
}: FullCloudAppraisalDialogProps) {
  const titleId = useId();

  useEffect(() => {
    if (!open) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      role="dialog"
      aria-modal="false"
      aria-labelledby={titleId}
      className="full-cloud-appraisal-panel"
      style={dialogStyle}
    >
      <header style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
        <h2 id={titleId} style={dialogTitleStyle}>{itemName}</h2>
        <Button variant="secondary" onClick={onClose} aria-label="Close appraisal">×</Button>
      </header>
      <div className="full-cloud-appraisal" style={bodyStyle}>
        {isLoading ? <LoadingState label="Appraising…" /> : null}
        {!isLoading && error ? <ErrorState title="Appraisal unavailable" description={error} onRetry={onRetry} /> : null}
        {!isLoading && !error && panel
          ? panel.sections.map((section) => (
              <section
                key={section.kind}
                className={`full-cloud-appraisal__section full-cloud-appraisal__section--${section.kind}`}
                style={sectionStyle}
              >
                {section.lines.map((line, lineIndex) => (
                  <p key={lineIndex} style={lineStyle(textStyleColor[line.style])}>
                    {line.text}
                  </p>
                ))}
              </section>
            ))
          : null}
      </div>
    </div>
  );
}

import { useId } from "react";
import { appraisalColorTokens } from "../design-system/inventoryFidelityTokens";
import { Dialog } from "../design-system/primitives/Dialog";
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

const panelStyle = {
  backgroundColor: appraisalColorTokens.panelBackground,
  borderColor: appraisalColorTokens.panelBorderLight,
} as const;

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

  return (
    <Dialog open={open} onClose={onClose} titleId={titleId} title={`${itemName} — Full Cloud Appraisal`}>
      <div className="full-cloud-appraisal" style={panelStyle}>
        {isLoading ? <LoadingState label="Appraising…" /> : null}
        {!isLoading && error ? <ErrorState title="Appraisal unavailable" description={error} onRetry={onRetry} /> : null}
        {!isLoading && !error && panel
          ? panel.sections.map((section) => (
              <section key={section.kind} className={`full-cloud-appraisal__section full-cloud-appraisal__section--${section.kind}`}>
                {section.lines.map((line, lineIndex) => (
                  <p key={lineIndex} style={{ color: textStyleColor[line.style] }}>
                    {line.text}
                  </p>
                ))}
              </section>
            ))
          : null}
      </div>
    </Dialog>
  );
}

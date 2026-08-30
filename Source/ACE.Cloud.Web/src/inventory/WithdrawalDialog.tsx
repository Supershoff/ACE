import { useEffect, useId, useState } from "react";
import type { WithdrawalApi } from "../api/withdrawalApi";
import type { CloudInventoryItem, WithdrawalReservationTargetRequest } from "../api/types";
import { Button } from "../design-system/primitives/Button";
import { Dialog } from "../design-system/primitives/Dialog";

export interface WithdrawalSelectionEntry {
  readonly item: CloudInventoryItem;
  /** The exact quantity to withdraw for a stack item; ignored for a whole (non-stack) item. */
  readonly quantity: number;
}

export interface WithdrawalDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly selection: readonly WithdrawalSelectionEntry[];
  readonly withdrawalApi: WithdrawalApi;
  /** Called once a token has been issued or an open reservation has been cancelled, so the caller can clear its own selection. */
  readonly onSettled: () => void;
}

function buildTarget(entry: WithdrawalSelectionEntry): WithdrawalReservationTargetRequest {
  const { item, quantity } = entry;

  if (item.stackLotId === null) {
    return { kind: "Item", itemId: item.itemId };
  }

  if (quantity >= item.quantity) {
    // INV-002's full-quantity default: reserve the lot directly rather than splitting off everything.
    return { kind: "StackLot", stackLotId: item.stackLotId };
  }

  return { kind: "StackLot", stackLotId: item.stackLotId, quantity, expectedVersion: item.version };
}

function openErrorMessage(status: number): string {
  if (status === 503) {
    return "ACE is currently offline, so a Withdrawal Token cannot be created right now. Try again once the world is back up.";
  }
  if (status === 409) {
    return "One or more selected items are no longer available to withdraw. Refresh your inventory and try again.";
  }
  return "The Withdrawal Token could not be created.";
}

function formatCountdown(secondsRemaining: number): string {
  const minutes = Math.floor(secondsRemaining / 60);
  const seconds = secondsRemaining % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

interface IssuedToken {
  readonly reservationId: string;
  readonly tokenSecret: string;
  readonly version: number;
  readonly expiresAtUtc: string;
}

/**
 * WDR-001..003, WDR-006, WDR-008: creates a Withdrawal Reservation/Token for the caller's current
 * selection, reveals the high-entropy secret exactly once with a copy affordance and a live
 * countdown to its 15-minute expiry, and allows explicit pre-redemption cancellation.
 */
export function WithdrawalDialog({ open, onClose, selection, withdrawalApi, onSettled }: WithdrawalDialogProps) {
  const titleId = useId();

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [issued, setIssued] = useState<IssuedToken | null>(null);
  const [secondsRemaining, setSecondsRemaining] = useState(0);
  const [copyConfirmed, setCopyConfirmed] = useState(false);

  useEffect(() => {
    if (!open) {
      setIssued(null);
      setError(null);
      setCopyConfirmed(false);
    }
  }, [open]);

  useEffect(() => {
    if (!issued) {
      return;
    }

    function tick() {
      const remainingMs = new Date(issued!.expiresAtUtc).getTime() - Date.now();
      setSecondsRemaining(Math.max(0, Math.ceil(remainingMs / 1000)));
    }

    tick();
    const interval = setInterval(tick, 1000);
    return () => clearInterval(interval);
  }, [issued]);

  async function handleCreate() {
    setIsSubmitting(true);
    setError(null);

    const targets = selection.map(buildTarget);
    const result = await withdrawalApi.openReservation(targets);

    setIsSubmitting(false);

    if (result.ok && result.data) {
      setIssued(result.data);
      // The selected items are now reserved (no longer withdrawable/listable/transferable), so the
      // caller's own selection state -- built from a now-stale inventory read -- must be refreshed.
      onSettled();
      return;
    }

    setError(openErrorMessage(result.status));
  }

  async function handleCancel() {
    if (!issued) {
      return;
    }

    setIsSubmitting(true);
    const result = await withdrawalApi.cancelReservation(issued.reservationId, issued.version);
    setIsSubmitting(false);

    if (result.ok) {
      setIssued(null);
      onSettled();
      onClose();
      return;
    }

    setError("This reservation could not be cancelled. It may have already expired or been redeemed.");
  }

  async function handleCopy() {
    if (!issued) {
      return;
    }
    try {
      await navigator.clipboard.writeText(issued.tokenSecret);
      setCopyConfirmed(true);
    } catch {
      // Clipboard access can fail (permissions, non-secure context); the secret remains visible for
      // manual copy, so this is not a fatal error.
    }
  }

  const isExpired = issued !== null && secondsRemaining <= 0;

  return (
    <Dialog open={open} onClose={onClose} titleId={titleId} title="Withdraw to a character">
      <div className="withdrawal-dialog">
        {!issued ? (
          <>
            <ul>
              {selection.map((entry) => (
                <li key={entry.item.stackLotId ? `${entry.item.itemId}:${entry.item.stackLotId}` : entry.item.itemId}>
                  {entry.item.name}
                  {entry.item.stackLotId ? ` × ${entry.quantity}` : ""}
                </li>
              ))}
            </ul>

            {error ? <p role="alert">{error}</p> : null}

            <div className="withdrawal-dialog__actions">
              <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
                Cancel
              </Button>
              <Button variant="primary" onClick={handleCreate} disabled={isSubmitting || selection.length === 0}>
                Create Withdrawal Token
              </Button>
            </div>
          </>
        ) : (
          <>
            <p>
              Use this command in game within 15 minutes. It is shown only once, so copy it now if you are not
              ready to redeem it immediately.
            </p>
            <code data-testid="withdrawal-token-secret">{issued.tokenSecret}</code>
            <Button variant="secondary" onClick={handleCopy}>
              {copyConfirmed ? "Copied" : "Copy"}
            </Button>

            {isExpired ? (
              <p role="alert">This Withdrawal Token has expired. Its reservation was released.</p>
            ) : (
              <p role="status">Expires in {formatCountdown(secondsRemaining)}</p>
            )}

            {error ? <p role="alert">{error}</p> : null}

            <div className="withdrawal-dialog__actions">
              <Button variant="danger" onClick={handleCancel} disabled={isSubmitting || isExpired}>
                Cancel Withdrawal Token
              </Button>
              <Button variant="secondary" onClick={onClose}>
                Done
              </Button>
            </div>
          </>
        )}
      </div>
    </Dialog>
  );
}

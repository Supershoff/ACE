import { useEffect, useId, useState } from "react";
import type { AccountApi } from "../api/accountApi";
import { Button } from "../design-system/primitives/Button";
import { Dialog } from "../design-system/primitives/Dialog";

export interface AccountUnlinkDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onUnlinked: () => void;
  readonly linkedAccountId: number;
  readonly accountApi: AccountApi;
}

function errorMessage(status: number): string {
  if (status === 403) {
    return "Only the Main Account can unlink an account.";
  }
  return "That account could not be unlinked. It may already be unlinked.";
}

/**
 * AUTH-005's irreversible unlink warning: unlinking stops future deposits from routing to this
 * Main Account, but never restores or reassigns any Cloud asset already transferred here.
 */
export function AccountUnlinkDialog({ open, onClose, onUnlinked, linkedAccountId, accountApi }: AccountUnlinkDialogProps) {
  const titleId = useId();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setError(null);
    }
  }, [open]);

  async function handleConfirm() {
    setIsSubmitting(true);
    setError(null);

    const result = await accountApi.unlink(linkedAccountId);

    setIsSubmitting(false);

    if (result.ok) {
      onUnlinked();
      return;
    }

    setError(errorMessage(result.status));
  }

  return (
    <Dialog open={open} onClose={onClose} titleId={titleId} title="Unlink this account">
      <div role="alertdialog" aria-describedby={`${titleId}-warning`}>
        <p id={`${titleId}-warning`} style={{ color: "var(--color-danger, #b00020)" }}>
          Unlinking is permanent. Cloud assets already transferred to this Main Account stay here; they are
          never moved back. Once unlinked, that account's future deposits belong to it independently.
        </p>

        {error ? <p role="alert">{error}</p> : null}

        <div className="account-unlink-dialog__actions">
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button variant="danger" onClick={handleConfirm} disabled={isSubmitting}>
            Unlink account
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

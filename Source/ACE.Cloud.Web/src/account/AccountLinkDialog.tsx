import { useEffect, useId, useState } from "react";
import type { AccountApi } from "../api/accountApi";
import type { CloudAccountLinkRejectionCode } from "../api/types";
import { Button } from "../design-system/primitives/Button";
import { Dialog } from "../design-system/primitives/Dialog";

export interface AccountLinkDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onLinked: () => void;
  readonly mainAccountName: string;
  readonly accountApi: AccountApi;
}

/** AUTH-007's delayed-accept control, in seconds. */
const CONFIRM_DELAY_SECONDS = 10;

const REJECTION_MESSAGES: Record<CloudAccountLinkRejectionCode, string> = {
  None: "",
  MutationsFrozen: "Cloud Mule is currently in maintenance and cannot process account links right now.",
  SameAccount: "You are already logged in as that account.",
  MainAccountIsLinkedElsewhere: "Your account is itself a Linked Account and cannot link another account.",
  SourceAlreadyLinked: "That account is already linked to a Main Account.",
  SourceHasLinkedAccounts: "That account already has Linked Accounts of its own and cannot be linked.",
  SourceHasPendingObligations: "That account has a pending reservation, token, or other obligation and must be settled before linking.",
  WouldCreateAuctionConflict: "Linking that account would create a conflict with an active auction.",
  LinkNotActive: "That link is no longer active.",
};

function errorMessage(status: number, error: unknown): string {
  const errorBody = error as { error?: string; reason?: CloudAccountLinkRejectionCode } | undefined;
  if (status === 401) {
    return "That source account name or password is incorrect.";
  }
  if (status === 403) {
    return "Only the Main Account can link another account.";
  }
  if (status === 429) {
    return "Too many link attempts. Wait a moment and try again.";
  }
  if (status === 409 && errorBody?.reason) {
    return REJECTION_MESSAGES[errorBody.reason] || "That account cannot be linked right now.";
  }
  return "The account could not be linked. Check the details and try again.";
}

/**
 * AUTH-005..009's destructive account-linking flow: a prominent warning, exact Main Account name
 * typing, source-password re-entry, and a ~10-second delayed accept control -- every irreversible
 * confirmation this Progressive Interface surfaces only when the player has actually committed to
 * the action, never as permanently visible chrome.
 */
export function AccountLinkDialog({ open, onClose, onLinked, mainAccountName, accountApi }: AccountLinkDialogProps) {
  const titleId = useId();
  const confirmationInputId = useId();
  const sourceNameInputId = useId();
  const sourcePasswordInputId = useId();

  const [confirmationText, setConfirmationText] = useState("");
  const [sourceAccountName, setSourceAccountName] = useState("");
  const [sourcePassword, setSourcePassword] = useState("");
  const [secondsRemaining, setSecondsRemaining] = useState(CONFIRM_DELAY_SECONDS);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    setConfirmationText("");
    setSourceAccountName("");
    setSourcePassword("");
    setSecondsRemaining(CONFIRM_DELAY_SECONDS);
    setError(null);

    const interval = setInterval(() => {
      setSecondsRemaining((current) => Math.max(0, current - 1));
    }, 1000);

    return () => clearInterval(interval);
  }, [open]);

  const nameMatches = confirmationText.trim() === mainAccountName;
  const canSubmit = nameMatches && sourceAccountName.trim().length > 0 && sourcePassword.length > 0 && secondsRemaining === 0 && !isSubmitting;

  async function handleSubmit() {
    if (!canSubmit) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    const result = await accountApi.link(sourceAccountName.trim(), sourcePassword);

    setIsSubmitting(false);

    if (result.ok) {
      onLinked();
      return;
    }

    setError(errorMessage(result.status, result.error));
  }

  return (
    <Dialog open={open} onClose={onClose} titleId={titleId} title="Link another account">
      <div className="account-link-dialog" role="alertdialog" aria-describedby={`${titleId}-warning`}>
        <p id={`${titleId}-warning`} className="account-link-dialog__warning" style={{ color: "var(--color-danger, #b00020)" }}>
          Linking is permanent. Every Cloud asset currently owned by the source account moves to this Main
          Account and cannot be moved back by unlinking later. The source account keeps using its own login
          for the game, but its web login will only show that it is linked.
        </p>

        <label htmlFor={confirmationInputId}>
          Type your Main Account name (<strong>{mainAccountName}</strong>) to confirm
        </label>
        <input
          id={confirmationInputId}
          type="text"
          value={confirmationText}
          onChange={(event) => setConfirmationText(event.target.value)}
          autoComplete="off"
        />

        <label htmlFor={sourceNameInputId}>Account name to link</label>
        <input
          id={sourceNameInputId}
          type="text"
          value={sourceAccountName}
          onChange={(event) => setSourceAccountName(event.target.value)}
          autoComplete="off"
        />

        <label htmlFor={sourcePasswordInputId}>That account&apos;s password</label>
        <input
          id={sourcePasswordInputId}
          type="password"
          value={sourcePassword}
          onChange={(event) => setSourcePassword(event.target.value)}
          autoComplete="off"
        />

        {error ? <p role="alert">{error}</p> : null}

        <div className="account-link-dialog__actions">
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button variant="danger" onClick={handleSubmit} disabled={!canSubmit}>
            {secondsRemaining > 0 ? `Link account (${secondsRemaining})` : "Link account"}
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

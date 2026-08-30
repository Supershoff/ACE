import { useId, useState, type FormEvent } from "react";
import type { AccountApi } from "../api/accountApi";
import type { CloudAccountIdentityResponse, CloudAccountLinkRejectionCode } from "../api/types";
import { Button } from "../design-system/primitives/Button";
import { Dialog } from "../design-system/primitives/Dialog";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { useSession } from "../session/SessionContext";
import { useDelayedConfirmation } from "./useDelayedConfirmation";

export interface AccountLinkingPanelProps {
  readonly identity: CloudAccountIdentityResponse;
  readonly accountApi: AccountApi;
  /** Re-fetches `/account/identity` after a successful link or unlink. */
  readonly onChanged: () => void;
}

const LINK_REJECTION_MESSAGES: Record<CloudAccountLinkRejectionCode, string> = {
  None: "",
  MutationsFrozen: "Account linking is temporarily paused for maintenance. Try again shortly.",
  SameAccount: "You can't link an account to itself.",
  MainAccountIsLinkedElsewhere:
    "Your account is already a Linked Account of another Main Account, so it can't link accounts of its own.",
  SourceAlreadyLinked: "That account is already linked to a Main Account.",
  SourceHasLinkedAccounts: "That account already has Linked Accounts of its own, so link trees can't form.",
  SourceHasPendingObligations:
    "That account has an active reservation, Withdrawal Token, or other pending activity. Resolve it before linking.",
  WouldCreateAuctionConflict: "Linking that account would create a conflict with an active auction.",
  LinkNotActive: "That account isn't currently linked.",
};

const LINK_ERROR_MESSAGES: Record<string, string> = {
  invalid_request: "Enter the account name and password to link.",
  invalid_source_credentials: "That account name and password don't match.",
  authentication_unavailable: "Account verification is temporarily unavailable. Try again shortly.",
  linked_account_restricted: "Linked account credentials can't manage account linking.",
};

export function AccountLinkingPanel({ identity, accountApi, onChanged }: AccountLinkingPanelProps) {
  const { accountName: mainAccountName } = useSession();
  const linkDialogTitleId = useId();
  const unlinkDialogTitleId = useId();

  const [sourceAccountName, setSourceAccountName] = useState("");
  const [sourcePassword, setSourcePassword] = useState("");
  const [linkDialogOpen, setLinkDialogOpen] = useState(false);
  const [typedMainAccountName, setTypedMainAccountName] = useState("");
  const [linkError, setLinkError] = useState<string | null>(null);
  const [linkPending, setLinkPending] = useState(false);
  const linkConfirmReady = useDelayedConfirmation(linkDialogOpen);
  const linkConfirmMatches = mainAccountName !== null && typedMainAccountName === mainAccountName;

  const [unlinkTargetAccountId, setUnlinkTargetAccountId] = useState<number | null>(null);
  const [unlinkError, setUnlinkError] = useState<string | null>(null);
  const [unlinkPending, setUnlinkPending] = useState(false);
  const unlinkConfirmReady = useDelayedConfirmation(unlinkTargetAccountId !== null);

  function openLinkDialog(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLinkError(null);
    setTypedMainAccountName("");
    setLinkDialogOpen(true);
  }

  async function confirmLink() {
    setLinkPending(true);
    setLinkError(null);
    const result = await accountApi.link(sourceAccountName, sourcePassword);
    setLinkPending(false);

    if (!result.ok) {
      const kind = (result.error as { error?: string } | undefined)?.error ?? "";
      setLinkError(LINK_ERROR_MESSAGES[kind] ?? "This account couldn't be linked. Try again.");
      return;
    }

    if (!result.data!.approved) {
      setLinkError(LINK_REJECTION_MESSAGES[result.data!.rejectionCode]);
      return;
    }

    setLinkDialogOpen(false);
    setSourceAccountName("");
    setSourcePassword("");
    onChanged();
  }

  async function confirmUnlink() {
    if (unlinkTargetAccountId === null) {
      return;
    }
    setUnlinkPending(true);
    setUnlinkError(null);
    const result = await accountApi.unlink(unlinkTargetAccountId);
    setUnlinkPending(false);

    if (!result.ok || !result.data!.approved) {
      setUnlinkError(
        result.ok
          ? LINK_REJECTION_MESSAGES[result.data!.rejectionCode]
          : "This account couldn't be unlinked. Try again.",
      );
      return;
    }

    setUnlinkTargetAccountId(null);
    onChanged();
  }

  return (
    <section aria-label="Linked accounts">
      <h2>Linked accounts</h2>

      {identity.linkedAccounts.length === 0 ? (
        <p>No accounts are linked yet.</p>
      ) : (
        <ul>
          {identity.linkedAccounts.map((link) => (
            <li key={link.accountId}>
              Linked account #{link.accountId}
              <Button
                variant="danger"
                onClick={() => {
                  setUnlinkError(null);
                  setUnlinkTargetAccountId(link.accountId);
                }}
              >
                Unlink
              </Button>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={openLinkDialog}>
        <h3>Link an account</h3>
        <label htmlFor="source-account-name">Account name to link</label>
        <input
          id="source-account-name"
          value={sourceAccountName}
          onChange={(event) => setSourceAccountName(event.target.value)}
        />

        <label htmlFor="source-password">That account's password</label>
        <input
          id="source-password"
          type="password"
          autoComplete="off"
          value={sourcePassword}
          onChange={(event) => setSourcePassword(event.target.value)}
        />

        <Button type="submit" disabled={!sourceAccountName || !sourcePassword}>
          Link account
        </Button>
      </form>

      <Dialog
        open={linkDialogOpen}
        onClose={() => setLinkDialogOpen(false)}
        titleId={linkDialogTitleId}
        title="Link this account? This can't be undone."
      >
        <p>
          Linking transfers every Cloud item and balance <strong>{sourceAccountName}</strong> currently owns to your
          Main Account. Unlinking later does not return them.
        </p>
        <p>
          To confirm, type your Main account name (<strong>{mainAccountName}</strong>) below.
        </p>
        <label htmlFor="confirm-main-account-name">Your Main account name</label>
        <input
          id="confirm-main-account-name"
          value={typedMainAccountName}
          onChange={(event) => setTypedMainAccountName(event.target.value)}
        />
        {linkError ? <ErrorState title="Link blocked" description={linkError} /> : null}
        <Button
          variant="danger"
          disabled={!linkConfirmReady || !linkConfirmMatches || linkPending}
          onClick={confirmLink}
        >
          {linkPending ? "Linking…" : "Confirm link"}
        </Button>
        <Button variant="secondary" onClick={() => setLinkDialogOpen(false)}>
          Cancel
        </Button>
      </Dialog>

      <Dialog
        open={unlinkTargetAccountId !== null}
        onClose={() => setUnlinkTargetAccountId(null)}
        titleId={unlinkDialogTitleId}
        title="Unlink this account?"
      >
        <p>
          Unlinking account #{unlinkTargetAccountId} stops routing its future deposits to your Main Account. Assets
          it already transferred to your Main Account stay with your Main Account -- this does not restore them to
          the unlinked account.
        </p>
        {unlinkError ? <ErrorState title="Unlink blocked" description={unlinkError} /> : null}
        <Button variant="danger" disabled={!unlinkConfirmReady || unlinkPending} onClick={confirmUnlink}>
          {unlinkPending ? "Unlinking…" : "Confirm unlink"}
        </Button>
        <Button variant="secondary" onClick={() => setUnlinkTargetAccountId(null)}>
          Cancel
        </Button>
      </Dialog>
    </section>
  );
}

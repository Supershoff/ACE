import { useEffect, useId, useState } from "react";
import type { CloudInventoryItem } from "../api/types";
import type { TransferOfferApi } from "../api/transferOfferApi";
import type { WithdrawalApi } from "../api/withdrawalApi";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { touchTargetStyle } from "../design-system/touchTarget";
import { RequireWritableService } from "../routes/RequireWritableService";
import { InventoryQuantityControl } from "./InventoryQuantityControl";

export type ItemActionKind = "transfer" | "withdraw";

export interface ItemActionDialogProps {
  readonly kind: ItemActionKind;
  readonly item: CloudInventoryItem;
  readonly transferOfferApi: TransferOfferApi;
  readonly withdrawalApi: WithdrawalApi;
  readonly onClose: () => void;
  /** Called after a successful submission so the caller can refresh the Inventory/appraisal. */
  readonly onCompleted: () => void;
}

const WITHDRAWAL_ERROR_MESSAGES: Record<string, string> = {
  invalid_request: "This item could not be selected for withdrawal.",
  linked_account_restricted: "Linked account credentials can't create Withdrawal Tokens.",
  world_boundary_unavailable: "ACE is currently offline, so Withdrawal Tokens can't be created right now. Try again once the world is back up.",
  conflict: "This item already has a pending action, or you already have an active Withdrawal Token. Refresh and try again.",
  unavailable: "Withdrawal Tokens are temporarily unavailable. Try again shortly.",
};

/**
 * Resolves the single selected item/quantity into one Transfer Offer or Withdrawal target,
 * splitting a new Cloud Stack Lot first when the requested quantity is a partial amount of a
 * stack (INV-002/INV-003) -- the original GUID/lot is never touched, matching
 * `WithdrawalTokenPanel.handleCreate`'s own established per-item logic, generalized here to a
 * single contextually selected item.
 */
async function resolveTarget(
  withdrawalApi: WithdrawalApi,
  item: CloudInventoryItem,
  requestedQuantity: number,
): Promise<{ ok: true; target: { kind: "Item" | "StackLot"; itemBiotaId?: number; stackLotId?: string } } | { ok: false }> {
  if (item.stackLotId === null) {
    return { ok: true, target: { kind: "Item", itemBiotaId: item.itemId } };
  }

  if (requestedQuantity >= item.quantity) {
    return { ok: true, target: { kind: "StackLot", stackLotId: item.stackLotId } };
  }

  const splitResult = await withdrawalApi.splitStackLot(item.stackLotId, item.version, requestedQuantity);
  if (!splitResult.ok || !splitResult.data) {
    return { ok: false };
  }
  return { ok: true, target: { kind: "StackLot", stackLotId: splitResult.data.newLot.id } };
}

/**
 * Issue #39's blocking human-acceptance fix: Transfer Offer and Withdrawal Token creation as
 * contextual actions for the currently selected Inventory item, so the item ID is always supplied
 * from application state (this component's own `item` prop) and never typed by the user.
 */
export function ItemActionDialog({ kind, item, transferOfferApi, withdrawalApi, onClose, onCompleted }: ItemActionDialogProps) {
  const titleId = useId();
  const [recipientCharacterName, setRecipientCharacterName] = useState("");
  const [quantity, setQuantity] = useState(item.quantity);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [justCreatedSecret, setJustCreatedSecret] = useState<string | null>(null);

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();

    if (kind === "transfer" && !recipientCharacterName.trim()) {
      setError("Enter a recipient character name.");
      return;
    }

    setPending(true);
    setError(null);

    const resolved = await resolveTarget(withdrawalApi, item, quantity);
    if (!resolved.ok) {
      setPending(false);
      setError(`Could not split ${item.name} into the requested quantity. Try again.`);
      return;
    }

    if (kind === "transfer") {
      const result = await transferOfferApi.create(recipientCharacterName.trim(), [resolved.target]);
      setPending(false);
      if (!result.ok) {
        setError("That offer could not be sent. Check the recipient name and try again.");
        return;
      }
      onCompleted();
      onClose();
      return;
    }

    const result = await withdrawalApi.create([resolved.target]);
    setPending(false);
    if (!result.ok || !result.data) {
      const errorKind = (result.error as { error?: string } | undefined)?.error ?? "";
      setError(WITHDRAWAL_ERROR_MESSAGES[errorKind] ?? "This Withdrawal Token couldn't be created. Try again.");
      return;
    }
    setJustCreatedSecret(result.data.secret);
    onCompleted();
  }

  async function handleCopySecret() {
    if (justCreatedSecret) {
      await navigator.clipboard.writeText(justCreatedSecret);
    }
  }

  const title = kind === "transfer" ? `Send ${item.name}` : `Withdraw ${item.name}`;

  return (
    <div role="dialog" aria-modal="false" aria-labelledby={titleId} className="item-action-dialog">
      <header style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
        <h2 id={titleId}>{title}</h2>
        <Button variant="secondary" onClick={onClose} aria-label="Close">×</Button>
      </header>

      {justCreatedSecret ? (
        <div>
          <p>Your Withdrawal Token (shown once -- copy it now):</p>
          <code>{justCreatedSecret}</code>
          <Button onClick={handleCopySecret}>Copy token</Button>
          <Button variant="secondary" onClick={onClose}>
            Done
          </Button>
        </div>
      ) : (
        <RequireWritableService>
          <form onSubmit={handleSubmit}>
            {kind === "transfer" ? (
              <label>
                Recipient character name
                <input
                  value={recipientCharacterName}
                  onChange={(event) => setRecipientCharacterName(event.target.value)}
                  style={touchTargetStyle}
                />
              </label>
            ) : null}

            {item.quantity > 1 ? (
              <InventoryQuantityControl
                itemName={item.name}
                maxQuantity={item.quantity}
                value={quantity}
                onChange={setQuantity}
              />
            ) : null}

            <Button type="submit" disabled={pending}>
              {pending ? "Sending…" : kind === "transfer" ? "Send offer" : "Create Withdrawal Token"}
            </Button>
            {error ? <ErrorState title="Action not completed" description={error} /> : null}
          </form>
        </RequireWritableService>
      )}
    </div>
  );
}

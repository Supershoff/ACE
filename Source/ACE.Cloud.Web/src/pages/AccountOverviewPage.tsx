import { Button } from "../design-system/primitives/Button";
import { RequireWritableService } from "../routes/RequireWritableService";

export function AccountOverviewPage() {
  return (
    <section>
      <h1>Account overview</h1>
      <p>Account linking and Withdrawal Token flows arrive with issue #33.</p>
      <RequireWritableService>
        <Button>Create Withdrawal Token</Button>
      </RequireWritableService>
    </section>
  );
}

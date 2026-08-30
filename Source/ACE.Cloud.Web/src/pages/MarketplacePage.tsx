import { EmptyState } from "../design-system/primitives/EmptyState";

export function MarketplacePage() {
  return (
    <section>
      <h1>Marketplace</h1>
      <EmptyState
        title="No active listings yet"
        description="Public Marketplace listings arrive with a later Cloud Mule phase."
      />
    </section>
  );
}

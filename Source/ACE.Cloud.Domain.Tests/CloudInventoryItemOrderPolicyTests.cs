namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #30's Red requirement: "...stable identity tie-breaks, filters, and sorts" (UI-003).
/// </summary>
[TestClass]
public sealed class CloudInventoryItemOrderPolicyTests
{
    [TestMethod]
    public void Sort_ByName_Ascending_OrdinalOrder()
    {
        var items = new[]
        {
            Item(3, "Zebra Longbow"),
            Item(1, "Aged Cheddar"),
            Item(2, "Mana Charge"),
        };

        var sorted = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        CollectionAssert.AreEqual(new[] { 1u, 2u, 3u }, sorted.Select(item => item.ItemId.Value).ToArray());
    }

    [TestMethod]
    public void Sort_ByName_Descending_ReversesOrder()
    {
        var items = new[] { Item(1, "Aged Cheddar"), Item(2, "Mana Charge") };

        var sorted = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Name, CloudInventorySortDirection.Descending);

        CollectionAssert.AreEqual(new[] { 2u, 1u }, sorted.Select(item => item.ItemId.Value).ToArray());
    }

    [TestMethod]
    public void Sort_ByValue_EqualValues_BreaksTieByStableItemIdentity()
    {
        var items = new[]
        {
            Item(5, "Item Five", value: 100),
            Item(3, "Item Three", value: 100),
            Item(4, "Item Four", value: 100),
        };

        var sorted = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Value, CloudInventorySortDirection.Ascending);

        // Equal values (a tie) must always resolve the same way regardless of input order: ascending
        // ItemId, never enumeration/delivery order (UI-003: "stable item identity as the final
        // tie-break").
        CollectionAssert.AreEqual(new[] { 3u, 4u, 5u }, sorted.Select(item => item.ItemId.Value).ToArray());
    }

    [TestMethod]
    public void Sort_ByValue_NullValueSortsLast_RegardlessOfDirection()
    {
        var items = new[] { Item(1, "Known Value", value: 10), Item(2, "Unknown Value", value: null) };

        var ascending = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Value, CloudInventorySortDirection.Ascending);
        CollectionAssert.AreEqual(new[] { 1u, 2u }, ascending.Select(item => item.ItemId.Value).ToArray());

        var descending = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Value, CloudInventorySortDirection.Descending);
        CollectionAssert.AreEqual(new[] { 1u, 2u }, descending.Select(item => item.ItemId.Value).ToArray());
    }

    [TestMethod]
    public void Sort_ByBurden_Descending_HeaviestFirst()
    {
        var items = new[] { Item(1, "Light", burden: 10), Item(2, "Heavy", burden: 200) };

        var sorted = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Burden, CloudInventorySortDirection.Descending);

        CollectionAssert.AreEqual(new[] { 2u, 1u }, sorted.Select(item => item.ItemId.Value).ToArray());
    }

    [TestMethod]
    public void Sort_SameInputTwice_ProducesIdenticalMembershipAndOrder()
    {
        // UI-003: page membership must not vary with client viewport/device -- since this API takes
        // no such input at all, the same query must be reproducible byte-for-byte every time.
        var items = new[] { Item(2, "B"), Item(1, "A"), Item(3, "C") };

        var first = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);
        var second = CloudInventoryItemOrderPolicy.Sort(items, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        CollectionAssert.AreEqual(first.Select(item => item.ItemId.Value).ToArray(), second.Select(item => item.ItemId.Value).ToArray());
    }

    private static CloudInventorySortableItem Item(uint id, string name, int? value = null, int? burden = null) =>
        new(new CloudItemId(id), name, value, burden);
}

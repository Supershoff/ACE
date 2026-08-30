namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #32's Red requirement: "Test text/property queries across all preserved fields,
/// category/filter/sort/page composition, stable tie ordering, ... revoked authorization" composed
/// with Safe Regex Search's admin-disablement and rate-limit behavior, at the pure Domain layer.
/// Authorization/category/sort/page composition itself is already proven by
/// <see cref="CloudInventoryQueryEngineTests"/> (reused here, not re-derived) -- these tests cover what
/// is new: typed text/property filtering and the Safe Regex Search opt-in.
/// </summary>
[TestClass]
public sealed class CloudInventorySearchEngineTests
{
    private static readonly CloudRateLimitResult Allowed = CloudRateLimitResult.Allowed();

    [TestMethod]
    public void Search_NameContains_IsCaseInsensitiveSubstringMatch()
    {
        var candidates = new[] { Candidate("Ivory Buckler"), Candidate("Steel Shield") };

        var result = Search(candidates, new CloudInventorySearchFilter { NameContains = "ivory" });

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.HasCount(1, result.Page!.Items);
        Assert.AreEqual("Ivory Buckler", result.Page.Items[0].Name);
    }

    [TestMethod]
    public void Search_ValueRange_OnlyReturnsItemsWithinBounds()
    {
        var candidates = new[] { Candidate("Cheap", value: 10), Candidate("Mid", value: 500), Candidate("Expensive", value: 9_999) };

        var result = Search(candidates, new CloudInventorySearchFilter { MinValue = 100, MaxValue = 1_000 });

        Assert.HasCount(1, result.Page!.Items);
        Assert.AreEqual("Mid", result.Page.Items[0].Name);
    }

    [TestMethod]
    public void Search_ValueRange_ItemWithNoRecordedValue_NeverMatchesABound()
    {
        var candidates = new[] { Candidate("No Value", value: null) };

        var result = Search(candidates, new CloudInventorySearchFilter { MinValue = 0 });

        Assert.IsEmpty(result.Page!.Items);
    }

    [TestMethod]
    public void Search_BurdenRange_OnlyReturnsItemsWithinBounds()
    {
        var candidates = new[] { Candidate("Light", burden: 1), Candidate("Heavy", burden: 500) };

        var result = Search(candidates, new CloudInventorySearchFilter { MaxBurden = 100 });

        Assert.HasCount(1, result.Page!.Items);
        Assert.AreEqual("Light", result.Page.Items[0].Name);
    }

    [TestMethod]
    public void Search_QuantityRange_OnlyReturnsItemsWithinBounds()
    {
        var candidates = new[] { Candidate("Single", quantity: 1), Candidate("Stack", quantity: 500) };

        var result = Search(candidates, new CloudInventorySearchFilter { MinQuantity = 100 });

        Assert.HasCount(1, result.Page!.Items);
        Assert.AreEqual("Stack", result.Page.Items[0].Name);
    }

    [TestMethod]
    public void Search_CategoryAndTextAndProperty_AllComposeTogether()
    {
        var candidates = new[]
        {
            Candidate("Ivory Buckler", category: CloudInventoryCategory.Armor, value: 500),
            Candidate("Ivory Wand", category: CloudInventoryCategory.Casters, value: 500),
            Candidate("Steel Buckler", category: CloudInventoryCategory.Armor, value: 500),
            Candidate("Ivory Buckler", category: CloudInventoryCategory.Armor, value: 5),
        };

        var result = Search(candidates, new CloudInventorySearchFilter
        {
            Category = CloudInventoryCategory.Armor,
            NameContains = "ivory",
            MinValue = 100,
        });

        Assert.HasCount(1, result.Page!.Items);
    }

    [TestMethod]
    public void Search_NoRegexPattern_NeverInvokesSafeRegexSearch_EvenWhenDisabled()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };

        var result = CloudInventorySearchEngine.Search(
            candidates, new CloudInventorySearchFilter { NameContains = "Ivory" }, regexSearchEnabled: false, Allowed);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.HasCount(1, result.Page!.Items);
    }

    [TestMethod]
    public void Search_RegexPattern_WhenRegexDisabled_ReturnsDisabled_WithoutDegradingPlainSearchElsewhere()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };

        var regexResult = CloudInventorySearchEngine.Search(
            candidates, new CloudInventorySearchFilter { RegexPattern = "Ivory" }, regexSearchEnabled: false, Allowed);
        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Disabled, regexResult.Kind);
        Assert.IsNull(regexResult.Page);

        var plainResult = CloudInventorySearchEngine.Search(
            candidates, new CloudInventorySearchFilter { NameContains = "Ivory" }, regexSearchEnabled: false, Allowed);
        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, plainResult.Kind);
        Assert.HasCount(1, plainResult.Page!.Items);
    }

    [TestMethod]
    public void Search_RegexPattern_WhenRegexEnabled_NarrowsFurtherThanPlainFilters()
    {
        var candidates = new[] { Candidate("Ivory Buckler"), Candidate("Ivory Wand") };

        var result = Search(candidates, new CloudInventorySearchFilter { NameContains = "Ivory", RegexPattern = "^Ivory Buckler$" });

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.HasCount(1, result.Page!.Items);
        Assert.AreEqual("Ivory Buckler", result.Page.Items[0].Name);
    }

    [TestMethod]
    public void Search_UnsupportedRegexConstruct_PropagatesTheEnginesOutcome()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };

        var result = Search(candidates, new CloudInventorySearchFilter { RegexPattern = @"(\w)\1" });

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.UnsupportedPattern, result.Kind);
        Assert.IsNull(result.Page);
        Assert.IsNotNull(result.Reason);
    }

    [TestMethod]
    public void Search_RateLimited_ReturnsRateLimited_WithoutEvaluatingAnything()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };
        var rateLimited = CloudRateLimitResult.RateLimited(TimeSpan.FromSeconds(30));

        var result = CloudInventorySearchEngine.Search(
            candidates, new CloudInventorySearchFilter { RegexPattern = "(a+)+$" }, regexSearchEnabled: true, rateLimited);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.RateLimited, result.Kind);
        Assert.IsNull(result.Page);
    }

    [TestMethod]
    public void Search_ZeroOrNegativePage_Throws()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CloudInventorySearchEngine.Search(candidates, new CloudInventorySearchFilter { Page = 0 }, regexSearchEnabled: true, Allowed));
    }

    private static CloudInventorySearchResult Search(
        IEnumerable<CloudInventoryQueryCandidate> candidates, CloudInventorySearchFilter filter) =>
        CloudInventorySearchEngine.Search(candidates, filter, regexSearchEnabled: true, Allowed);

    private static CloudInventoryQueryCandidate Candidate(
        string name,
        CloudInventoryCategory category = CloudInventoryCategory.Miscellaneous,
        int? value = null,
        int? burden = null,
        int quantity = 1) =>
        new(
            new CloudItemId(1),
            StackLotId: null,
            OwnerId: Guid.NewGuid(),
            Name: name,
            Category: category,
            Quantity: quantity,
            Value: value,
            Burden: burden,
            IsReserved: false,
            Version: CloudAggregateVersion.Initial);
}

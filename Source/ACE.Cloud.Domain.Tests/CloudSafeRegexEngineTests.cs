using System.Diagnostics;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #32's Red requirement: "Test catastrophic-looking expressions, unsupported constructs,
/// maximum pattern/input/result/time limits, cancellation, ... and attempts to reach SQL." Every
/// scenario here is pure and in-memory; there is no database anywhere near this engine.
/// </summary>
[TestClass]
public sealed class CloudSafeRegexEngineTests
{
    [TestMethod]
    public void Search_RegexDisabled_ReturnsDisabled_WithoutEvaluatingAnyCandidate()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };

        var result = CloudSafeRegexEngine.Search(candidates, "Ivory", regexSearchEnabled: false);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Disabled, result.Kind);
        Assert.IsEmpty(result.Matches);
        Assert.IsNotNull(result.Reason);
    }

    [TestMethod]
    public void Search_PatternLongerThanLimit_ReturnsPatternTooLong()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };
        var tooLong = new string('a', CloudSafeRegexLimits.MaxPatternLength + 1);

        var result = CloudSafeRegexEngine.Search(candidates, tooLong, regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.PatternTooLong, result.Kind);
    }

    [TestMethod]
    public void Search_MoreCandidatesThanLimit_ReturnsTooManyCandidates_WithoutMatchingAnything()
    {
        var candidates = Enumerable.Range(0, CloudSafeRegexLimits.MaxCandidatesToScan + 1)
            .Select(i => Candidate($"Item {i}"))
            .ToList();

        var result = CloudSafeRegexEngine.Search(candidates, "Item", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.TooManyCandidates, result.Kind);
    }

    [TestMethod]
    public void Search_Backreference_IsAnUnsupportedConstruct()
    {
        // Non-backtracking execution cannot support backreferences (they require exploring more than
        // one interpretation of an earlier group), so RegexOptions.NonBacktracking rejects this at
        // construction time, before any candidate is scanned.
        var candidates = new[] { Candidate("aa") };

        var result = CloudSafeRegexEngine.Search(candidates, @"(\w)\1", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.UnsupportedPattern, result.Kind);
    }

    [TestMethod]
    public void Search_Lookahead_IsAnUnsupportedConstruct()
    {
        var candidates = new[] { Candidate("foobar") };

        var result = CloudSafeRegexEngine.Search(candidates, "foo(?=bar)", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.UnsupportedPattern, result.Kind);
    }

    [TestMethod]
    public void Search_Lookbehind_IsAnUnsupportedConstruct()
    {
        var candidates = new[] { Candidate("foobar") };

        var result = CloudSafeRegexEngine.Search(candidates, "(?<=foo)bar", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.UnsupportedPattern, result.Kind);
    }

    [TestMethod]
    public void Search_MalformedPattern_ReturnsInvalidPattern()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };

        var result = CloudSafeRegexEngine.Search(candidates, "(unclosed", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.InvalidPattern, result.Kind);
    }

    [TestMethod]
    public void Search_CatastrophicLookingPattern_CompletesQuickly_InsteadOfHanging()
    {
        // A backtracking engine can take exponential time on a pattern shaped like this against an
        // input with no match; RegexOptions.NonBacktracking guarantees linear-time execution
        // regardless, so this must complete almost immediately rather than needing the match timeout
        // to rescue it.
        var pathologicalName = new string('a', 40) + "!";
        var candidates = new[] { Candidate(pathologicalName) };

        var stopwatch = Stopwatch.StartNew();
        var result = CloudSafeRegexEngine.Search(candidates, "(a+)+$", regexSearchEnabled: true);
        stopwatch.Stop();

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.IsEmpty(result.Matches);
        Assert.IsLessThan(2_000, stopwatch.ElapsedMilliseconds, "A non-backtracking engine must never exhibit catastrophic-backtracking-shaped slowdown.");
    }

    [TestMethod]
    public void Search_CaseInsensitiveSubstringPattern_Matches()
    {
        var candidates = new[] { Candidate("Ivory Buckler"), Candidate("Steel Shield") };

        var result = CloudSafeRegexEngine.Search(candidates, "ivory", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.HasCount(1, result.Matches);
        Assert.AreEqual("Ivory Buckler", result.Matches[0].Name);
    }

    [TestMethod]
    public void Search_SqlInjectionShapedPattern_IsTreatedAsAnOrdinaryPattern_NeverReachesSql()
    {
        // This engine never builds or executes SQL at all -- there is no query for this string to
        // escape into. It is evaluated purely as an in-memory literal-text regex against already
        // fetched names, matches nothing here, and produces an ordinary Matched/empty result rather
        // than any special-cased error.
        var candidates = new[] { Candidate("Ivory Buckler") };

        var result = CloudSafeRegexEngine.Search(candidates, "'; DROP TABLE CloudCustodyRecords; --", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.IsEmpty(result.Matches);
    }

    [TestMethod]
    public void Search_MoreMatchesThanResultLimit_TruncatesAtTheLimit()
    {
        var candidates = Enumerable.Range(0, CloudSafeRegexLimits.MaxResults + 50)
            .Select(i => Candidate($"Trade Note {i}"))
            .ToList();

        var result = CloudSafeRegexEngine.Search(candidates, "Trade Note", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.HasCount(CloudSafeRegexLimits.MaxResults, result.Matches);
    }

    [TestMethod]
    public void Search_AlreadyCancelledToken_ThrowsInsteadOfScanning()
    {
        var candidates = new[] { Candidate("Ivory Buckler") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            CloudSafeRegexEngine.Search(candidates, "Ivory", regexSearchEnabled: true, cts.Token));
    }

    [TestMethod]
    public void Search_NoCandidates_ReturnsMatchedWithNoResults()
    {
        var result = CloudSafeRegexEngine.Search([], "anything", regexSearchEnabled: true);

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, result.Kind);
        Assert.IsEmpty(result.Matches);
    }

    private static CloudInventoryQueryCandidate Candidate(string name) =>
        new(
            new CloudItemId(1),
            StackLotId: null,
            OwnerId: Guid.NewGuid(),
            Name: name,
            Category: CloudInventoryCategory.Miscellaneous,
            Quantity: 1,
            Value: null,
            Burden: null,
            IsReserved: false,
            Version: CloudAggregateVersion.Initial);
}

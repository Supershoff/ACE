namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #20's Red section: "Test highest-total-logins default, selection,
/// rename/deletion fallback, immutable snapshots, and no-current-character behavior" (AUTH-003).
/// </summary>
[TestClass]
public sealed class CloudDisplayCharacterSelectionPolicyTests
{
    [TestMethod]
    public void SelectDefault_OneCandidate_SelectsIt()
    {
        var candidate = new CloudDisplayCharacterCandidate(1, "Sologrind", totalLogins: 5);

        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault([candidate]);

        Assert.IsTrue(result.HasSelection);
        Assert.AreEqual(1u, result.CharacterId);
        Assert.AreEqual("Sologrind", result.CharacterName);
        Assert.AreEqual(5, result.TotalLogins);
    }

    [TestMethod]
    public void SelectDefault_SelectsTheHighestTotalLogins()
    {
        var low = new CloudDisplayCharacterCandidate(1, "Alt", totalLogins: 3);
        var high = new CloudDisplayCharacterCandidate(2, "Main", totalLogins: 300);

        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault([low, high]);

        Assert.AreEqual(2u, result.CharacterId);
        Assert.AreEqual("Main", result.CharacterName);
    }

    [TestMethod]
    public void SelectDefault_EqualTotalLogins_BreaksTiesByTheLowestCharacterId()
    {
        // Deterministic, never input-order-dependent (matches CloudBidPriorityPolicy's precedent).
        var higherId = new CloudDisplayCharacterCandidate(99, "Newer", totalLogins: 40);
        var lowerId = new CloudDisplayCharacterCandidate(2, "Older", totalLogins: 40);

        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault([higherId, lowerId]);

        Assert.AreEqual(2u, result.CharacterId);
    }

    [TestMethod]
    public void SelectDefault_TieBreakIsIndependentOfInputOrder()
    {
        var a = new CloudDisplayCharacterCandidate(5, "A", totalLogins: 10);
        var b = new CloudDisplayCharacterCandidate(6, "B", totalLogins: 10);

        var forward = CloudDisplayCharacterSelectionPolicy.SelectDefault([a, b]);
        var reversed = CloudDisplayCharacterSelectionPolicy.SelectDefault([b, a]);

        Assert.AreEqual(forward.CharacterId, reversed.CharacterId);
        Assert.AreEqual(5u, forward.CharacterId);
    }

    [TestMethod]
    public void SelectDefault_NoCandidates_ReturnsNoSelection()
    {
        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault([]);

        Assert.IsFalse(result.HasSelection);
        Assert.IsNull(result.CharacterName);
    }

    [TestMethod]
    public void SelectDefault_AfterDeletionOfTheSelectedCharacter_FallsBackToTheRemainingHighestTotalLogins()
    {
        var deletedWinner = new CloudDisplayCharacterCandidate(1, "WasWinner", totalLogins: 500);
        var remaining = new CloudDisplayCharacterCandidate(2, "RemainingAlt", totalLogins: 20);

        var beforeDeletion = CloudDisplayCharacterSelectionPolicy.SelectDefault([deletedWinner, remaining]);
        Assert.AreEqual(1u, beforeDeletion.CharacterId);

        // A deletion removes the character from the current-character candidate list entirely; the
        // caller re-runs the same policy against the refreshed roster rather than a special-cased
        // fallback algorithm.
        var afterDeletion = CloudDisplayCharacterSelectionPolicy.SelectDefault([remaining]);

        Assert.AreEqual(2u, afterDeletion.CharacterId);
        Assert.AreEqual("RemainingAlt", afterDeletion.CharacterName);
    }

    [TestMethod]
    public void SelectDefault_AfterRenameOfTheSelectedCharacter_KeepsItSelectedUnderItsNewNameSnapshot()
    {
        var original = new CloudDisplayCharacterCandidate(1, "OldName", totalLogins: 500);
        var beforeRename = CloudDisplayCharacterSelectionPolicy.SelectDefault([original]);
        Assert.AreEqual("OldName", beforeRename.CharacterName);

        // A rename does not remove the character; it changes its name snapshot in the refreshed
        // candidate list AUTH-003 reselection re-runs against.
        var renamed = new CloudDisplayCharacterCandidate(1, "NewName", totalLogins: 500);
        var afterRename = CloudDisplayCharacterSelectionPolicy.SelectDefault([renamed]);

        Assert.AreEqual(1u, afterRename.CharacterId);
        Assert.AreEqual("NewName", afterRename.CharacterName);
    }

    [TestMethod]
    public void SelectDefault_ResultIsAnImmutableSnapshot_IndependentOfLaterCandidateChanges()
    {
        var candidate = new CloudDisplayCharacterCandidate(1, "Frozen", totalLogins: 10);
        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault([candidate]);

        // Selecting again with a changed roster must not retroactively alter the already-returned
        // snapshot (EVT-002's "display-name snapshot" guarantee applied to this selection result).
        var laterResult = CloudDisplayCharacterSelectionPolicy.SelectDefault([new CloudDisplayCharacterCandidate(1, "ChangedLater", totalLogins: 10)]);

        Assert.AreEqual("Frozen", result.CharacterName);
        Assert.AreEqual("ChangedLater", laterResult.CharacterName);
    }

    [TestMethod]
    public void Candidate_ZeroCharacterId_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudDisplayCharacterCandidate(0, "Name", totalLogins: 1));
    }

    [TestMethod]
    public void Candidate_BlankName_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudDisplayCharacterCandidate(1, "  ", totalLogins: 1));
    }

    [TestMethod]
    public void Candidate_NegativeTotalLogins_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudDisplayCharacterCandidate(1, "Name", totalLogins: -1));
    }

    [TestMethod]
    public void SelectDefault_NullCandidates_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudDisplayCharacterSelectionPolicy.SelectDefault(null!));
    }
}

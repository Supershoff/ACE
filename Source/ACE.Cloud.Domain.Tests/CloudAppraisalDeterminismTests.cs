using System.Reflection;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red section: "Test that character appraisal skill and Display Character do not alter the result."
/// Acceptance criterion: "No live Player object or appraisal skill is required by the companion
/// backend." <see cref="CloudAppraisalProjector.Build"/> structurally cannot vary by examiner,
/// character, skill, or Display Character -- its signature has no parameter a caller could even use
/// to pass one -- so this is proven both by reflecting over that signature and by building the same
/// snapshot repeatedly and asserting an identical result every time.
/// </summary>
[TestClass]
public sealed class CloudAppraisalDeterminismTests
{
    private static readonly CloudItemId ItemId = new(111222333);

    private static CloudAppraisalRawItemSnapshot RepresentativeItem() => new()
    {
        ItemId = ItemId,
        Name = "Test Item",
        LongDescription = "A test item.",
        Value = 100,
        Burden = 10,
        Spells = [new CloudAppraisalSpellReference { Name = "Some Spell", IsActiveEnchantment = true }],
    };

    [TestMethod]
    public void Build_MethodSignature_TakesExactlyOneSnapshotParameterAndNothingCharacterOrSkillShaped()
    {
        var method = typeof(CloudAppraisalProjector).GetMethod(nameof(CloudAppraisalProjector.Build), BindingFlags.Public | BindingFlags.Static);

        Assert.IsNotNull(method);

        var parameters = method!.GetParameters();
        Assert.HasCount(1, parameters);
        Assert.AreEqual(typeof(CloudAppraisalRawItemSnapshot), parameters[0].ParameterType);

        string[] forbiddenFragments = ["player", "examiner", "skill", "character", "worldobject"];
        foreach (var fragment in forbiddenFragments)
        {
            Assert.IsFalse(
                parameters[0].ParameterType.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"The Build parameter type name must never look like a live Player/skill/character concept ('{fragment}').");
        }
    }

    [TestMethod]
    public void Build_SameSnapshotBuiltRepeatedly_ProducesAnEqualPanelEveryTime()
    {
        var snapshot = RepresentativeItem();

        var first = CloudAppraisalProjector.Build(snapshot);
        var second = CloudAppraisalProjector.Build(snapshot);
        var third = CloudAppraisalProjector.Build(snapshot);

        Assert.AreEqual(first, second);
        Assert.AreEqual(second, third);
    }

    [TestMethod]
    public void Build_TwoSnapshotsThatDifferOnlyByItemId_ProduceTheSamePlayerFacingPanelContent()
    {
        // Simulates "different viewers/characters examining the same item state": nothing about who is
        // asking is even representable on the snapshot, so two otherwise-identical snapshots (as if
        // captured for two different Display Characters) must render identical player-facing content.
        var snapshotA = RepresentativeItem() with { ItemId = new CloudItemId(1) };
        var snapshotB = RepresentativeItem() with { ItemId = new CloudItemId(2) };

        var panelA = CloudAppraisalProjector.Build(snapshotA);
        var panelB = CloudAppraisalProjector.Build(snapshotB);

        Assert.AreEqual(panelA, panelB);
    }
}

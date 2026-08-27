namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// EVT-002: every event carries an immutable actor identity and display snapshot.
/// </summary>
[TestClass]
public sealed class CloudActorIdentityTests
{
    [TestMethod]
    public void Constructor_RejectsEmptyIdForNonSystemActor()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudActorIdentity(CloudActorKind.Account, Guid.Empty, "Someone"));
    }

    [TestMethod]
    public void Constructor_RejectsNullIdForNonSystemActor()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudActorIdentity(CloudActorKind.Character, null, "Someone"));
    }

    [TestMethod]
    public void Constructor_RejectsIndividualIdForSystemActor()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudActorIdentity(CloudActorKind.System, Guid.NewGuid(), "Automation"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Constructor_RejectsBlankDisplaySnapshot(string displaySnapshot)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudActorIdentity(CloudActorKind.Character, Guid.NewGuid(), displaySnapshot));
    }

    [TestMethod]
    public void SystemActor_CreatesActorWithNoIndividualId()
    {
        var actor = CloudActorIdentity.SystemActor("Withdrawal Token Expiry Sweep");

        Assert.AreEqual(CloudActorKind.System, actor.Kind);
        Assert.IsNull(actor.Id);
        Assert.AreEqual("Withdrawal Token Expiry Sweep", actor.DisplaySnapshot);
    }

    [TestMethod]
    public void Equality_IsValueBased()
    {
        var id = Guid.NewGuid();
        var first = new CloudActorIdentity(CloudActorKind.Character, id, "Aerbax");
        var second = new CloudActorIdentity(CloudActorKind.Character, id, "Aerbax");
        var different = new CloudActorIdentity(CloudActorKind.Character, id, "Someone Else");

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.AreNotEqual(first, different);
    }
}

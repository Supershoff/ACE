namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red section: "Test removal/redaction of account names, ScribeAccount/private administrator
/// fields, internal IDs not shown to players." Every fixture here deliberately carries the
/// administrator-only/internal raw value, then asserts it never appears anywhere in the rendered
/// panel -- proving redaction by scanning the actual output, not by trusting the projector's doc
/// comments (CONTEXT.md: "Full Cloud Appraisal excludes internal administrator-only fields").
/// </summary>
[TestClass]
public sealed class CloudAppraisalRedactionTests
{
    private static readonly CloudItemId ItemId = new(999888777);

    private const string SecretScribeAccount = "some_admin_ace_account_name";
    private const string SecretHouseOwnerAccount = "another_private_ace_account_name";

    private static CloudAppraisalRawItemSnapshot ItemWithPrivateFields() => new()
    {
        ItemId = ItemId,
        Name = "Player-Scribed Note",
        LongDescription = "A note left by an administrator for testing.",
        ScribeAccountName = SecretScribeAccount,
        HouseOwnerAccountName = SecretHouseOwnerAccount,
        AllowedWielderInstanceId = 123456,
        AllowedActivatorInstanceId = 654321,
    };

    private static IEnumerable<string> AllRenderedText(CloudAppraisalPanel panel) =>
        panel.Sections.SelectMany(s => s.Lines).Select(l => l.Text).Append(panel.ItemName);

    [TestMethod]
    public void Build_ScribeAccountName_NeverAppearsInAnyRenderedLine()
    {
        var panel = CloudAppraisalProjector.Build(ItemWithPrivateFields());

        Assert.IsFalse(AllRenderedText(panel).Any(text => text.Contains(SecretScribeAccount, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_HouseOwnerAccountName_NeverAppearsInAnyRenderedLine()
    {
        var panel = CloudAppraisalProjector.Build(ItemWithPrivateFields());

        Assert.IsFalse(AllRenderedText(panel).Any(text => text.Contains(SecretHouseOwnerAccount, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_AllowedWielderInstanceId_NeverAppearsInAnyRenderedLine_OnlyItsPresenceIsShown()
    {
        var panel = CloudAppraisalProjector.Build(ItemWithPrivateFields());

        Assert.IsFalse(AllRenderedText(panel).Any(text => text.Contains("123456", StringComparison.Ordinal)));

        var requirementsSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Requirements);
        Assert.Contains("This item can only be wielded by an allowed character.", requirementsSection.Lines.Select(l => l.Text).ToArray());
    }

    [TestMethod]
    public void Build_AllowedActivatorInstanceId_NeverAppearsInAnyRenderedLine_OnlyItsPresenceIsShown()
    {
        var panel = CloudAppraisalProjector.Build(ItemWithPrivateFields());

        Assert.IsFalse(AllRenderedText(panel).Any(text => text.Contains("654321", StringComparison.Ordinal)));

        var requirementsSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Requirements);
        Assert.Contains("This item can only be activated by an allowed character.", requirementsSection.Lines.Select(l => l.Text).ToArray());
    }

    [TestMethod]
    public void Build_ZeroAllowedWielderInstanceId_RendersNoRequirementLine()
    {
        var snapshot = ItemWithPrivateFields() with { AllowedWielderInstanceId = 0, AllowedActivatorInstanceId = 0 };

        var panel = CloudAppraisalProjector.Build(snapshot);

        Assert.IsFalse(panel.Sections.Any(s => s.Kind == CloudAppraisalSectionKind.Requirements));
    }

    [TestMethod]
    public void Build_RawItemSnapshotType_HasNoPropertyExposingTheAccountNameFragment()
    {
        // Defense in depth beyond the projector's behavior: even the *type itself* is checked so a
        // future field rename/addition that reintroduces an account-name-shaped property is caught
        // immediately, mirroring CloudPublicContractPrivacyTests' reflection sweep pattern.
        var properties = typeof(CloudAppraisalRawItemSnapshot).GetProperties();

        var accountNameProperties = properties
            .Where(p => p.Name.Contains("AccountName", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        CollectionAssert.AreEquivalent(
            new[] { nameof(CloudAppraisalRawItemSnapshot.ScribeAccountName), nameof(CloudAppraisalRawItemSnapshot.HouseOwnerAccountName) },
            accountNameProperties,
            "Only the two known, deliberately-never-rendered administrator account name properties should exist on this snapshot type.");
    }
}

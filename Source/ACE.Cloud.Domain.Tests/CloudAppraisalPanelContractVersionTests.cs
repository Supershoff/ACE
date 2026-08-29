using System.Text.Json;

namespace ACE.Cloud.Domain.Tests;

/// <summary>Green: "Version the projection contract."</summary>
[TestClass]
public sealed class CloudAppraisalPanelContractVersionTests
{
    private static readonly CloudItemId ItemId = new(42);

    [TestMethod]
    public void Build_DefaultsToTheCurrentContractVersion()
    {
        var panel = CloudAppraisalProjector.Build(new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "Versioned Item" });

        Assert.AreEqual(CloudAppraisalPanel.CurrentContractVersion, panel.ContractVersion);
    }

    [TestMethod]
    public void Panel_RoundTripsAnExplicitOlderContractVersionWithoutSilentlyReinterpretingIt()
    {
        var panel = new CloudAppraisalPanel
        {
            ContractVersion = 0,
            ItemName = "Legacy Captured Item",
            Sections = [new CloudAppraisalSection { Kind = CloudAppraisalSectionKind.Header, Lines = [new CloudAppraisalLine { Text = "Legacy Captured Item", Style = CloudAppraisalTextStyle.Title }] }],
        };

        var json = JsonSerializer.Serialize(panel);
        var roundTripped = JsonSerializer.Deserialize<CloudAppraisalPanel>(json);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(0, roundTripped!.ContractVersion);
        Assert.AreEqual(panel, roundTripped);
    }
}

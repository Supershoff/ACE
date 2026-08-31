using ACE.Database.Models.Shard;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// Human-acceptance regression (issue #34, 2026-08-31 human-acceptance pass, item 4): a Cloud custody
/// row deposited before the appraisal-snapshot capture landed -- or whose deposit-time capture failed
/// -- had no repair path at all, permanently stranding it on <c>CloudInventoryEndpoints.HandleGetAppraisalAsync</c>'s
/// Name/Value/Burden-only fallback (see <c>CloudCustodianManager.BackfillAppraisalSnapshotAsync</c>).
/// These tests exercise <see cref="Player.BuildAppraisalSnapshot(Biota)"/> directly against a
/// hand-built <see cref="Biota"/> -- the exact shape <c>CloudCustodianManager</c>'s backfill passes
/// already read from <c>DatabaseManager.Shard.BaseDatabase.GetBiota</c> -- with no live WorldObject,
/// Player session, or database required, mirroring <see cref="CloudCustodianAppraisalSnapshotTests"/>'s
/// existing "no live database/session needed" seam.
/// </summary>
[TestClass]
public sealed class CloudCustodianAppraisalBackfillTests
{
    private static uint _nextBiotaId = 1;

    private static Biota NewBiota(WeenieType weenieType)
    {
        return new Biota
        {
            Id = _nextBiotaId++,
            WeenieType = (int)weenieType,
        };
    }

    private static void SetInt(Biota biota, PropertyInt property, int value) =>
        biota.BiotaPropertiesInt.Add(new BiotaPropertiesInt { ObjectId = biota.Id, Type = (ushort)property, Value = value });

    private static void SetString(Biota biota, PropertyString property, string value) =>
        biota.BiotaPropertiesString.Add(new BiotaPropertiesString { ObjectId = biota.Id, Type = (ushort)property, Value = value });

    // Armor (Clothing/Shield) and weapon items are deliberately not exercised end-to-end here:
    // AppraiseInfo.BuildArmor/BuildWeapon construct a real ArmorProfile/WeaponProfile, whose
    // enchantment-modifier lookups (EnchantmentManager.GetArmorModVsType/GetDefenseMod ->
    // GetEnchantments_TopLayer) require portal.dat's spell table (ACE.Server.Entity.SpellSet) to be
    // loaded -- unavailable in this WorldObject-free, no-DAT test project by design (see this
    // project's .csproj comment), the same reason CloudCustodianAppraisalSnapshotTests's own
    // AppraiseInfo-based tests never populate ArmorProfile/WeaponProfile either. That mapping is
    // proven separately and exhaustively against a hand-built AppraiseInfo in
    // CloudCustodianAppraisalSnapshotTests; a live/production run already exercises the exact same
    // AppraiseInfo(item, examiner, true) construction this method uses for the already-shipped
    // deposit-time capture (Player_CloudCustodian.CaptureAppraisalSnapshot), so this is an existing,
    // unrelated environment constraint, not a gap introduced by this backfill overload.

    [TestMethod]
    public void BuildAppraisalSnapshot_FromBiota_MapsCompleteDescriptiveFieldsNotJustNameValueBurden()
    {
        var biota = NewBiota(WeenieType.Gem);
        SetString(biota, PropertyString.Name, "Retained Ruby");
        SetString(biota, PropertyString.LongDesc, "A finely cut gemstone.");
        SetInt(biota, PropertyInt.Value, 250);
        SetInt(biota, PropertyInt.EncumbranceVal, 5);
        SetInt(biota, PropertyInt.ItemWorkmanship, 7);
        SetInt(biota, PropertyInt.MaterialType, (int)MaterialType.Ruby);
        SetInt(biota, PropertyInt.Attuned, (int)AttunedStatus.Attuned);
        SetInt(biota, PropertyInt.Bonded, (int)BondedStatus.Bonded);

        var snapshot = Player.BuildAppraisalSnapshot(biota);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("Retained Ruby", snapshot.Name);
        Assert.AreEqual("A finely cut gemstone.", snapshot.LongDescription);
        Assert.AreEqual(250, snapshot.Value);
        Assert.AreEqual(5, snapshot.Burden);
        Assert.AreEqual(7, snapshot.Workmanship);
        Assert.AreEqual(nameof(MaterialType.Ruby), snapshot.MaterialName);

        // The whole point of this backfill path: a retained-biota row with no captured appraisal
        // snapshot at all now gets these fields instead of permanently serving the
        // Name/Value/Burden-only legacy fallback.
        Assert.IsTrue(snapshot.IsAttunedOrSticky);
        Assert.IsTrue(snapshot.IsBonded);
    }

    [TestMethod]
    public void BuildAppraisalSnapshot_FromBiota_NeverIncludesScribeAccountName()
    {
        // No live examiner exists during backfill to decide admin/sentinel/envoy/arch/psr privilege
        // (AppraiseInfo.BuildProfile), so a null examiner must always be treated as non-privileged --
        // this must never regress into leaking a private account name into ace_cloud (AGENTS.md: "no
        // private account names ... entering ... artifacts").
        var biota = NewBiota(WeenieType.Book);
        SetString(biota, PropertyString.Name, "Retained Scribed Book");
        SetString(biota, PropertyString.ScribeAccount, "some-private-account-name");

        var snapshot = Player.BuildAppraisalSnapshot(biota);

        Assert.IsNotNull(snapshot);
        Assert.IsNull(snapshot.ScribeAccountName);
    }

    [TestMethod]
    public void BuildAppraisalSnapshot_FromBiota_UnmappedWeenieType_ReturnsNull()
    {
        var biota = NewBiota(WeenieType.Undef);

        var snapshot = Player.BuildAppraisalSnapshot(biota);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public void BuildAppraisalSnapshot_FromBiota_NoNameProperty_FallsBackToBiotaIdPlaceholder()
    {
        var biota = NewBiota(WeenieType.Coin);

        var snapshot = Player.BuildAppraisalSnapshot(biota);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual($"Item 0x{biota.Id:X8}", snapshot.Name);
    }
}

using System.Text;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green coverage for issue #19's Red section: "...replayed/expired grants..." and Green
/// bullet "returning a signed, audience-bound, short-lived one-use grant" (AUTH-002). Replay itself
/// is a persistence concern (the Cloud backend records <see cref="CloudAuthGrant.Nonce"/>
/// consumption); this suite proves the pure signature/expiry/audience contract the persistence layer
/// builds on, plus key-rotation overlap.
/// </summary>
[TestClass]
public sealed class CloudAuthGrantIssuerValidatorTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    private static CloudPrivateServiceKeyRing MakeKeyRing(string activeKeyId = "k1", string? previousKeyId = null) =>
        new(
            new CloudPrivateServiceKey(activeKeyId, Encoding.UTF8.GetBytes("active-secret-active-secret-32b")),
            previousKeyId is null ? null : new CloudPrivateServiceKey(previousKeyId, Encoding.UTF8.GetBytes("previous-secret-previous-sec32b")));

    [TestMethod]
    public void IssueThenValidate_RoundTrips()
    {
        var keyRing = MakeKeyRing();
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now, keyRing);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(42u, result.Grant!.AccountId);
        Assert.AreEqual("cloud-backend", result.Grant.Audience);
    }

    [TestMethod]
    public void Validate_WrongAudience_IsAudienceMismatch()
    {
        var keyRing = MakeKeyRing();
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var result = CloudAuthGrantValidator.Validate(token, "some-other-audience", Now, keyRing);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.AudienceMismatch, result.Kind);
    }

    [TestMethod]
    public void Validate_AfterExpiry_IsExpired()
    {
        var keyRing = MakeKeyRing();
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now.AddMinutes(2), keyRing);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.Expired, result.Kind);
    }

    [TestMethod]
    public void Validate_JustBeforeExpiry_IsStillValid()
    {
        var keyRing = MakeKeyRing();
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now.AddMinutes(2).AddTicks(-1), keyRing);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_TamperedPayload_IsBadSignature()
    {
        var keyRing = MakeKeyRing();
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var parts = token.Split('.');
        var tampered = string.Join('.', parts[0] + "AAAA", parts[1], parts[2]);

        var result = CloudAuthGrantValidator.Validate(tampered, "cloud-backend", Now, keyRing);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.BadSignature, result.Kind);
    }

    [TestMethod]
    public void Validate_TamperedSignature_IsBadSignature()
    {
        var keyRing = MakeKeyRing();
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var parts = token.Split('.');
        var tampered = string.Join('.', parts[0], parts[1], parts[2] + "AAAA");

        var result = CloudAuthGrantValidator.Validate(tampered, "cloud-backend", Now, keyRing);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.BadSignature, result.Kind);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not-a-real-token")]
    [DataRow("only.two")]
    public void Validate_MalformedToken_IsMalformed(string? token)
    {
        var keyRing = MakeKeyRing();

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now, keyRing);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.Malformed, result.Kind);
    }

    [TestMethod]
    public void Validate_SignedWithUnknownKey_IsUnknownSigningKey()
    {
        var issuingRing = MakeKeyRing("k1");
        var validatingRing = MakeKeyRing("k2");
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), issuingRing);

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now, validatingRing);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.UnknownSigningKey, result.Kind);
    }

    [TestMethod]
    public void Validate_DuringKeyRotationOverlap_TokenSignedByPreviousKeyStillValidates()
    {
        var beforeRotation = MakeKeyRing(activeKeyId: "k1");
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), beforeRotation);

        // Rotation: what was active ("k1") becomes the overlap-window previous key; a new "k2"
        // becomes active. Tokens signed under the old key must still validate until it is fully
        // retired.
        var afterRotation = new CloudPrivateServiceKeyRing(
            new CloudPrivateServiceKey("k2", Encoding.UTF8.GetBytes("new-active-secret-new-active-32")),
            beforeRotation.ActiveKey);

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now, afterRotation);

        Assert.IsTrue(result.IsValid, "A grant signed just before rotation must still validate during the overlap window.");
    }

    [TestMethod]
    public void Validate_AfterOverlapWindowKeyFullyRetired_TokenIsUnknownSigningKey()
    {
        var beforeRotation = MakeKeyRing(activeKeyId: "k1");
        var token = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), beforeRotation);

        var fullyRotated = new CloudPrivateServiceKeyRing(
            new CloudPrivateServiceKey("k2", Encoding.UTF8.GetBytes("new-active-secret-new-active-32")));

        var result = CloudAuthGrantValidator.Validate(token, "cloud-backend", Now, fullyRotated);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(CloudAuthGrantValidationOutcomeKind.UnknownSigningKey, result.Kind);
    }

    [TestMethod]
    public void Issue_NeverProducesTheSameNonceTwice()
    {
        var keyRing = MakeKeyRing();

        var first = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);
        var second = CloudAuthGrantIssuer.Issue(42, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing);

        var firstGrant = CloudAuthGrantValidator.Validate(first, "cloud-backend", Now, keyRing).Grant!;
        var secondGrant = CloudAuthGrantValidator.Validate(second, "cloud-backend", Now, keyRing).Grant!;

        Assert.AreNotEqual(firstGrant.Nonce, secondGrant.Nonce);
    }

    [TestMethod]
    public void Issue_RejectsZeroAccountId()
    {
        var keyRing = MakeKeyRing();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CloudAuthGrantIssuer.Issue(0, "cloud-backend", Now, TimeSpan.FromMinutes(2), keyRing));
    }
}

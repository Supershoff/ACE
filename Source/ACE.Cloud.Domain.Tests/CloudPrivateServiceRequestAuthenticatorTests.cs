using System.Text;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green coverage for issue #19's Red section: "Test... private-service authentication, and
/// key rotation overlap" (security baseline).
/// </summary>
[TestClass]
public sealed class CloudPrivateServiceRequestAuthenticatorTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxSkew = TimeSpan.FromSeconds(30);

    private static CloudPrivateServiceKeyRing MakeKeyRing(string activeKeyId = "k1", string? previousKeyId = null) =>
        new(
            new CloudPrivateServiceKey(activeKeyId, Encoding.UTF8.GetBytes("active-secret-active-secret-32b")),
            previousKeyId is null ? null : new CloudPrivateServiceKey(previousKeyId, Encoding.UTF8.GetBytes("previous-secret-previous-sec32b")));

    [TestMethod]
    public void SignThenValidate_RoundTrips()
    {
        var keyRing = MakeKeyRing();
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, keyRing);

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(header, "POST", "/internal/auth/grants", Now, MaxSkew, keyRing);

        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void Validate_DifferentPath_IsRejected()
    {
        var keyRing = MakeKeyRing();
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, keyRing);

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(header, "POST", "/internal/auth/access-level/1", Now, MaxSkew, keyRing);

        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void Validate_DifferentMethod_IsRejected()
    {
        var keyRing = MakeKeyRing();
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, keyRing);

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(header, "GET", "/internal/auth/grants", Now, MaxSkew, keyRing);

        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void Validate_OutsideClockSkewWindow_IsRejected()
    {
        var keyRing = MakeKeyRing();
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, keyRing);

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(
            header, "POST", "/internal/auth/grants", Now + MaxSkew + TimeSpan.FromSeconds(1), MaxSkew, keyRing);

        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void Validate_MissingHeader_IsRejected()
    {
        var keyRing = MakeKeyRing();

        Assert.IsFalse(CloudPrivateServiceRequestAuthenticator.Validate(null, "POST", "/internal/auth/grants", Now, MaxSkew, keyRing));
    }

    [TestMethod]
    public void Validate_TamperedSignature_IsRejected()
    {
        var keyRing = MakeKeyRing();
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, keyRing);
        var tampered = header[..^4] + "AAAA";

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(tampered, "POST", "/internal/auth/grants", Now, MaxSkew, keyRing);

        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void Validate_UnknownKeyId_IsRejected()
    {
        var issuingRing = MakeKeyRing("k1");
        var validatingRing = MakeKeyRing("k2");
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, issuingRing);

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(header, "POST", "/internal/auth/grants", Now, MaxSkew, validatingRing);

        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void Validate_DuringKeyRotationOverlap_RequestSignedByPreviousKeyStillValidates()
    {
        var beforeRotation = MakeKeyRing(activeKeyId: "k1");
        var header = CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, beforeRotation);

        var afterRotation = new CloudPrivateServiceKeyRing(
            new CloudPrivateServiceKey("k2", Encoding.UTF8.GetBytes("new-active-secret-new-active-32")),
            beforeRotation.ActiveKey);

        var isValid = CloudPrivateServiceRequestAuthenticator.Validate(header, "POST", "/internal/auth/grants", Now, MaxSkew, afterRotation);

        Assert.IsTrue(isValid, "A request signed just before rotation must still authenticate during the overlap window.");
    }
}

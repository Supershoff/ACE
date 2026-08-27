using System.Text.Json;
using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// A Cloud boundary command result is exactly one of success, conflict, validation failure,
/// unavailable, or idempotent replay (transaction rules 3, 4, 8) — never an ambiguous state.
/// </summary>
[TestClass]
public sealed class CloudCommandResultTests
{
    [TestMethod]
    public void Success_CarriesPayloadAndNoReason()
    {
        var result = CloudCommandResult<string>.Success("payload");

        Assert.AreEqual(CloudCommandResultKind.Success, result.Kind);
        Assert.AreEqual("payload", result.Payload);
        Assert.IsNull(result.Reason);
    }

    [TestMethod]
    public void IdempotentReplay_CarriesPayloadAndNoReason()
    {
        var result = CloudCommandResult<string>.IdempotentReplay("payload");

        Assert.AreEqual(CloudCommandResultKind.IdempotentReplay, result.Kind);
        Assert.AreEqual("payload", result.Payload);
        Assert.IsNull(result.Reason);
    }

    [TestMethod]
    public void Success_RejectsNullPayload()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudCommandResult<string>.Success(null!));
    }

    [TestMethod]
    public void Conflict_CarriesReasonAndNoPayload()
    {
        var result = CloudCommandResult<string>.Conflict("stale version");

        Assert.AreEqual(CloudCommandResultKind.Conflict, result.Kind);
        Assert.IsNull(result.Payload);
        Assert.AreEqual("stale version", result.Reason);
    }

    [TestMethod]
    public void ValidationFailed_CarriesReasonAndNoPayload()
    {
        var result = CloudCommandResult<string>.ValidationFailed("not eligible");

        Assert.AreEqual(CloudCommandResultKind.ValidationFailed, result.Kind);
        Assert.IsNull(result.Payload);
        Assert.AreEqual("not eligible", result.Reason);
    }

    [TestMethod]
    public void Unavailable_CarriesReasonAndNoPayload()
    {
        var result = CloudCommandResult<string>.Unavailable("ACE world process is offline");

        Assert.AreEqual(CloudCommandResultKind.Unavailable, result.Kind);
        Assert.IsNull(result.Payload);
        Assert.AreEqual("ACE world process is offline", result.Reason);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void NonSuccessResults_RequireANonBlankReason(string? reason)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudCommandResult<string>.Conflict(reason!));
        Assert.ThrowsExactly<ArgumentException>(() => CloudCommandResult<string>.ValidationFailed(reason!));
        Assert.ThrowsExactly<ArgumentException>(() => CloudCommandResult<string>.Unavailable(reason!));
    }

    [TestMethod]
    public void Deserialization_RejectsSuccessWithNullPayload()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            JsonSerializer.Deserialize<CloudCommandResult<string>>("""{"Kind":0,"Payload":null,"Reason":null}"""));
    }

    [TestMethod]
    public void Deserialization_RejectsIdempotentReplayWithNullPayload()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            JsonSerializer.Deserialize<CloudCommandResult<string>>("""{"Kind":4,"Payload":null,"Reason":null}"""));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void Deserialization_RejectsNonSuccessKindWithBlankReason(int kind)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            JsonSerializer.Deserialize<CloudCommandResult<string>>($$"""{"Kind":{{kind}},"Payload":null,"Reason":null}"""));
    }
}

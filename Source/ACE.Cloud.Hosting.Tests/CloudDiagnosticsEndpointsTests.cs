using ACE.Cloud.Hosting;

namespace ACE.Cloud.Hosting.Tests;

/// <summary>
/// Red -> Green tests for issue #23's explicit failing scenario: "Prove that ACE world-process
/// downtime leaves Backend and Auth Bridge readiness routable for login and all off-world operations;
/// only Withdrawal Token creation/redemption and deposits may be unavailable. A generic readiness 503
/// for WorldBoundaryUnavailable is a failing result." Before this issue, <c>/health/ready</c> mapped
/// every non-Operational mode -- including WorldBoundaryUnavailable -- to a 503, which would pull a
/// healthy Backend/Auth Bridge out of a load balancer's rotation entirely during an ordinary ACE
/// world-process restart, failing login too. <see cref="CloudDiagnosticsEndpoints.IsRoutable"/> is the
/// exact decision the endpoint now uses.
/// </summary>
[TestClass]
public sealed class CloudDiagnosticsEndpointsTests
{
    [TestMethod]
    public void IsRoutable_Operational_IsTrue()
    {
        Assert.IsTrue(CloudDiagnosticsEndpoints.IsRoutable(CloudServiceAvailabilityMode.Operational));
    }

    [TestMethod]
    public void IsRoutable_WorldBoundaryUnavailable_IsTrue_NotAGeneric503()
    {
        Assert.IsTrue(
            CloudDiagnosticsEndpoints.IsRoutable(CloudServiceAvailabilityMode.WorldBoundaryUnavailable),
            "ARCH-008: the ACE world process being offline must not remove an otherwise healthy service from routing.");
    }

    [TestMethod]
    public void IsRoutable_ReadOnly_IsFalse()
    {
        Assert.IsFalse(
            CloudDiagnosticsEndpoints.IsRoutable(CloudServiceAvailabilityMode.ReadOnly),
            "ARCH-009: an unavailable database genuinely cannot serve any request.");
    }

    [TestMethod]
    public void IsRoutable_VersionIncompatible_IsFalse()
    {
        Assert.IsFalse(CloudDiagnosticsEndpoints.IsRoutable(CloudServiceAvailabilityMode.VersionIncompatible));
    }
}

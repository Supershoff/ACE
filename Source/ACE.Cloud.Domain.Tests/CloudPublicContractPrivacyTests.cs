using System.Reflection;
using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Proves private account names and secret-bearing token material can never reach a public
/// contract shape (MKT-201's "Never expose ACE account names ... credentials, tokens"). Every
/// public-surface contract must implement <see cref="ICloudPublicContract"/>; this sweep
/// mechanically checks every such type instead of relying on reviewers to notice a leaked field.
/// </summary>
[TestClass]
public sealed class CloudPublicContractPrivacyTests
{
    private static readonly string[] ForbiddenPropertyNameFragments =
    [
        "accountname",
        "password",
        "secret",
        "token",
        "connectionstring",
        "credential",
        "hash",
    ];

    [TestMethod]
    public void PublicContractTypes_CarryNoForbiddenPropertyNames()
    {
        var publicContractTypes = typeof(ICloudPublicContract).Assembly
            .GetTypes()
            .Where(type => typeof(ICloudPublicContract).IsAssignableFrom(type) && !type.IsInterface)
            .ToList();

        Assert.IsNotEmpty(publicContractTypes, "Expected at least one public contract type to scan.");

        var violations = new List<string>();
        foreach (var type in publicContractTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var lowerName = property.Name.ToLowerInvariant();
                foreach (var forbidden in ForbiddenPropertyNameFragments)
                {
                    if (lowerName.Contains(forbidden, StringComparison.Ordinal))
                    {
                        violations.Add($"{type.Name}.{property.Name} matches forbidden fragment '{forbidden}'.");
                    }
                }
            }
        }

        Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void ActivityLedgerPayload_IsPrivateByDefault()
    {
        Assert.IsFalse(typeof(ICloudPublicContract).IsAssignableFrom(typeof(CloudActivityLedgerEventPayload)));
    }

    [TestMethod]
    public void CustodyOutboxPayload_IsPrivateByDefault()
    {
        Assert.IsFalse(typeof(ICloudPublicContract).IsAssignableFrom(typeof(CloudCustodyOutboxEventPayload)));
    }

    [TestMethod]
    public void CommandEnvelope_IsNotItselfAPublicContract()
    {
        Assert.IsFalse(typeof(ICloudPublicContract).IsAssignableFrom(typeof(CloudCommandEnvelope<CloudWithdrawalReservationCommand>)));
    }

    [TestMethod]
    public void PrivateEventEnvelope_IsNotItselfAPublicContract()
    {
        Assert.IsFalse(typeof(ICloudPublicContract).IsAssignableFrom(typeof(CloudEventEnvelope<CloudActivityLedgerEventPayload>)));
    }

    [TestMethod]
    public void ListingPublicSnapshot_IsMarkedPublic()
    {
        Assert.IsTrue(typeof(ICloudPublicContract).IsAssignableFrom(typeof(CloudListingPublicSnapshot)));
    }
}

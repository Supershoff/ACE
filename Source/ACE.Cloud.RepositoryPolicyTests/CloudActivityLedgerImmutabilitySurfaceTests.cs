using System.Reflection;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.RepositoryPolicyTests;

/// <summary>
/// Issue #34's Red: "Test... attempts to edit/delete events." Each Activity Ledger table (EVT-001's
/// "append-only ... no web UI can edit/delete it") is proven immutable here by enumerating its actual
/// public method surface rather than trusting a doc comment: every property setter is
/// <c>private</c>, and the type exposes no public method whose name suggests a mutation (Update,
/// Set, Edit, Delete, Remove) -- the only way to add one is the constructor, and there is no way to
/// change or remove a row already added. <see cref="CloudNotification"/> is deliberately excluded:
/// its own doc comment explains it is presentation state over the immutable ledger, not itself part
/// of the append-only ledger, so <see cref="CloudNotification.MarkRead"/> is an intentional exception
/// this test must not flag.
/// </summary>
[TestClass]
public sealed class CloudActivityLedgerImmutabilitySurfaceTests
{
    private static readonly Type[] LedgerEventTypes =
    [
        typeof(CloudActivityLedgerEvent),
        typeof(CloudAccountLinkLedgerEvent),
        typeof(CloudGlobalMaintenanceLedgerEvent),
        typeof(CloudAssetImportLedgerEvent),
        typeof(CloudSharingGrantLedgerEvent),
    ];

    private static readonly string[] ForbiddenMutatorNameFragments = ["Update", "Set", "Edit", "Delete", "Remove", "Replace"];

    [TestMethod]
    public void EveryLedgerEventType_ExposesNoPublicMutatorMethod()
    {
        var violations = new List<string>();

        foreach (var type in LedgerEventTypes)
        {
            var publicMethods = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName); // excludes property get_/set_ accessors themselves

            foreach (var method in publicMethods)
            {
                if (ForbiddenMutatorNameFragments.Any(fragment => method.Name.Contains(fragment, StringComparison.Ordinal)))
                {
                    violations.Add($"{type.Name}.{method.Name}");
                }
            }
        }

        Assert.HasCount(
            0,
            violations,
            "EVT-001: an Activity Ledger event is append-only and must expose no public mutator "
                + $"method ({string.Join(", ", violations)}).");
    }

    [TestMethod]
    public void EveryLedgerEventType_ExposesNoPublicPropertySetter()
    {
        var violations = new List<string>();

        foreach (var type in LedgerEventTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var setter = property.GetSetMethod(nonPublic: false);
                if (setter is not null)
                {
                    violations.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.HasCount(
            0,
            violations,
            "EVT-001: an Activity Ledger event's properties must have no public setter "
                + $"({string.Join(", ", violations)}).");
    }
}

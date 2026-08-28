using System.Runtime.CompilerServices;

// Exposes Player_CloudCustodian.BuildRuntimeEnchantments(...) -- a pure, WorldObject-free reduction
// of a live item's enchantment registry to the DEP-005 Frozen Enchantment preservation list -- so it
// can be unit-tested directly (AGENTS.md's verification-quality rule) without constructing a live
// WorldObject. No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Server.Tests")]

// ACE.Cloud.ServerSeamsTests holds the same WorldObject-free Cloud Custodian regression tests
// (issue #13 review, finding 3): it exists so the Cloud Mule CI test-discovery filter, which only
// runs test projects whose name matches "Cloud", actually executes them; ACE.Server.Tests is
// intentionally excluded from that filter (docs/agents/automation.md).
[assembly: InternalsVisibleTo("ACE.Cloud.ServerSeamsTests")]

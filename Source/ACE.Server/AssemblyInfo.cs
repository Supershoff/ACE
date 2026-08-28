using System.Runtime.CompilerServices;

// Exposes Player_CloudCustodian.BuildRuntimeEnchantments(...) -- a pure, WorldObject-free reduction
// of a live item's enchantment registry to the DEP-005 Frozen Enchantment preservation list -- so it
// can be unit-tested directly (AGENTS.md's verification-quality rule) without constructing a live
// WorldObject. No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Server.Tests")]

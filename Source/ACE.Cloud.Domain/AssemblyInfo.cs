using System.Runtime.CompilerServices;

// Exposes CloudReservation's internal Released(...) transition (a pure test seam, AGENTS.md) so the
// aggregate's own immutability/versioning behavior can be unit-tested directly, independent of
// CloudReservationPolicy (issue #7). No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Cloud.Domain.Tests")]

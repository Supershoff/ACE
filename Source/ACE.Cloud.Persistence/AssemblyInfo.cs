using System.Runtime.CompilerServices;

// Exposes CloudCustodyBoundary's internal fault-injection overloads (pure test seams, AGENTS.md)
// to the integration test project that proves crash-safety at every named boundary (issue #4).
// No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Cloud.PersistenceIntegrationTests")]

// Exposes CloudWithdrawalReservation's internal Release(...) mutator (a pure test seam, AGENTS.md)
// to ACE.Cloud.Backend.Tests, whose in-memory ICloudWithdrawalReservationService fake must be able
// to construct a realistic released-reservation state for endpoint tests without a real MariaDB
// (issue #33). No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Cloud.Backend.Tests")]

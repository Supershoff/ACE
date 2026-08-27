using System.Runtime.CompilerServices;

// Exposes CloudCustodyBoundary's internal fault-injection overloads (pure test seams, AGENTS.md)
// to the integration test project that proves crash-safety at every named boundary (issue #4).
// No production assembly is granted this visibility.
[assembly: InternalsVisibleTo("ACE.Cloud.PersistenceIntegrationTests")]

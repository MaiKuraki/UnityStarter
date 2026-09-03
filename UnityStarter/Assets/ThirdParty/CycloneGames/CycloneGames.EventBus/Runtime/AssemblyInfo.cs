using System.Runtime.CompilerServices;

// Test assemblies assert on internal state (deferred-removal counts, budget clamping,
// diagnostics plumbing) so those invariants stay testable without widening the public API.
// the EditMode suite (EventBusTests)
[assembly: InternalsVisibleTo("CycloneGames.EventBus.Tests.EditMode")]
// the lifecycle / integration suite
[assembly: InternalsVisibleTo("CycloneGames.EventBus.Tests.Integrations")]

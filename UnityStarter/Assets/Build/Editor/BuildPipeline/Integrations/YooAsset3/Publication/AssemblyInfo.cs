using System.Runtime.CompilerServices;

// The publication assembly is consumed by the pipeline test assembly (recovery tests use the
// internal journal/ownership types directly) and by the version-gated YooAsset3 integration
// assemblies. Without these grants the internal types that moved out of Build.Pipeline.Editor
// during the Publication split would stop being visible to their existing consumers.
[assembly: InternalsVisibleTo("Build.Pipeline.Tests.Editor")]
[assembly: InternalsVisibleTo("Build.Pipeline.Integrations.YooAsset3.Editor")]
[assembly: InternalsVisibleTo("Build.Pipeline.Integrations.YooAsset3.Tests.Editor")]

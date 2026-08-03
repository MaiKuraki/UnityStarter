using CycloneGames.Logging.Pipeline;
using CycloneGames.Logging.Unity.Editor;
using NUnit.Framework;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    /// <summary>
    /// Gives this assembly exclusive ownership of the global logger while its lifecycle and
    /// reliability tests intentionally create, stop, and reset LogPipeline instances.
    /// </summary>
    [SetUpFixture]
    internal sealed class LoggingEditorTestEnvironment
    {
        [OneTimeSetUp]
        public void SuspendAutomaticEditorBootstrap()
        {
            LoggingEditorBootstrap.SuspendForTests();
        }

        [OneTimeTearDown]
        public void RestoreAutomaticEditorBootstrap()
        {
            LoggingEditorBootstrap.ResumeAfterTests();
        }
    }
}

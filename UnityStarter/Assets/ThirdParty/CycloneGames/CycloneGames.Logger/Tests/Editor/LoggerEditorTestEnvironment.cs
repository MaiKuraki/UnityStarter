using CycloneGames.Logger.Editor;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    /// <summary>
    /// Gives this assembly exclusive ownership of the global logger while its lifecycle and
    /// reliability tests intentionally create, stop, and reset CLogger instances.
    /// </summary>
    [SetUpFixture]
    internal sealed class LoggerEditorTestEnvironment
    {
        [OneTimeSetUp]
        public void SuspendAutomaticEditorBootstrap()
        {
            LoggerEditorBootstrap.SuspendForTests();
        }

        [OneTimeTearDown]
        public void RestoreAutomaticEditorBootstrap()
        {
            LoggerEditorBootstrap.ResumeAfterTests();
        }
    }
}

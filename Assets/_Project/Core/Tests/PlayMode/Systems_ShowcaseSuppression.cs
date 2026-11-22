using NUnit.Framework;

namespace PoFootball.Tests
{
    /// <summary>
    /// Holds attract mode off for the whole PlayMode suite (see
    /// Systems_AttractModeView.Suppressed); the demo's own test switches it back on.
    /// </summary>
    [SetUpFixture]
    public sealed class Systems_ShowcaseSuppression
    {
        [OneTimeSetUp]
        public void SuppressAttractMode()
        {
            Views.Systems_AttractModeView.Suppressed = true;
        }

        [OneTimeTearDown]
        public void RestoreAttractMode()
        {
            Views.Systems_AttractModeView.Suppressed = false;
        }
    }
}

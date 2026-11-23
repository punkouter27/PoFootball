using NUnit.Framework;
using PoFootball.Views;

namespace PoFootball.Tests
{
    /// <summary>
    /// The attract mode's idle clock (Systems_AttractModeView.Advance): ordinary
    /// frames add up to the 30 s threshold, a frame over 0.25 s (a load, a stall,
    /// a throttled background tab) adds nothing.
    /// </summary>
    public sealed class Systems_AttractClockTests
    {
        [Test]
        public void OrdinaryFrames_AddUpToTheThreshold()
        {
            float clock = 0f;

            // 30 s of 60 Hz frames.
            for (int frame = 0; frame < 1800; frame++)
            {
                clock = Systems_AttractModeView.Advance(clock, 1f / 60f);
            }

            Assert.That(clock, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void ALongFrame_IsNotCountedAsIdleTime()
        {
            float clock = Systems_AttractModeView.Advance(10f, 0.016f);
            Assert.That(clock, Is.EqualTo(10.016f).Within(1e-4f));

            // A 5 s hitch (scene load, background tab) must not push the clock to 15 s.
            clock = Systems_AttractModeView.Advance(clock, 5f);
            Assert.That(clock, Is.EqualTo(10.016f).Within(1e-4f), "a 5 s frame was counted");

            // Just over the cap is skipped, exactly at the cap still counts.
            Assert.That(Systems_AttractModeView.Advance(0f, 0.2501f), Is.EqualTo(0f));
            Assert.That(
                Systems_AttractModeView.Advance(0f, Systems_AttractModeView.MAX_COUNTED_FRAME_SECONDS),
                Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void LongFramesAlone_NeverStartTheDemo()
        {
            float clock = 0f;

            for (int frame = 0; frame < 100; frame++)
            {
                clock = Systems_AttractModeView.Advance(clock, 1f);
            }

            Assert.That(clock, Is.Zero, "100 one-second frames started the 30 s demo clock");
            Assert.That(Systems_ShowcaseSettings.DEFAULT_IDLE_SECONDS, Is.EqualTo(30f));
        }
    }
}

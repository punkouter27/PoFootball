using NUnit.Framework;
using PoFootball.Models;
using PoFootball.Views;
using UnityEngine;

namespace PoFootball.Tests
{
    /// <summary>
    /// The broadcast director's rules about WHEN to cut: which shot each situation
    /// calls for, the holds and the hysteresis that stop the picture flapping, and
    /// the breakaway projection the wide shot hangs on.
    ///
    /// No camera and no scene. Systems_ShotDirector takes a struct of facts, which
    /// is the whole reason it takes a struct of facts.
    /// </summary>
    public sealed class Systems_ShotDirectorTests
    {
        private const float FRAME = 1f / 60f;

        private Systems_ShotDirector _director;

        [SetUp]
        public void SetUp()
        {
            _director = new Systems_ShotDirector();
        }

        private static Systems_ShotSituation Situation(
            bool enabled = true,
            bool passInFlight = false,
            bool kickInFlight = false,
            bool hasCarrier = false,
            bool carrierGone = false,
            bool passerHoldingBall = false,
            float nearestRusherMetres = float.MaxValue,
            bool goalLine = false)
        {
            return new Systems_ShotSituation(
                enabled, passInFlight, kickInFlight, hasCarrier, carrierGone,
                passerHoldingBall, nearestRusherMetres, goalLine);
        }

        private Systems_ShotKind Run(Systems_ShotSituation situation, float seconds)
        {
            Systems_ShotKind shot = _director.Current;

            for (float elapsed = 0f; elapsed < seconds; elapsed += FRAME)
            {
                shot = _director.Advance(situation, FRAME);
            }

            return shot;
        }

        [Test]
        public void Disabled_AlwaysFramesTheDefaultShot()
        {
            Assert.That(
                _director.Advance(Situation(enabled: false, passInFlight: true), FRAME),
                Is.EqualTo(Systems_ShotKind.Default));
        }

        [Test]
        public void SwitchingOff_DropsADirectedShotImmediately()
        {
            _director.Advance(Situation(passInFlight: true), FRAME);

            Assert.That(
                _director.Advance(Situation(enabled: false), FRAME),
                Is.EqualTo(Systems_ShotKind.Default));
        }

        [Test]
        public void PassRelease_CutsToBallInAirWithoutWaitingOutTheHold()
        {
            Systems_ShotSituation pocket = Situation(
                hasCarrier: true, passerHoldingBall: true, nearestRusherMetres: 3f);

            Assert.That(_director.Advance(pocket, FRAME), Is.EqualTo(Systems_ShotKind.Pocket));

            Assert.That(
                _director.Advance(Situation(passInFlight: true), FRAME),
                Is.EqualTo(Systems_ShotKind.BallInAir));
        }

        [Test]
        public void Kick_IsFramedWhileTheBallIsInTheAir()
        {
            Assert.That(
                _director.Advance(Situation(kickInFlight: true), FRAME),
                Is.EqualTo(Systems_ShotKind.Kick));
        }

        [Test]
        public void Pocket_EntersInsideTheEnterDistanceAndHoldsUntilTheExitDistance()
        {
            float between = (Systems_ShotDirector.POCKET_ENTER_METRES
                + Systems_ShotDirector.POCKET_EXIT_METRES) * 0.5f;

            Assert.That(
                Run(Situation(hasCarrier: true, passerHoldingBall: true, nearestRusherMetres: between), 1f),
                Is.EqualTo(Systems_ShotKind.Default),
                "A rusher between the two distances must not bring the pocket shot in.");

            Assert.That(
                _director.Advance(
                    Situation(hasCarrier: true, passerHoldingBall: true,
                        nearestRusherMetres: Systems_ShotDirector.POCKET_ENTER_METRES - 0.1f),
                    FRAME),
                Is.EqualTo(Systems_ShotKind.Pocket));

            Assert.That(
                Run(Situation(hasCarrier: true, passerHoldingBall: true, nearestRusherMetres: between), 1f),
                Is.EqualTo(Systems_ShotKind.Pocket),
                "Once in, the same distance must keep it — that is the hysteresis.");

            Assert.That(
                Run(Situation(hasCarrier: true, passerHoldingBall: true,
                    nearestRusherMetres: Systems_ShotDirector.POCKET_EXIT_METRES + 0.5f), 1f),
                Is.EqualTo(Systems_ShotKind.Default));
        }

        [Test]
        public void JudgementShot_IsHeldForTheMinimumBeforeTheDirectorChangesItsMind()
        {
            _director.Advance(
                Situation(hasCarrier: true, passerHoldingBall: true, nearestRusherMetres: 3f), FRAME);

            Systems_ShotSituation clear = Situation(hasCarrier: true);

            Assert.That(
                Run(clear, Systems_ShotDirector.MIN_HOLD_SECONDS * 0.5f),
                Is.EqualTo(Systems_ShotKind.Pocket));

            Assert.That(
                Run(clear, Systems_ShotDirector.MIN_HOLD_SECONDS),
                Is.EqualTo(Systems_ShotKind.Default));
        }

        [Test]
        public void Breakaway_OutlivesAWobbleInTheProjectionButNotTheWhistle()
        {
            Assert.That(
                _director.Advance(Situation(hasCarrier: true, carrierGone: true), FRAME),
                Is.EqualTo(Systems_ShotKind.Breakaway));

            Assert.That(
                Run(Situation(hasCarrier: true), Systems_ShotDirector.BREAKAWAY_RELEASE_SECONDS * 0.5f),
                Is.EqualTo(Systems_ShotKind.Breakaway),
                "A pursuer stepping inside the reach for a stride must not cut the shot.");

            Assert.That(
                Run(Situation(hasCarrier: true), Systems_ShotDirector.BREAKAWAY_RELEASE_SECONDS),
                Is.EqualTo(Systems_ShotKind.Default));
        }

        [Test]
        public void GoalLine_IsCalledFromTheFirstFrameAfterAReset()
        {
            _director.Reset();

            Assert.That(
                _director.Advance(Situation(goalLine: true), FRAME),
                Is.EqualTo(Systems_ShotKind.GoalLine));
        }

        [Test]
        public void IsBreakaway_TrueForARunnerWithNobodyBetweenHimAndTheGoal()
        {
            Vector2[] positions = { new Vector2(0f, -20f) };
            float[] speeds = { 8f };

            Assert.That(
                Systems_ShotDirector.IsBreakaway(
                    new Vector2(0f, 10f), new Vector2(0f, 8f), 1f,
                    Systems_FieldModel.ATTACKING_GOAL_LINE_Y, positions, speeds, 1),
                Is.True);
        }

        [Test]
        public void IsBreakaway_FalseWithADefenderOnThePath()
        {
            Vector2[] positions = { new Vector2(0f, 30f) };
            float[] speeds = { 8f };

            Assert.That(
                Systems_ShotDirector.IsBreakaway(
                    new Vector2(0f, 10f), new Vector2(0f, 8f), 1f,
                    Systems_FieldModel.ATTACKING_GOAL_LINE_Y, positions, speeds, 1),
                Is.False);
        }

        [Test]
        public void IsBreakaway_FalseForAJog()
        {
            Assert.That(
                Systems_ShotDirector.IsBreakaway(
                    new Vector2(0f, 10f),
                    new Vector2(0f, Systems_SimConstants.BREAKAWAY_MIN_SPEED * 0.5f),
                    1f, Systems_FieldModel.ATTACKING_GOAL_LINE_Y,
                    new Vector2[0], new float[0], 0),
                Is.False);
        }
    }
}

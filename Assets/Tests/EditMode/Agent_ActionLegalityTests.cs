using MessagePipe;
using NUnit.Framework;
using PoFootball.Agents;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;

namespace PoFootball.Tests
{
    /// <summary>
    /// The mask's promise, asserted: every discrete action it leaves enabled is one
    /// the game then honours. An enabled action that is silently refused is
    /// exploration spent on nothing, and no series in TensorBoard shows it.
    ///
    /// Both sides read Agent_ActionLegality, which is the point of it — so these
    /// hold the predicates to the MODELS' own rules (Systems_PlayModel.LatchCall,
    /// Systems_BallSystem.TryLateral), the place a mismatch can actually live. The
    /// release test is the one that found the revision 17 defect: a quarterback with
    /// no pass on and a back behind him had release enabled, and pitching along an
    /// aim that pointed downfield did nothing.
    /// </summary>
    public sealed class Agent_ActionLegalityTests
    {
        private sealed class StubPublisher<T> : IPublisher<T>
        {
            public void Publish(T message)
            {
            }
        }

        private sealed class StubPlayer : Systems_IPlayerHandle
        {
            public int Id { get; set; }

            public Systems_PlayerRole Role { get; set; }

            public Systems_TeamSide Side => Systems_RoleTable.SideOf(Role);

            public Vector2 Position { get; set; }

            public Vector2 Velocity { get; set; }

            public Vector2 Facing { get; set; } = Vector2.up;

            public bool IsCarrier { get; private set; }

            public float Fatigue => 0f;

            public void RestBetweenPlays() { }

            public void Freeze() { }

            public void ResetTo(Vector2 position) => Position = position;

            public void SetCarrier(bool isCarrier) => IsCarrier = isCarrier;

            public void SetTeamColor(Color color) { }

            public void Nudge(Vector2 velocity) => Velocity = velocity;
        }

        /// <summary>Snaps from midfield and from the opponent's 15, in and out of field-goal range.</summary>
        private static readonly float[] LinesOfScrimmage =
        {
            0f,
            Systems_FieldModel.ATTACKING_GOAL_LINE_Y - (15f * Systems_FieldModel.YARD),
        };

        private static readonly Systems_PlayKind[] Kinds =
        {
            Systems_PlayKind.Scrimmage,
            Systems_PlayKind.TwoPoint,
            Systems_PlayKind.Kickoff,
            Systems_PlayKind.ExtraPoint,
        };

        [Test]
        public void EveryCallTheMaskEnables_IsACallThePlayModelLatches()
        {
            bool[] enabled = new bool[Agent_ActionLegality.CALL_BRANCH_SIZE];
            int checkedCalls = 0;

            foreach (float lineOfScrimmage in LinesOfScrimmage)
            {
                foreach (Systems_PlayKind kind in Kinds)
                {
                    for (int down = 1; down <= Systems_GameRules.DOWNS_PER_SERIES; down++)
                    {
                        for (int preSnap = 0; preSnap < Systems_SimConstants.PRESNAP_TICKS; preSnap++)
                        {
                            checkedCalls += CheckEnabledCallsLatch(
                                lineOfScrimmage, kind, down, preSnap, -1, enabled);
                        }

                        for (int live = 0; live <= Systems_SimConstants.DROPBACK_TICKS + 20; live++)
                        {
                            checkedCalls += CheckEnabledCallsLatch(
                                lineOfScrimmage, kind, down, Systems_SimConstants.PRESNAP_TICKS,
                                live, enabled);
                        }
                    }
                }
            }

            // Proves the loops reached both windows rather than passing vacuously.
            Assert.That(checkedCalls, Is.GreaterThan(100));
        }

        /// <summary>
        /// The run-fit drill (Systems_RandomSpotProvider.LESSON_RUN_FIT): LatchCall
        /// refuses a pass, so the mask must not offer one — and must still offer
        /// every run, or the drill's dropback decision would have nothing to pick.
        /// </summary>
        [Test]
        public void InTheRunFitDrill_NoPassIsOffered_AndEveryRunLatches()
        {
            bool[] enabled = new bool[Agent_ActionLegality.CALL_BRANCH_SIZE];

            for (int live = 0; live <= Systems_SimConstants.DROPBACK_TICKS + 20; live++)
            {
                CheckEnabledCallsLatch(
                    0f, Systems_PlayKind.Scrimmage, 2, Systems_SimConstants.PRESNAP_TICKS,
                    live, enabled, true);
            }

            Agent_ActionLegality.WriteCallMask(
                PlayAt(0f, Systems_PlayKind.Scrimmage, 2, Systems_SimConstants.PRESNAP_TICKS,
                    Systems_SimConstants.DROPBACK_TICKS, true),
                enabled);

            Assert.That(enabled[(int)Systems_PlayCall.Pass], Is.False);
            Assert.That(enabled[(int)Systems_PlayCall.PassDeep], Is.False);
            Assert.That(enabled[(int)Systems_PlayCall.HandoffHalfback], Is.True);
            Assert.That(enabled[(int)Systems_PlayCall.HandoffFullback], Is.True);
            Assert.That(enabled[(int)Systems_PlayCall.KeepQuarterback], Is.True);
        }

        [Test]
        public void BeforeTheDropback_OnlyNoCallIsEnabled()
        {
            bool[] enabled = new bool[Agent_ActionLegality.CALL_BRANCH_SIZE];
            Systems_PlayModel play = PlayAt(0f, Systems_PlayKind.Scrimmage, 1,
                Systems_SimConstants.PRESNAP_TICKS, 0);

            Agent_ActionLegality.WriteCallMask(play, enabled);

            Assert.That(enabled[(int)Systems_PlayCall.None], Is.True);

            for (int callIndex = 1; callIndex < enabled.Length; callIndex++)
            {
                Assert.That(enabled[callIndex], Is.False, $"{(Systems_PlayCall)callIndex} at tick 0");
            }
        }

        [Test]
        public void AFieldGoal_IsOnlyOfferedInRange()
        {
            bool[] enabled = new bool[Agent_ActionLegality.CALL_BRANCH_SIZE];

            Agent_ActionLegality.WriteCallMask(
                PlayAt(0f, Systems_PlayKind.Scrimmage, 4, 0, -1), enabled);
            Assert.That(enabled[(int)Systems_PlayCall.FieldGoal], Is.False, "from midfield");
            Assert.That(enabled[(int)Systems_PlayCall.Punt], Is.True, "from midfield");

            Agent_ActionLegality.WriteCallMask(
                PlayAt(LinesOfScrimmage[1], Systems_PlayKind.Scrimmage, 4, 0, -1), enabled);
            Assert.That(enabled[(int)Systems_PlayCall.FieldGoal], Is.True, "from the 15");
        }

        /// <summary>
        /// The revision 17 defect, pinned from both ends: the bare aimed pitch fails
        /// with the aim downfield, and the one the handler now makes does not, for
        /// any aim at all.
        /// </summary>
        [Test]
        public void EveryReleaseTheMaskEnables_ThrowsOrPitches_WhateverTheAim()
        {
            int directions = 16;

            for (int direction = 0; direction < directions; direction++)
            {
                float angle = direction * (2f * Mathf.PI / directions);
                Vector2 aim = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));

                Fixture fixture = new Fixture(Systems_PlayCall.HandoffHalfback);

                bool releaseEnabled =
                    Agent_ActionLegality.CanReleasePass(fixture.Play, fixture.Ball, fixture.Quarterback)
                    || Agent_ActionLegality.CanLateral(
                        fixture.Play, fixture.Ball, fixture.BallSystem, fixture.Quarterback);

                Assert.That(releaseEnabled, Is.True, "a back is behind the quarterback");
                Assert.That(
                    Agent_ActionLegality.CanReleasePass(fixture.Play, fixture.Ball, fixture.Quarterback),
                    Is.False, "a handoff call has no pass on");

                Assert.That(
                    Agent_ActionLegality.TryPitch(fixture.BallSystem, fixture.Quarterback, aim),
                    Is.True, $"the enabled release did nothing with the aim at {aim}");
            }

            Fixture downfield = new Fixture(Systems_PlayCall.HandoffHalfback);
            Assert.That(
                downfield.BallSystem.TryLateral(downfield.Quarterback, Vector2.up), Is.False,
                "the aimed pitch alone finds nobody downfield — the defect TryPitch exists for");
        }

        [Test]
        public void NoReleaseIsEnabled_ForAPlayerWithoutTheBall()
        {
            Fixture fixture = new Fixture(Systems_PlayCall.Pass);
            fixture.Quarterback.SetCarrier(false);

            Assert.That(
                Agent_ActionLegality.CanReleasePass(fixture.Play, fixture.Ball, fixture.Quarterback),
                Is.False);
            Assert.That(
                Agent_ActionLegality.CanLateral(
                    fixture.Play, fixture.Ball, fixture.BallSystem, fixture.Quarterback),
                Is.False);
        }

        /// <summary>
        /// For one state: every call the mask enables, latched on an identical
        /// model at the tick the agent would apply it (the dropback, or this
        /// pre-snap tick), must stick. Returns how many enabled calls it checked.
        /// </summary>
        private static int CheckEnabledCallsLatch(
            float lineOfScrimmage, Systems_PlayKind kind, int down, int preSnap, int live,
            bool[] enabled, bool runsOnly = false)
        {
            Agent_ActionLegality.WriteCallMask(
                PlayAt(lineOfScrimmage, kind, down, preSnap, live, runsOnly), enabled);

            int checkedCalls = 0;

            for (int callIndex = 1; callIndex < enabled.Length; callIndex++)
            {
                if (!enabled[callIndex])
                {
                    continue;
                }

                int appliedAt = live < 0 ? -1 : Mathf.Max(live, Systems_SimConstants.DROPBACK_TICKS);
                Systems_PlayModel play = PlayAt(lineOfScrimmage, kind, down, preSnap, appliedAt, runsOnly);
                Systems_PlayCall call = (Systems_PlayCall)callIndex;

                play.LatchCall(call);

                Assert.That(
                    play.Call, Is.EqualTo(call),
                    $"{call} was enabled ({kind}, down {down}, pre-snap {preSnap}, live {live}) "
                    + "and the play model refused it");
                checkedCalls++;
            }

            return checkedCalls;
        }

        /// <summary>A play in a given state: <paramref name="live"/> below zero is still before the snap.</summary>
        private static Systems_PlayModel PlayAt(
            float lineOfScrimmage, Systems_PlayKind kind, int down, int preSnap, int live,
            bool runsOnly = false)
        {
            Systems_PlayModel play = new Systems_PlayModel();
            play.BeginEpisode(lineOfScrimmage, lineOfScrimmage, down, 10f, kind, false, runsOnly);

            for (int tick = 0; tick < preSnap; tick++)
            {
                play.AdvancePreSnap();
            }

            if (live >= 0)
            {
                play.Snap();

                for (int tick = 0; tick < live; tick++)
                {
                    play.AdvanceTick();
                }
            }

            return play;
        }

        /// <summary>The base I-formation at midfield, live, the quarterback holding it.</summary>
        private sealed class Fixture
        {
            public readonly Systems_PlayModel Play = new Systems_PlayModel();
            public readonly Systems_BallModel Ball = new Systems_BallModel();
            public readonly Systems_BallSystem BallSystem;
            public readonly StubPlayer Quarterback;

            public Fixture(Systems_PlayCall call)
            {
                Systems_PlayerRegistry registry = new Systems_PlayerRegistry();
                Systems_FormationSelection formations = new Systems_FormationSelection();

                for (int slot = 0; slot < Systems_PlayerRegistry.CAPACITY; slot++)
                {
                    Systems_FormationSlot definition = formations.GetSlot(slot);
                    registry.Register(slot, new StubPlayer
                    {
                        Id = slot,
                        Role = definition.Role,
                        Position = new Vector2(definition.OffsetX, definition.OffsetY),
                    });
                }

                Quarterback = (StubPlayer)registry.Get(Systems_Formation.QUARTERBACK_SLOT_INDEX);
                BallSystem = new Systems_BallSystem(
                    Ball, Play, registry, new Systems_SeededSkillModel(),
                    new StubPublisher<Systems_PassThrownMessage>(),
                    new StubPublisher<Systems_PassCaughtMessage>());

                Play.BeginEpisode(0f, Quarterback.Position.y, 1, Systems_GameRules.YARDS_TO_GAIN);
                Play.Snap();

                for (int tick = 0; tick < Systems_SimConstants.DROPBACK_TICKS; tick++)
                {
                    Play.AdvanceTick();
                }

                Play.LatchCall(call);
                Quarterback.SetCarrier(true);
                Ball.ResetForSnap(Systems_Formation.QUARTERBACK_SLOT_INDEX, Quarterback.Position);
            }
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;

namespace PoFootball.Tests
{
    /// <summary>
    /// The QB challenge's pure pieces: the playbook, the route geometry, the human
    /// control model and the three-minute game length. The snap gate is pinned in
    /// Systems_RefereeTests, beside the rest of the snap.
    /// </summary>
    public sealed class Systems_QbChallengeTests
    {
        // --- Playbook ------------------------------------------------------------

        [Test]
        public void ThePlayDesignSlots_AreTheFormationBooksSlots()
        {
            Assert.That(Systems_PlayDesign.TIGHT_END_SLOT, Is.EqualTo(Systems_FormationBook.TIGHT_END_SLOT_INDEX));
            Assert.That(Systems_PlayDesign.LEFT_RECEIVER_SLOT, Is.EqualTo(Systems_FormationBook.WIDE_RECEIVER_LEFT_SLOT_INDEX));
            Assert.That(Systems_PlayDesign.RIGHT_RECEIVER_SLOT, Is.EqualTo(Systems_FormationBook.WIDE_RECEIVER_RIGHT_SLOT_INDEX));
            Assert.That(Systems_PlayDesign.FULLBACK_SLOT, Is.EqualTo(Systems_FormationBook.FULLBACK_SLOT_INDEX));
            Assert.That(Systems_PlayDesign.HALFBACK_SLOT, Is.EqualTo(Systems_FormationBook.HALFBACK_SLOT_INDEX));
        }

        [Test]
        public void ThePlaybook_HoldsMoreThanAHandOfDistinctlyNamedPlays()
        {
            Assert.That(Systems_Playbook.Plays.Length, Is.GreaterThan(Systems_Playbook.HAND_SIZE));

            HashSet<string> names = new HashSet<string>();

            foreach (Systems_PlayDesign play in Systems_Playbook.Plays)
            {
                Assert.That(names.Add(play.Name), Is.True, $"two plays are called {play.Name}");
                Assert.That(play.IsKick, Is.False, $"{play.Name}: kicks are dealt only on fourth down");
            }
        }

        [Test]
        public void EveryRun_NamesTheBackItIsFor_AndEveryPass_HasReadsAtTwoDepths()
        {
            foreach (Systems_PlayDesign play in Systems_Playbook.Plays)
            {
                int handoffs = 0;
                int routes = 0;
                bool shortRead = false;
                bool longRead = false;

                foreach (int slot in new[] { 5, 6, 7, 9, 10 })
                {
                    Systems_Route route = play.RouteOf(slot);

                    if (route == Systems_Route.Handoff)
                    {
                        handoffs++;
                        Assert.That(slot == 9 || slot == 10, Is.True, $"{play.Name}: only a back takes a handoff");
                        Assert.That(
                            Systems_Formation.HandoffSlotOf(play.Call), Is.EqualTo(slot),
                            $"{play.Name}: the handoff route and the call name different backs");
                    }
                    else if (route != Systems_Route.Block)
                    {
                        routes++;
                        shortRead |= IsShort(route);
                        longRead |= !IsShort(route);
                    }
                }

                if (play.IsPass)
                {
                    Assert.That(handoffs, Is.Zero, $"{play.Name}: a pass with a handoff back");
                    Assert.That(routes, Is.GreaterThanOrEqualTo(3), $"{play.Name}: fewer than three receivers out");
                    Assert.That(shortRead && longRead, Is.True, $"{play.Name}: every read is at one depth");
                }
                else if (play.Call == Systems_PlayCall.KeepQuarterback)
                {
                    Assert.That(handoffs, Is.Zero, $"{play.Name}: a keeper with a handoff back");
                }
                else
                {
                    Assert.That(handoffs, Is.EqualTo(1), $"{play.Name}: a run needs exactly one ball carrier");
                    Assert.That(routes, Is.Zero, $"{play.Name}: receivers on a run should block");
                }
            }
        }

        // --- Route geometry --------------------------------------------------------

        private const float LINE = 0f;

        [Test]
        public void ASlant_GoesUpThenInside()
        {
            Vector2 start = new Vector2(12f, LINE);
            bool leg = false;

            Vector2 early = Systems_RouteBook.TargetFor(
                Systems_Route.Slant, start, LINE, start, ref leg, out _);
            Assert.That(early.x, Is.EqualTo(start.x).Within(0.01f), "the stem is straight upfield");
            Assert.That(leg, Is.False);

            Vector2 pastStem = new Vector2(12f, LINE + (3f * Systems_FieldModel.YARD));
            Vector2 broken = Systems_RouteBook.TargetFor(
                Systems_Route.Slant, start, LINE, pastStem, ref leg, out _);
            Assert.That(leg, Is.True);
            Assert.That(broken.x, Is.LessThan(pastStem.x), "a receiver on the right breaks left, inside");
            Assert.That(broken.y, Is.GreaterThan(pastStem.y), "and still upfield");
        }

        [Test]
        public void AnOut_BreaksToTheSidelineHeLinedUpNearer()
        {
            Vector2 start = new Vector2(-12f, LINE);
            Vector2 atDepth = new Vector2(-12f, LINE + (7f * Systems_FieldModel.YARD));
            bool leg = false;

            Vector2 target = Systems_RouteBook.TargetFor(
                Systems_Route.Out, start, LINE, atDepth, ref leg, out _);

            Assert.That(target.x, Is.LessThan(atDepth.x), "the left receiver's out goes left");
            Assert.That(target.y, Is.EqualTo(atDepth.y).Within(0.01f), "flat, not upfield");
        }

        [Test]
        public void ACompletedLeg_StaysCompleted_EvenBackShortOfTheStem()
        {
            Vector2 start = new Vector2(12f, LINE);
            bool leg = true;

            Vector2 target = Systems_RouteBook.TargetFor(
                Systems_Route.Dig, start, LINE, start, ref leg, out _);

            Assert.That(leg, Is.True);
            Assert.That(target.x, Is.LessThan(start.x), "no turning back to the stem once broken");
        }

        [Test]
        public void ACurl_SettlesOnItsSpot()
        {
            Vector2 start = new Vector2(12f, LINE);
            Vector2 deep = new Vector2(12f, LINE + (10.5f * Systems_FieldModel.YARD));
            bool leg = false;

            Vector2 target = Systems_RouteBook.TargetFor(
                Systems_Route.Curl, start, LINE, deep, ref leg, out float settle);

            Assert.That(settle, Is.GreaterThan(0f), "a settling route eases on");
            Assert.That(target.y, Is.LessThan(deep.y), "and comes back toward the quarterback");
        }

        [Test]
        public void NoRoute_LeavesTheField()
        {
            foreach (Systems_Route route in System.Enum.GetValues(typeof(Systems_Route)))
            {
                foreach (float x in new[] { -24f, 24f })
                {
                    Vector2 start = new Vector2(x, LINE);
                    bool leg = false;

                    Vector2 target = Systems_RouteBook.TargetFor(route, start, LINE, start, ref leg, out _);

                    Assert.That(Mathf.Abs(target.x), Is.LessThanOrEqualTo(Systems_FieldModel.HALF_WIDTH),
                        $"{route} from x={x}");
                }
            }
        }

        // --- Human control model ----------------------------------------------------

        [Test]
        public void ATappedSpot_IsASprint_ThenAStopOnIt()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);
            Assert.That(control.CommandToward(Vector2.zero, Vector2.zero, 8), Is.EqualTo(Vector2.zero),
                "a quarterback nobody has tapped for stands in the pocket");

            control.SetMoveTarget(new Vector2(0f, 20f), 8);
            Vector2 sprint = control.CommandToward(Vector2.zero, Vector2.zero, 8);
            Assert.That(sprint.magnitude, Is.EqualTo(1f).Within(1e-4f), "far away is a sprint");
            Assert.That(sprint.y, Is.GreaterThan(0.99f), "toward the spot");

            control.SetPointer(new Vector2(30f, 0f));
            Assert.That(control.CommandToward(Vector2.zero, Vector2.zero, 8), Is.EqualTo(sprint),
                "a pointer moving is not a tap: it steers nobody");

            float rampMiddle = Systems_HumanControlModel.STAND_RADIUS_METRES
                + (Systems_HumanControlModel.ARRIVAL_RAMP_METRES / 2f);
            Vector2 nearly = new Vector2(0f, 20f - rampMiddle);
            Assert.That(control.CommandToward(nearly, Vector2.up, 8).magnitude, Is.EqualTo(0.5f).Within(1e-3f),
                "he eases off as he arrives");

            Vector2 there = new Vector2(0f, 19.6f);
            Assert.That(control.CommandToward(there, Vector2.up, 8), Is.EqualTo(Vector2.zero), "and stops on it");
            Assert.That(control.HasMoveTarget, Is.False);
            Assert.That(control.CommandToward(there, Vector2.zero, 8), Is.EqualTo(Vector2.zero), "and stays stopped");
        }

        [Test]
        public void ANewCarrier_KeepsHisRun_NotTheQuarterbacksSpot()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);
            control.SetMoveTarget(new Vector2(0f, -3f), 8);
            control.CommandToward(Vector2.zero, Vector2.zero, 8);

            Vector2 kept = control.CommandToward(new Vector2(10f, 15f), new Vector2(6f, 0f), 7);
            Assert.That(kept.x, Is.EqualTo(1f).Within(1e-4f), "a receiver who caught it on the run keeps running, flat out");
            Assert.That(control.HasMoveTarget, Is.False, "and the quarterback's drop is not his");

            control.SetMoveTarget(new Vector2(10f, 30f), 7);
            Assert.That(control.CommandToward(new Vector2(10f, 15f), new Vector2(6f, 0f), 7).y, Is.GreaterThan(0.99f),
                "until the human taps a spot for him");

            control.BeginDown(true);
            Assert.That(control.CommandToward(Vector2.zero, Vector2.zero, 7), Is.EqualTo(Vector2.zero),
                "nothing outlives the down");
        }

        [Test]
        public void ASpotTappedWhileThePassIsInTheAir_IsForTheCatcher()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);
            control.CommandToward(Vector2.zero, Vector2.zero, 8);

            control.SetMoveTarget(new Vector2(10f, 40f), -1);
            Vector2 command = control.CommandToward(new Vector2(10f, 20f), new Vector2(5f, 0f), 6);

            Assert.That(command.y, Is.GreaterThan(0.99f), "the man who catches it runs where the human tapped");
        }

        [Test]
        public void TwoTapsOnOneSpotInTheWindow_AreOneDoubleTap_NotTwoSingles()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);
            Vector2 spot = new Vector2(5f, 30f);

            control.Tap(spot, 10f);
            Assert.That(control.TakeClick(), Is.True, "the first tap acts at once");
            Assert.That(control.TakeDoubleTap(), Is.False);

            control.Tap(spot + new Vector2(1f, 0f), 10.2f);
            Assert.That(control.TakeClick(), Is.False, "the second is not a single tap as well");
            Assert.That(control.TakeDoubleTap(), Is.True);
            Assert.That(control.DoubleTapWorld, Is.EqualTo(spot + new Vector2(1f, 0f)));

            control.Tap(spot, 10.3f);
            Assert.That(control.TakeDoubleTap(), Is.False, "a third tap starts over");
            Assert.That(control.TakeClick(), Is.True);

            control.Tap(spot, 10.3f + Systems_HumanControlModel.DOUBLE_TAP_SECONDS + 0.05f);
            Assert.That(control.TakeDoubleTap(), Is.False, "too slow");

            control.Tap(spot + new Vector2(0f, Systems_HumanControlModel.DOUBLE_TAP_METRES + 1f), 10.8f);
            Assert.That(control.TakeDoubleTap(), Is.False, "too far apart");

            control.Tap(spot, 20f);
            control.ForgetTap();
            control.Tap(spot, 20.1f);
            Assert.That(control.TakeDoubleTap(), Is.False, "a tap spent on the hike pairs with nothing");
        }

        [Test]
        public void ASpotPass_IsQueuedOnce_AndForgottenAtTheNextDown()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);
            control.QueuePass(new Vector2(3f, 25f));

            Assert.That(control.TakePass(out Vector2 spot), Is.True);
            Assert.That(spot, Is.EqualTo(new Vector2(3f, 25f)));
            Assert.That(control.TakePass(out _), Is.False);

            control.NoteIntendedSpot(new Vector2(3f, 25f));
            control.BeginDown(true);
            Assert.That(control.TakeIntendedSpot(out _), Is.False, "a spot never outlives its down");
        }

        [Test]
        public void ThePlayCeiling_IsTheTrainersUnlessRaised_AndResetsEveryEpisode()
        {
            Systems_PlayModel play = new Systems_PlayModel();
            play.BeginEpisode(0f, 0f, 1, 10f);
            Assert.That(play.TickCeiling, Is.EqualTo(Systems_PlayModel.MAX_PHYSICS_TICKS));

            play.SetTickCeiling(Systems_QbChallengeSystem.HUMAN_TICK_CEILING);
            Assert.That(play.TickCeiling, Is.EqualTo(Systems_QbChallengeSystem.HUMAN_TICK_CEILING));

            play.BeginEpisode(0f, 0f, 1, 10f);
            Assert.That(play.TickCeiling, Is.EqualTo(Systems_PlayModel.MAX_PHYSICS_TICKS),
                "a raised ceiling never outlives the human's down");
        }

        [Test]
        public void TheHuman_SteersOnlyHisOwnBallCarrier_OnHisOwnDown()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);

            Assert.That(control.IsControlling(true, true, true), Is.False, "not until it is his down");

            control.SetHumanOnOffense(true);
            Assert.That(control.IsControlling(true, true, true), Is.True);
            Assert.That(control.IsControlling(true, false, true), Is.False, "not a man without the ball");
            Assert.That(control.IsControlling(false, true, true), Is.False, "not a defender returning a pick");
            Assert.That(control.IsControlling(true, true, false), Is.False, "not before the snap");

            Assert.That(
                new Systems_HumanControlModel(false).IsControlling(true, true, true), Is.False,
                "a disabled model never steers anybody");
        }

        [Test]
        public void ClicksPicksAndActions_AreTakenOnce()
        {
            Systems_HumanControlModel control = new Systems_HumanControlModel(true);

            control.Click(Vector2.one);
            control.Pick(3);
            control.QueueAction(7);

            Assert.That(control.TakeClick(), Is.True);
            Assert.That(control.TakeClick(), Is.False);
            Assert.That(control.TakePick(), Is.EqualTo(3));
            Assert.That(control.TakePick(), Is.EqualTo(-1));
            Assert.That(control.TakeAction(), Is.EqualTo(7));
            Assert.That(control.TakeAction(), Is.EqualTo(-1));
        }

        // --- Game length --------------------------------------------------------------

        [Test]
        public void TheQbChallenge_IsOneThreeMinutePeriodWithNoOvertime()
        {
            Systems_GameModel game = new Systems_GameModel(Systems_GameLength.QbChallenge);
            game.KickOff(Systems_TeamId.Home, 0f);

            Assert.That(game.SecondsRemaining, Is.EqualTo(180f));
            Assert.That(game.Length.Periods, Is.EqualTo(1));
            Assert.That(game.Length.HasOvertime, Is.False);
            Assert.That(game.Length.HalftimeAfter, Is.Zero);
        }

        [Test]
        public void TheStandardGame_IsUnchanged()
        {
            Systems_GameModel game = new Systems_GameModel();

            Assert.That(game.SecondsRemaining, Is.EqualTo(Systems_GameRules.QUARTER_SECONDS));
            Assert.That(game.Length.Periods, Is.EqualTo(Systems_GameRules.QUARTER_COUNT));
            Assert.That(game.Length.HalftimeAfter, Is.EqualTo(2));
            Assert.That(game.Length.HasOvertime, Is.True);
        }

        private static bool IsShort(Systems_Route route)
        {
            switch (route)
            {
                case Systems_Route.Slant:
                case Systems_Route.Out:
                case Systems_Route.Hitch:
                case Systems_Route.Flat:
                case Systems_Route.Swing:
                case Systems_Route.Drag:
                    return true;
                default:
                    return false;
            }
        }
    }
}

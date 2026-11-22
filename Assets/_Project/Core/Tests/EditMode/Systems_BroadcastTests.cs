using System;
using NUnit.Framework;
using PoFootball.Agents;
using PoFootball.Models;
using PoFootball.Systems;
using PoFootball.Views;

namespace PoFootball.Tests
{
    /// <summary>
    /// The numbers and the words the broadcast graphics put on screen: the win
    /// probability, the expected points behind it, the scripted call sheet's odds,
    /// the hit meter's impulse and the play-by-play.
    ///
    /// WHY THESE EXIST. Every one of these is a claim made to a viewer — "78% run",
    /// "HOME 63% to win", "brought down by the linebacker" — and a graphic that
    /// states a wrong number with confidence is worse than no graphic. Each is a
    /// pure function, so each is pinned here without a scene.
    /// </summary>
    public sealed class Systems_BroadcastTests
    {
        private const float TOLERANCE = 1e-4f;

        // --- Expected points -------------------------------------------------

        [Test]
        public void ExpectedPoints_RisesTowardTheGoal()
        {
            float ownTen = Systems_ExpectedPoints.Of(YardLine(10f), 1, 10f);
            float midfield = Systems_ExpectedPoints.Of(YardLine(50f), 1, 10f);
            float redZone = Systems_ExpectedPoints.Of(YardLine(90f), 1, 10f);

            Assert.That(ownTen, Is.LessThan(midfield));
            Assert.That(midfield, Is.LessThan(redZone));
            Assert.That(redZone, Is.GreaterThan(4f), "first and ten at the 10 is worth most of a touchdown");
        }

        [Test]
        public void ExpectedPoints_FallsWithEachDownUsed()
        {
            float first = Systems_ExpectedPoints.Of(YardLine(50f), 1, 10f);
            float third = Systems_ExpectedPoints.Of(YardLine(50f), 3, 10f);

            Assert.That(third, Is.LessThan(first));
        }

        // --- Win probability ---------------------------------------------------

        [Test]
        public void NormalCdf_MatchesKnownValues()
        {
            Assert.That(Systems_WinProbability.NormalCdf(0f), Is.EqualTo(0.5f).Within(TOLERANCE));
            Assert.That(Systems_WinProbability.NormalCdf(1f), Is.EqualTo(0.8413f).Within(TOLERANCE));
            Assert.That(Systems_WinProbability.NormalCdf(-1.96f), Is.EqualTo(0.025f).Within(TOLERANCE));
        }

        [Test]
        public void WinProbability_TiedAtKickoffWithNothingToGain_IsEven()
        {
            float chance = Systems_WinProbability.ForOffense(
                0, Systems_WinProbability.REGULATION_SECONDS, 0f);

            Assert.That(chance, Is.EqualTo(0.5f).Within(TOLERANCE));
        }

        [Test]
        public void WinProbability_TheSameLeadMattersMoreLate()
        {
            float early = Systems_WinProbability.ForOffense(7, Systems_WinProbability.REGULATION_SECONDS, 0f);
            float late = Systems_WinProbability.ForOffense(7, 30f, 0f);

            Assert.That(early, Is.GreaterThan(0.5f));
            Assert.That(late, Is.GreaterThan(early));
            Assert.That(late, Is.GreaterThan(0.95f));
        }

        [Test]
        public void WinProbability_PossessionValueFadesAsTheClockDies()
        {
            // Tied, a field goal's worth of possession: worth something with a
            // quarter left, nothing once the clock is gone.
            float withTime = Systems_WinProbability.ForOffense(0, 120f, 3f);
            float atZero = Systems_WinProbability.ForOffense(0, 0f, 3f);

            Assert.That(withTime, Is.GreaterThan(0.5f));
            Assert.That(atZero, Is.EqualTo(0.5f).Within(TOLERANCE));
        }

        [Test]
        public void SecondsLeft_CountsTheQuartersStillToPlay()
        {
            float secondQuarter = Systems_WinProbability.SecondsLeft(2, 30f, false);

            Assert.That(secondQuarter, Is.EqualTo(30f + (2f * Systems_GameRules.QUARTER_SECONDS)).Within(TOLERANCE));
            Assert.That(Systems_WinProbability.SecondsLeft(1, 50f, true), Is.EqualTo(50f).Within(TOLERANCE));
        }

        [Test]
        public void WinProbabilityModel_RecordsSwingsAndTheBiggestOne()
        {
            Systems_WinProbabilityModel model = new Systems_WinProbabilityModel();
            model.Reset(0.5f);
            model.Record(0.55f, 0.3f, Systems_TeamId.Home);
            model.Record(0.30f, -2.1f, Systems_TeamId.Home);
            model.Record(0.35f, 0.4f, Systems_TeamId.Away);

            Assert.That(model.Count, Is.EqualTo(4));
            Assert.That(model.HomeNow, Is.EqualTo(0.35f).Within(TOLERANCE));
            Assert.That(model.LastSwing, Is.EqualTo(0.05f).Within(TOLERANCE));
            Assert.That(model.BiggestSwing, Is.EqualTo(-0.25f).Within(TOLERANCE));
            Assert.That(model.BiggestSwingIndex, Is.EqualTo(2));
        }

        [Test]
        public void WinProbabilityModel_ThinsRatherThanOverflows()
        {
            Systems_WinProbabilityModel model = new Systems_WinProbabilityModel();
            model.Reset(0.5f);

            for (int play = 0; play < Systems_WinProbabilityModel.CAPACITY * 2; play++)
            {
                model.Record(0.5f, 0f, Systems_TeamId.Home);
            }

            Assert.That(model.Count, Is.LessThanOrEqualTo(Systems_WinProbabilityModel.CAPACITY));
            Assert.That(model.Count, Is.GreaterThan(Systems_WinProbabilityModel.CAPACITY / 2));
        }

        // --- The scripted call sheet ---------------------------------------------

        /// <summary>
        /// Agent_PlayCaller.Chances must be exactly the distribution Choose draws
        /// from — checked against Choose itself, over enough draws that a two-point
        /// disagreement on any call fails.
        /// </summary>
        [TestCase(1, 10f, 70f)]
        [TestCase(2, 6f, 55f)]
        [TestCase(3, 8f, 60f)]
        [TestCase(3, 2f, 60f)]
        [TestCase(1, 1f, 40f)]
        [TestCase(2, 10f, 15f)]
        [TestCase(4, 9f, 40f)]
        public void PlayCallerChances_MatchChoose(int down, float yardsToGo, float yardsToGoal)
        {
            const int DRAWS = 20000;

            float[] chances = new float[Systems_CallSheet.CALL_SLOTS];
            Agent_PlayCaller.Chances(down, yardsToGo, yardsToGoal, false, chances);

            Random random = new Random(1234);
            Func<float> next = () => (float)random.NextDouble();
            int[] counts = new int[Systems_CallSheet.CALL_SLOTS];

            for (int draw = 0; draw < DRAWS; draw++)
            {
                counts[(int)Agent_PlayCaller.Choose(down, yardsToGo, yardsToGoal, false, next)]++;
            }

            float total = 0f;

            for (int call = 0; call < chances.Length; call++)
            {
                total += chances[call];
                Assert.That(
                    (float)counts[call] / DRAWS,
                    Is.EqualTo(chances[call]).Within(0.02f),
                    $"{(Systems_PlayCall)call} on {down} & {yardsToGo} at {yardsToGoal} to go");
            }

            Assert.That(total, Is.EqualTo(1f).Within(TOLERANCE));
        }

        [Test]
        public void PlayCallerChances_DeepShotMovesThePassMass()
        {
            float[] chances = new float[Systems_CallSheet.CALL_SLOTS];
            Agent_PlayCaller.Chances(3, 9f, 60f, true, chances);

            Assert.That(chances[(int)Systems_PlayCall.Pass], Is.EqualTo(0f));
            Assert.That(chances[(int)Systems_PlayCall.PassDeep], Is.EqualTo(1f).Within(TOLERANCE));
        }

        [Test]
        public void CallSheet_KeepsWhatWasReportedAndWhichPlayItWasFor()
        {
            Systems_CallSheet sheet = new Systems_CallSheet();
            float[] fronts = new float[Systems_FormationBook.DefensiveFormationCount];
            fronts[(int)Systems_DefensiveFormation.ZeroBlitz] = 0.4f;

            sheet.ReportDefense(12, fronts, Systems_CallSource.Policy);
            fronts[(int)Systems_DefensiveFormation.ZeroBlitz] = 0f;

            Assert.That(sheet.DefenseEpisode, Is.EqualTo(12));
            Assert.That(sheet.DefenseChance((int)Systems_DefensiveFormation.ZeroBlitz),
                Is.EqualTo(0.4f).Within(TOLERANCE), "the sheet copies; the caller may reuse its array");

            sheet.ReportOffense(12, null, Systems_CallSource.Policy);
            Assert.That(sheet.OffenseSource, Is.EqualTo(Systems_CallSource.PolicyChoiceOnly));
        }

        // --- Hit meter ---------------------------------------------------------------

        [Test]
        public void HitImpulse_IsReducedMassTimesClosingSpeed()
        {
            // 100 kg into 100 kg at 6 m/s: reduced mass 50 kg, 300 N·s.
            Assert.That(Systems_HitMeter.ImpulseOf(100f, 100f, 6f), Is.EqualTo(300f).Within(TOLERANCE));
            Assert.That(Systems_HitMeter.ImpulseOf(100f, 100f, -2f), Is.EqualTo(0f));
        }

        // --- Play-by-play ---------------------------------------------------------------

        [Test]
        public void Commentary_UnresolvedPlay_SaysNothing()
        {
            Systems_PlayFacts facts = new Systems_PlayFacts();
            facts.Clear();

            Assert.That(Systems_CommentaryWriter.Describe(facts), Is.Empty);
        }

        [Test]
        public void Commentary_CompletionForAFirstDown()
        {
            Systems_PlayFacts facts = Scrimmage(3, 7f, 34f);
            facts.Thrown = true;
            facts.Caught = true;
            facts.CatcherRole = Systems_PlayerRole.TightEnd;
            facts.YardsGained = 12f;
            facts.Result = Systems_DownResult.FirstDown;
            facts.Outcome = Systems_PlayOutcome.Tackle;
            facts.Call = Systems_PlayCall.Pass;

            string line = Systems_CommentaryWriter.Describe(facts);

            StringAssert.StartsWith("3rd & 7 at the HOME 34.", line);
            StringAssert.Contains("the tight end", line);
            StringAssert.Contains("12 yards", line);
            StringAssert.Contains("First down.", line);
        }

        [Test]
        public void Commentary_TouchdownCallsTheScore()
        {
            Systems_PlayFacts facts = Scrimmage(1, 10f, 80f);
            facts.HasCarrier = true;
            facts.CarrierRole = Systems_PlayerRole.RunningBack;
            facts.Call = Systems_PlayCall.HandoffHalfback;
            facts.YardsGained = 20f;
            facts.Outcome = Systems_PlayOutcome.Touchdown;
            facts.Result = Systems_DownResult.Touchdown;
            facts.ScoringTeam = Systems_TeamId.Home;
            facts.PointsScored = 6;
            facts.HomeScore = 13;
            facts.AwayScore = 10;

            string line = Systems_CommentaryWriter.Describe(facts);

            StringAssert.Contains("The halfback", line);
            StringAssert.Contains("HOME!", line);
            StringAssert.Contains("HOME 13, AWAY 10.", line);
        }

        [Test]
        public void Commentary_SackNamesTheLossAndWhoGotThere()
        {
            Systems_PlayFacts facts = Scrimmage(2, 8f, 45f);
            facts.Outcome = Systems_PlayOutcome.Sack;
            facts.Result = Systems_DownResult.Sack;
            facts.HasTackler = true;
            facts.TacklerRole = Systems_PlayerRole.Linebacker;
            facts.YardsGained = -6f;

            string line = Systems_CommentaryWriter.Describe(facts);

            StringAssert.Contains("Sacked!", line);
            StringAssert.Contains("linebacker", line);
            StringAssert.Contains("loss of 6 yards", line);
        }

        [Test]
        public void Commentary_BigSwingIsCalledOut()
        {
            Systems_PlayFacts facts = Scrimmage(1, 10f, 50f);
            facts.Intercepted = true;
            facts.CatcherRole = Systems_PlayerRole.Cornerback;
            facts.Outcome = Systems_PlayOutcome.Interception;
            facts.Result = Systems_DownResult.Interception;
            facts.HomeWinSwing = -0.2f;
            facts.HomeWinChance = 0.3f;

            string line = Systems_CommentaryWriter.Describe(facts);

            StringAssert.Contains("Intercepted by the corner!", line);
            StringAssert.Contains("AWAY now 70% to win.", line);
        }

        [Test]
        public void Commentary_SpotIsNamedForTheSideOfTheField()
        {
            Assert.That(Systems_CommentaryWriter.Spot(YardLine(50f), Systems_TeamId.Home), Is.EqualTo("at midfield"));
            Assert.That(Systems_CommentaryWriter.Spot(YardLine(20f), Systems_TeamId.Away), Is.EqualTo("at the AWAY 20"));
            Assert.That(Systems_CommentaryWriter.Spot(YardLine(70f), Systems_TeamId.Away), Is.EqualTo("at the HOME 30"));
        }

        [Test]
        public void Commentary_TheSamePlayReadsTheSameEveryTime()
        {
            Systems_PlayFacts facts = Scrimmage(1, 10f, 40f);
            facts.HasCarrier = true;
            facts.CarrierRole = Systems_PlayerRole.Fullback;
            facts.YardsGained = 3f;
            facts.Outcome = Systems_PlayOutcome.Tackle;
            facts.PlayNumber = 17;

            Assert.That(Systems_CommentaryWriter.Describe(facts), Is.EqualTo(Systems_CommentaryWriter.Describe(facts)));
        }

        private static Systems_PlayFacts Scrimmage(int down, float yardsToGo, float yardLine)
        {
            Systems_PlayFacts facts = new Systems_PlayFacts();
            facts.Clear();
            facts.Offense = Systems_TeamId.Home;
            facts.Kind = Systems_PlayKind.Scrimmage;
            facts.Down = down;
            facts.YardsToGo = yardsToGo;
            facts.LineY = YardLine(yardLine);
            facts.Resolved = true;
            return facts;
        }

        /// <summary>Attacking-frame Y of a yard line counted from the offense's own goal.</summary>
        private static float YardLine(float yards)
        {
            return Systems_FieldModel.OWN_GOAL_LINE_Y + (yards * Systems_FieldModel.YARD);
        }
    }
}

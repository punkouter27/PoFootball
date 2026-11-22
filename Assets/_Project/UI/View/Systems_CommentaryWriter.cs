using System.Text;
using PoFootball.Models;
using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// Turns one play's facts into a sentence of play-by-play: "3rd & 7 at the
    /// HOME 34. The quarterback finds the tight end for 12 yards. First down."
    ///
    /// A TEMPLATE GRAMMAR, NOT A MODEL. Every line is assembled from the facts
    /// the simulation already has — the situation at the snap, who threw, caught,
    /// carried and tackled, and the down result — so it cannot say anything that
    /// did not happen, and it costs nothing a phone would notice. A language model
    /// was considered and rejected for the ticker: a network round trip per play,
    /// an API key that would have to ship inside the APK, and a sentence that
    /// might name a play that never occurred.
    ///
    /// VARIETY WITHOUT RANDOMNESS. A ticker that says "runs for 4 yards" ninety
    /// times is a ticker nobody reads, so most verbs come in sets of three or four.
    /// The pick is a hash of the play number and a salt — deterministic, so a
    /// fixed-seed game reads the same each time it is watched, and it draws from
    /// no stream the simulation uses (a commentary line that consumed an RNG draw
    /// would change the game being commented on).
    ///
    /// Pure and static: a test builds a Systems_PlayFacts and reads the string.
    /// </summary>
    public static class Systems_CommentaryWriter
    {
        /// <summary>A play that moves the home team's chance this far gets a line about it.</summary>
        public const float BIG_SWING = 0.15f;

        /// <summary>Air yards past which a completion is called a deep shot.</summary>
        public const float DEEP_AIR_YARDS = 20f;

        /// <summary>A hit closing faster than this, m/s, is called a big one.</summary>
        public const float BIG_HIT_SPEED = 6.5f;

        /// <summary>Yards behind the goal line that the kick is taken from: end zone plus the hold.</summary>
        private const float FIELD_GOAL_EXTRA_YARDS = 17f;

        /// <summary>
        /// Gains only. A capture read "the halfback picks up and loses 1 yard", so a
        /// run that went backwards always "runs", whichever verb the hash picked.
        /// </summary>
        private static readonly string[] RunVerbs = { "runs", "carries", "takes it", "goes" };
        private static readonly string[] CatchVerbs = { "finds", "hits", "connects with", "goes to" };
        private static readonly string[] TackleVerbs = { "brought down by", "stopped by", "wrapped up by", "dropped by" };
        private static readonly string[] BigHitVerbs = { "Big hit from", "Leveled by", "Hammered by" };
        private static readonly string[] TouchdownCalls = { "TOUCHDOWN", "INTO THE END ZONE", "SIX POINTS" };

        private static readonly StringBuilder Builder = new StringBuilder(256);

        /// <summary>
        /// The line for this play, or an empty string when there is nothing worth
        /// saying (an unresolved play). Allocates the returned string and nothing
        /// else — once a play, never per frame.
        /// </summary>
        public static string Describe(Systems_PlayFacts facts)
        {
            if (facts == null || !facts.Resolved)
            {
                return string.Empty;
            }

            Builder.Clear();

            if (facts.Result == Systems_DownResult.EndOfGame)
            {
                AppendFinal(facts);
                return Builder.ToString();
            }

            if (facts.Outcome == Systems_PlayOutcome.DeadBallFoul)
            {
                AppendSituation(facts);
                Builder.Append("Flag before the snap: ");
                Builder.Append(Lower(Systems_DisplayText.FoulName(facts.Foul)));
                Builder.Append(", ").Append(Systems_DisplayText.TeamName(facts.FoulTeam)).Append('.');
                AppendTail(facts);
                return Builder.ToString();
            }

            switch (facts.Kind)
            {
                case Systems_PlayKind.Kickoff:
                    AppendKickoff(facts);
                    break;
                case Systems_PlayKind.Punt:
                    AppendSituation(facts);
                    AppendPunt(facts);
                    break;
                case Systems_PlayKind.FieldGoal:
                    AppendFieldGoal(facts);
                    break;
                case Systems_PlayKind.ExtraPoint:
                    AppendExtraPoint(facts);
                    break;
                case Systems_PlayKind.TwoPoint:
                    AppendTwoPoint(facts);
                    break;
                case Systems_PlayKind.KneelDown:
                    Builder.Append(Systems_DisplayText.TeamName(facts.Offense)).Append(" takes a knee.");
                    break;
                case Systems_PlayKind.Spike:
                    Builder.Append("Spiked to stop the clock.");
                    break;
                default:
                    AppendSituation(facts);
                    AppendScrimmage(facts);
                    break;
            }

            AppendTail(facts);
            return Builder.ToString();
        }

        // --- The play ----------------------------------------------------------

        private static void AppendScrimmage(Systems_PlayFacts facts)
        {
            if (facts.Outcome == Systems_PlayOutcome.Sack || facts.Result == Systems_DownResult.Sack)
            {
                Builder.Append("Sacked! ");

                if (facts.HasTackler)
                {
                    Builder.Append("The ").Append(RoleName(facts.TacklerRole)).Append(" gets there");
                }
                else
                {
                    Builder.Append("The quarterback goes down");
                }

                Builder.Append(" for a loss of ").Append(Yards(-facts.YardsGained)).Append('.');
                return;
            }

            if (facts.Intercepted || facts.Outcome == Systems_PlayOutcome.Interception)
            {
                Builder.Append("Intercepted");

                if (facts.Intercepted)
                {
                    Builder.Append(" by the ").Append(RoleName(facts.CatcherRole));
                }

                Builder.Append('!');
                AppendReturn(facts);
                return;
            }

            if (facts.Thrown && facts.Caught)
            {
                Builder.Append("The quarterback ").Append(Pick(CatchVerbs, facts.PlayNumber, 1));
                Builder.Append(" the ").Append(RoleName(facts.CatcherRole));

                if (facts.AirYards >= DEEP_AIR_YARDS)
                {
                    Builder.Append(" deep");
                }

                Builder.Append(' ');
                AppendGain(facts.YardsGained);
                AppendTackle(facts);
                return;
            }

            if (facts.Thrown || facts.Outcome == Systems_PlayOutcome.Incompletion)
            {
                Builder.Append("Pass");

                if (facts.HasTarget)
                {
                    Builder.Append(" for the ").Append(RoleName(facts.TargetRole));
                }

                Builder.Append(" falls incomplete.");
                return;
            }

            // A run, or a called pass the quarterback took off with.
            string runner = facts.HasCarrier
                ? RoleName(facts.CarrierRole)
                : RunnerOf(facts.Call);

            Builder.Append("The ").Append(runner).Append(' ');

            if (facts.Call.IsPass() && facts.CarrierRole == Systems_PlayerRole.Quarterback)
            {
                Builder.Append("scrambles ");
            }
            else if (Mathf.RoundToInt(facts.YardsGained) > 0)
            {
                Builder.Append(Pick(RunVerbs, facts.PlayNumber, 2)).Append(' ');
            }
            else
            {
                Builder.Append("runs ");
            }

            AppendGain(facts.YardsGained);

            if (facts.Outcome == Systems_PlayOutcome.FumbleLost || facts.Result == Systems_DownResult.FumbleLost)
            {
                Builder.Remove(Builder.Length - 1, 1);
                Builder.Append(" and FUMBLES! ")
                    .Append(Systems_DisplayText.TeamName(facts.Offense.Opponent()))
                    .Append(" recovers.");
                return;
            }

            AppendTackle(facts);
        }

        private static void AppendGain(float yards)
        {
            int rounded = Mathf.RoundToInt(yards);

            if (rounded == 0)
            {
                Builder.Append("for no gain.");
            }
            else if (rounded > 0)
            {
                Builder.Append("for ").Append(Yards(rounded)).Append('.');
            }
            else
            {
                Builder.Append("and loses ").Append(Yards(-rounded)).Append('.');
            }
        }

        private static void AppendTackle(Systems_PlayFacts facts)
        {
            if (facts.Outcome == Systems_PlayOutcome.OutOfBounds)
            {
                Builder.Append(" Out of bounds.");
                return;
            }

            if (!facts.HasTackler || facts.Result == Systems_DownResult.Touchdown)
            {
                return;
            }

            Builder.Append(' ');

            if (facts.HitSpeed >= BIG_HIT_SPEED)
            {
                Builder.Append(Pick(BigHitVerbs, facts.PlayNumber, 3))
                    .Append(" the ").Append(RoleName(facts.TacklerRole)).Append('!');
                return;
            }

            Builder.Append('(').Append(Pick(TackleVerbs, facts.PlayNumber, 4))
                .Append(" the ").Append(RoleName(facts.TacklerRole)).Append(')');
        }

        private static void AppendReturn(Systems_PlayFacts facts)
        {
            int returned = Mathf.RoundToInt(facts.ReturnYards);

            if (returned > 0)
            {
                Builder.Append(" Returned ").Append(Yards(returned)).Append('.');
            }
        }

        private static void AppendKickoff(Systems_PlayFacts facts)
        {
            Builder.Append(Systems_DisplayText.TeamName(facts.Offense)).Append(" kicks off. ");

            switch (facts.Result)
            {
                case Systems_DownResult.Touchback:
                    Builder.Append("Touchback.");
                    return;
                case Systems_DownResult.KickRecovered:
                    Builder.Append("Onside — and ")
                        .Append(Systems_DisplayText.TeamName(facts.Offense))
                        .Append(" recovers!");
                    return;
            }

            int returned = Mathf.RoundToInt(facts.ReturnYards);
            Builder.Append(returned > 0 ? "Returned " + Yards(returned) + "." : "Fielded and downed.");
        }

        private static void AppendPunt(Systems_PlayFacts facts)
        {
            if (facts.Result == Systems_DownResult.BlockedKick)
            {
                Builder.Append("The punt is BLOCKED!");
                return;
            }

            Builder.Append(Systems_DisplayText.TeamName(facts.Offense)).Append(" punts.");

            if (facts.Result == Systems_DownResult.Touchback)
            {
                Builder.Append(" Into the end zone, touchback.");
                return;
            }

            AppendReturn(facts);
        }

        private static void AppendFieldGoal(Systems_PlayFacts facts)
        {
            int distance = Mathf.RoundToInt(
                Systems_FieldModel.YardsToAttackingGoal(facts.LineY) + FIELD_GOAL_EXTRA_YARDS);

            Builder.Append(distance).Append("-yard field goal attempt... ");

            switch (facts.Result)
            {
                case Systems_DownResult.FieldGoalGood:
                    Builder.Append("it's GOOD.");
                    break;
                case Systems_DownResult.BlockedKick:
                    Builder.Append("BLOCKED!");
                    break;
                default:
                    Builder.Append("no good.");
                    break;
            }
        }

        private static void AppendExtraPoint(Systems_PlayFacts facts)
        {
            switch (facts.Result)
            {
                case Systems_DownResult.ExtraPointGood:
                    Builder.Append("The extra point is good.");
                    break;
                case Systems_DownResult.DefensiveTry:
                    Builder.Append("Blocked and returned — two points for ")
                        .Append(Systems_DisplayText.TeamName(facts.Offense.Opponent())).Append('!');
                    break;
                default:
                    Builder.Append("The extra point is NO GOOD.");
                    break;
            }
        }

        private static void AppendTwoPoint(Systems_PlayFacts facts)
        {
            Builder.Append("Going for two... ");

            switch (facts.Result)
            {
                case Systems_DownResult.TwoPointGood:
                    Builder.Append("they get it!");
                    break;
                case Systems_DownResult.DefensiveTry:
                    Builder.Append("turned over and run back — two for ")
                        .Append(Systems_DisplayText.TeamName(facts.Offense.Opponent())).Append('!');
                    break;
                default:
                    Builder.Append("stopped short.");
                    break;
            }
        }

        // --- Around the play ---------------------------------------------------

        private static void AppendSituation(Systems_PlayFacts facts)
        {
            bool goalToGo = Systems_FieldModel.YardsToAttackingGoal(facts.LineY) <= facts.YardsToGo;

            Builder.Append(Systems_DisplayText.DownAndDistance(facts.Down, facts.YardsToGo, goalToGo));
            Builder.Append(' ').Append(Spot(facts.LineY, facts.Offense)).Append(". ");
        }

        /// <summary>The score, the result's consequence, the flag and the clock.</summary>
        private static void AppendTail(Systems_PlayFacts facts)
        {
            switch (facts.Result)
            {
                case Systems_DownResult.Touchdown:
                    Builder.Append(' ')
                        .Append(Pick(TouchdownCalls, facts.PlayNumber, 5))
                        .Append(", ").Append(Systems_DisplayText.TeamName(facts.ScoringTeam)).Append('!');
                    AppendScore(facts);
                    break;
                case Systems_DownResult.Safety:
                    Builder.Append(" SAFETY — two points ")
                        .Append(Systems_DisplayText.TeamName(facts.ScoringTeam)).Append('.');
                    AppendScore(facts);
                    break;
                case Systems_DownResult.FieldGoalGood:
                case Systems_DownResult.ExtraPointGood:
                case Systems_DownResult.TwoPointGood:
                case Systems_DownResult.DefensiveTry:
                    AppendScore(facts);
                    break;
                case Systems_DownResult.FirstDown:
                    Builder.Append(" First down.");
                    break;
                case Systems_DownResult.TurnoverOnDowns:
                    Builder.Append(" Turnover on downs.");
                    break;
                case Systems_DownResult.EndOfQuarter:
                    Builder.Append(" That ends the quarter.");
                    break;
            }

            if (facts.Foul != Systems_FoulType.None && facts.Outcome != Systems_PlayOutcome.DeadBallFoul)
            {
                Builder.Append(" Flag: ").Append(Lower(Systems_DisplayText.FoulName(facts.Foul)))
                    .Append(", ").Append(Systems_DisplayText.TeamName(facts.FoulTeam));

                if (!facts.PenaltyAccepted)
                {
                    Builder.Append(" — declined.");
                }
                else if (facts.PenaltyYards > 0.05f)
                {
                    Builder.Append(", ").Append(Yards(Mathf.RoundToInt(facts.PenaltyYards))).Append('.');
                }
                else
                {
                    Builder.Append('.');
                }
            }

            if (Mathf.Abs(facts.HomeWinSwing) >= BIG_SWING)
            {
                bool homeGained = facts.HomeWinSwing > 0f;
                Systems_TeamId gainer = homeGained ? Systems_TeamId.Home : Systems_TeamId.Away;
                float chance = homeGained ? facts.HomeWinChance : 1f - facts.HomeWinChance;

                Builder.Append(" Huge swing: ").Append(Systems_DisplayText.TeamName(gainer))
                    .Append(" now ").Append(Mathf.RoundToInt(chance * 100f)).Append("% to win.");
            }

            if (facts.TwoMinuteWarning)
            {
                Builder.Append(" Two-minute warning.");
            }

            if (facts.TimeoutCalled)
            {
                Builder.Append(" Timeout, ").Append(Systems_DisplayText.TeamName(facts.TimeoutTeam)).Append('.');
            }
        }

        private static void AppendScore(Systems_PlayFacts facts)
        {
            Builder.Append(' ')
                .Append(Systems_DisplayText.TeamName(Systems_TeamId.Home)).Append(' ').Append(facts.HomeScore)
                .Append(", ")
                .Append(Systems_DisplayText.TeamName(Systems_TeamId.Away)).Append(' ').Append(facts.AwayScore)
                .Append('.');
        }

        private static void AppendFinal(Systems_PlayFacts facts)
        {
            Builder.Append("That's the game. Final: ");

            if (facts.HomeScore == facts.AwayScore)
            {
                Builder.Append("a tie, ").Append(facts.HomeScore).Append(" all.");
                return;
            }

            bool homeWon = facts.HomeScore > facts.AwayScore;
            Systems_TeamId winner = homeWon ? Systems_TeamId.Home : Systems_TeamId.Away;

            Builder.Append(Systems_DisplayText.TeamName(winner)).Append(' ')
                .Append(homeWon ? facts.HomeScore : facts.AwayScore).Append(", ")
                .Append(Systems_DisplayText.TeamName(winner.Opponent())).Append(' ')
                .Append(homeWon ? facts.AwayScore : facts.HomeScore).Append('.');
        }

        // --- Words ---------------------------------------------------------------

        /// <summary>
        /// "at the HOME 34", from the offense's side: its own half is named for
        /// it, the far half for the defense, and the middle is midfield.
        /// </summary>
        public static string Spot(float lineY, Systems_TeamId offense)
        {
            int yardLine = Mathf.RoundToInt(
                (lineY - Systems_FieldModel.OWN_GOAL_LINE_Y) / Systems_FieldModel.YARD);

            if (yardLine == 50)
            {
                return "at midfield";
            }

            return yardLine < 50
                ? "at the " + Systems_DisplayText.TeamName(offense) + " " + Mathf.Max(1, yardLine)
                : "at the " + Systems_DisplayText.TeamName(offense.Opponent()) + " " + Mathf.Max(1, 100 - yardLine);
        }

        public static string RoleName(Systems_PlayerRole role)
        {
            switch (role)
            {
                case Systems_PlayerRole.Quarterback: return "quarterback";
                case Systems_PlayerRole.RunningBack: return "halfback";
                case Systems_PlayerRole.Fullback: return "fullback";
                case Systems_PlayerRole.WideReceiver: return "receiver";
                case Systems_PlayerRole.TightEnd: return "tight end";
                case Systems_PlayerRole.OffensiveLine: return "lineman";
                case Systems_PlayerRole.DefensiveLine: return "defensive lineman";
                case Systems_PlayerRole.Linebacker: return "linebacker";
                case Systems_PlayerRole.Cornerback: return "corner";
                case Systems_PlayerRole.Safety: return "safety";
                default: return "player";
            }
        }

        private static string RunnerOf(Systems_PlayCall call)
        {
            switch (call)
            {
                case Systems_PlayCall.HandoffFullback: return "fullback";
                case Systems_PlayCall.HandoffHalfback: return "halfback";
                default: return "quarterback";
            }
        }

        private static string Yards(float yards)
        {
            int rounded = Mathf.Max(0, Mathf.RoundToInt(yards));
            return rounded == 1 ? "1 yard" : rounded + " yards";
        }

        private static string Lower(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.ToLowerInvariant();
        }

        /// <summary>
        /// A variant chosen by the play number and a per-slot salt, so the verbs in
        /// one sentence do not move in lockstep. Knuth's multiplicative hash; the
        /// distribution only has to look varied to a reader.
        /// </summary>
        private static string Pick(string[] options, int playNumber, int salt)
        {
            uint hash = unchecked(((uint)playNumber * 2654435761u) ^ ((uint)salt * 40503u));
            return options[(int)(hash % (uint)options.Length)];
        }
    }
}

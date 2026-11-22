using PoFootball.Models;

namespace PoFootball.Rewards
{
    /// <summary>
    /// The dense half of the reward: paid every physics tick on ball movement.
    ///
    /// This exists specifically to defeat the do-nothing optimum. With 22 agents
    /// and minimal rules, a purely terminal reward leaves a very large basin where
    /// no side gains anything by acting, and self-play settles into it. Paying per
    /// yard means standing still earns the offense only the time cost.
    ///
    /// Pure functions — no state, no allocation.
    /// </summary>
    public static class Reward_Progress
    {
        /// <summary>
        /// Reward for one physics tick. yardsDelta is the carrier's forward
        /// progress since the previous tick, signed toward the offense's goal.
        ///
        /// The time cost is divided by DECISION_PERIOD so that accumulating it
        /// every tick totals the same as charging it once per decision.
        /// </summary>
        public static float PerTick(Systems_TeamSide side, float yardsDelta)
        {
            float progress = yardsDelta * Systems_SimConstants.YARD_REWARD_SCALE;

            if (side == Systems_TeamSide.Offense)
            {
                return progress
                    + (Systems_SimConstants.TIME_COST_PER_DECISION
                        / Systems_SimConstants.DECISION_PERIOD);
            }

            return -progress;
        }

        /// <summary>
        /// Whether ball movement on this tick counts as progress at all.
        ///
        /// NOT WHILE A PASS IS IN THE AIR. The ball's Y is mirrored every tick of
        /// its flight, so paying on it handed the offense up to half a touchdown
        /// for a throw that then fell incomplete: a forty-yard miss netted +0.30
        /// against -0.42 for an eight-yard run that ended in a tackle. The caller
        /// holds its baseline at the release point instead, so a catch is paid
        /// the whole air distance on the tick it is made, and a miss or a pick —
        /// both of which end the play on the spot — is paid nothing.
        ///
        /// NOR WHILE A KICK IS IN THE AIR OR THE BALL IS ON THE GROUND (revision
        /// 14). Forty-five yards of punt is field position, which the terminal
        /// reward prices; it is not the offense driving the ball, and paying it
        /// densely would make every punt worth more than a drive. The caller also
        /// re-baselines whenever the ball changes sides
        /// (Systems_BallModel.PossessionEpoch), so the distance across a turnover
        /// or a kick is never paid to anybody — only the return that follows it.
        /// </summary>
        public static bool CountsAsProgress(Systems_BallState ballState)
        {
            return ballState == Systems_BallState.Held
                || ballState == Systems_BallState.Incomplete;
        }

        /// <summary>
        /// Whether the caller should HOLD its baseline at the snap instead of paying
        /// this tick: a scrimmage down, the ball still with the side that snapped it,
        /// no forward pass yet, inside the throw window, and the ball at or behind
        /// the line of scrimmage.
        ///
        /// THE DROPBACK WAS CHARGED BY THE YARD (football_base26). Paid per tick, a
        /// quarterback retreating seven yards to throw lost 7 x YARD_REWARD_SCALE
        /// before the call even latched (DROPBACK_TICKS), and early in a run, when no
        /// pass is completed, nothing ever paid it back. The policy learned to run
        /// forward from the snap, crossed the line, and so made every pass illegal
        /// (Agent_ActionLegality.CanReleasePass): 8.1M steps, Pass/AttemptRate
        /// 0.25-0.44 with ~2% completed and NO incompletions, interceptions or sacks
        /// — the ball was simply never thrown, and four realism games had one
        /// completion between them.
        ///
        /// Holding the baseline changes no total: the shaping still telescopes, so a
        /// catch, or a scramble past the line, is paid from the snap spot the moment
        /// it happens, and a play that ends in the backfield is priced by the
        /// terminal reward's tackle-for-loss, as a sack always was. What it removes is
        /// the per-tick penalty on the movement every pass needs.
        /// </summary>
        public static bool HoldsBaselineInBackfield(
            bool isScrimmage,
            bool possessionChanged,
            bool forwardPassThrown,
            int physicsTick,
            float ballY,
            float lineOfScrimmageY)
        {
            return isScrimmage
                && !possessionChanged
                && !forwardPassThrown
                && physicsTick <= Systems_SimConstants.THROW_WINDOW_TICKS
                && ballY <= lineOfScrimmageY;
        }
    }
}

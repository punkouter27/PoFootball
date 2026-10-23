using PoFootball.Models;
using PoFootball.Sensors;
using PoFootball.Systems;
using UnityEngine;

namespace PoFootball.Agents
{
    /// <summary>
    /// Which discrete actions are legal right now, as pure functions of the models —
    /// the ONE authority both Agent_FootballPlayer.WriteDiscreteActionMask and the
    /// action handlers read, lifted out of the agent so a test can hold the two to
    /// each other without a scene.
    ///
    /// WHY IT MOVED. The mask's whole promise is that every action it leaves enabled
    /// DOES something: a policy that spends exploration on an action the game then
    /// silently refuses is learning noise, and nothing reports it. The predicates
    /// already shared one method each inside the agent (CanReleasePass, CanLateral),
    /// but "shared" stopped at the predicate. The quarterback's release was enabled
    /// whenever a pitch was ON — CanLateral, which looks for the NEAREST trailing
    /// team-mate — and then pitched along the AIM, which ResolveLateralTarget
    /// refuses for anybody behind the ray. A quarterback aiming downfield with no
    /// pass on got an enabled release that did nothing (Agent_ActionLegalityTests).
    /// <see cref="TryPitch"/> now falls back to the nearest man, so the enabled
    /// release always pitches. That changes what an action does, which is contract:
    /// revision 17.
    /// </summary>
    internal static class Agent_ActionLegality
    {
        /// <summary>
        /// Fills <paramref name="enabled"/> (PLAY_CALL_BRANCH_SIZE wide) with the
        /// play-call mask: before the snap on fourth down, the kicks and "go for it";
        /// at the dropback, every scrimmage call and nothing else; at every other
        /// decision, "no call" alone, so those samples carry no gradient.
        /// </summary>
        internal static void WriteCallMask(Systems_PlayModel play, bool[] enabled)
        {
            for (int callIndex = 0; callIndex < enabled.Length; callIndex++)
            {
                enabled[callIndex] = true;
            }

            if (CanCallKickNow(play))
            {
                // BEFORE THE SNAP, ON FOURTH DOWN: kick, or not. None means "line up
                // and go for it"; every scrimmage call waits for the dropback.
                bool isInRange = IsInFieldGoalRange(play);

                for (int callIndex = 1; callIndex < enabled.Length; callIndex++)
                {
                    Systems_PlayCall call = (Systems_PlayCall)callIndex;

                    enabled[callIndex] = call == Systems_PlayCall.Punt
                        || (call == Systems_PlayCall.FieldGoal && isInRange);
                }

                return;
            }

            if (!IsInCallLatchWindow(play))
            {
                for (int callIndex = 1; callIndex < enabled.Length; callIndex++)
                {
                    enabled[callIndex] = false;
                }

                return;
            }

            // AT THE DROPBACK: a scrimmage call, which must be made now. Kicks were a
            // pre-snap decision and are no longer on offer.
            enabled[(int)Systems_PlayCall.None] = false;
            enabled[(int)Systems_PlayCall.Punt] = false;
            enabled[(int)Systems_PlayCall.FieldGoal] = false;

            // The run-fit drill: LatchCall refuses a pass, so the mask must not offer one.
            if (play.RunsOnly)
            {
                enabled[(int)Systems_PlayCall.Pass] = false;
                enabled[(int)Systems_PlayCall.PassDeep] = false;
            }
        }

        /// <summary>
        /// Whether a decision taken now can latch a pre-snap kick: fourth down, set,
        /// and inside the call window with a decision period to spare.
        /// </summary>
        internal static bool CanCallKickNow(Systems_PlayModel play)
        {
            return play.CanCallKick
                && play.PreSnapTick + Systems_SimConstants.DECISION_PERIOD
                    < Systems_SimConstants.KICK_CALL_WINDOW_TICKS;
        }

        /// <summary>
        /// Whether a decision taken now can be the one that latches the call: no call
        /// yet, and its actions will still be applied on or after DROPBACK_TICKS.
        ///
        /// Two decision periods of margin rather than one, because the referee's
        /// tick counter and the Academy's step are not guaranteed to advance in a
        /// fixed order within a FixedUpdate. Erring early costs one extra unpinned
        /// sample; erring late would pin the latching decision to None and push the
        /// call a whole decision past the dropback.
        /// </summary>
        internal static bool IsInCallLatchWindow(Systems_PlayModel play)
        {
            return play.Phase == Systems_PlayPhase.Live
                && !play.CallIsLatched
                && play.Kind.HasLiveCall()
                && play.PhysicsTick + (2 * Systems_SimConstants.DECISION_PERIOD)
                    > Systems_SimConstants.DROPBACK_TICKS;
        }

        /// <summary>
        /// Distance to the goal line plus the seventeen yards every real field goal
        /// carries — ten of end zone and seven back to the hold.
        /// </summary>
        internal static bool IsInFieldGoalRange(Systems_PlayModel play)
        {
            float yardsToGoalLine = Systems_FieldModel.YardsToAttackingGoal(play.LineOfScrimmageY);

            return yardsToGoalLine + Systems_GameRules.FIELD_GOAL_SNAP_YARDS
                <= Systems_GameRules.FIELD_GOAL_MAX_YARDS;
        }

        /// <summary>
        /// Every condition under which a pass may leave the passer's hand.
        /// </summary>
        internal static bool CanReleasePass(
            Systems_PlayModel play, Systems_BallModel ball, Systems_IPlayerHandle passer)
        {
            return play.Call.IsPass()
                && play.Phase == Systems_PlayPhase.Live
                && play.Kind != Systems_PlayKind.Spike
                && !play.ForwardPassThrown
                && passer.IsCarrier
                && ball != null
                && ball.IsHeld
                && ball.CarrierId == passer.Id
                && play.PhysicsTick <= Systems_SimConstants.THROW_WINDOW_TICKS
                // A FORWARD PASS MUST BE THROWN FROM BEHIND THE LINE OF SCRIMMAGE,
                // and there is only one. Past the line the quarterback has tucked it
                // and is a runner; the mask makes an illegal forward pass impossible
                // rather than a foul waiting to be flagged.
                && passer.Position.y <= play.LineOfScrimmageY;
        }

        /// <summary>
        /// Whether <paramref name="carrier"/> may pitch the ball right now: carrying
        /// it, live, with a team-mate level or behind inside range.
        /// </summary>
        internal static bool CanLateral(
            Systems_PlayModel play, Systems_BallModel ball, Systems_BallSystem ballSystem,
            Systems_IPlayerHandle carrier)
        {
            return carrier.IsCarrier
                && play != null
                && play.Phase == Systems_PlayPhase.Live
                && ball != null
                && ball.IsHeld
                && ball.CarrierId == carrier.Id
                && ballSystem != null
                && !ballSystem.IsKickoffNotYetKicked()
                && ballSystem.ResolveLateralTarget(carrier, Vector2.zero) != null;
        }

        /// <summary>
        /// A pitch along <paramref name="aim"/>, or to the nearest trailing man if
        /// nobody is along it. Whenever <see cref="CanLateral"/> holds this pitches,
        /// which is what lets the mask enable a release on CanLateral alone.
        /// </summary>
        internal static bool TryPitch(
            Systems_BallSystem ballSystem, Systems_IPlayerHandle carrier, Vector2 aim)
        {
            return ballSystem.TryLateral(carrier, aim)
                || (aim.sqrMagnitude >= 1e-4f && ballSystem.TryLateral(carrier, Vector2.zero));
        }

        /// <summary>The call-branch width, for a caller sizing its reusable buffer.</summary>
        internal const int CALL_BRANCH_SIZE = Sensor_FootballState.PLAY_CALL_BRANCH_SIZE;
    }
}

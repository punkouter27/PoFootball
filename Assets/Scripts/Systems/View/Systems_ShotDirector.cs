using PoFootball.Models;
using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// Chooses the shot, the way a television director calls cameras: tight on the
    /// quarterback while the rush closes, wide and leading when a runner is gone,
    /// pulled back while a pass is in the air, on the returner under a kick, and
    /// the whole end zone at the goal line. Systems_BroadcastCameraView frames
    /// whatever this says; this class never touches the camera.
    ///
    /// PLAIN C#, NO SCENE. Every input arrives as a Systems_ShotSituation the camera
    /// has already worked out, and the breakaway projection is a static over plain
    /// arrays, so the rules about WHEN to cut are pinned by EditMode tests
    /// (Systems_ShotDirectorTests) rather than judged by eye on a live game.
    ///
    /// THE CAMERA MUST NEVER FLAP, AND THERE ARE TWO GUARDS AGAINST IT.
    ///
    ///   HYSTERESIS on the two shots that hang on a distance or a projection. The
    ///   pocket shot comes in when a rusher is inside POCKET_ENTER_METRES and only
    ///   goes when he is back past POCKET_EXIT_METRES, so a defensive end hand-
    ///   fighting a tackle at four and a half metres does not toggle it every
    ///   frame. A breakaway is held for BREAKAWAY_RELEASE_SECONDS after the
    ///   projection stops saying "gone", because a pursuer stepping inside the
    ///   reach for a stride and out again is the commonest way that answer wobbles.
    ///
    ///   A MINIMUM HOLD between the shots the director chooses on judgement —
    ///   default, pocket, breakaway, goal line. The two that follow the BALL are
    ///   exempt both ways: a pass leaving the hand and a kick leaving the foot are
    ///   the cue, and a director who waited half a second to cut to the throw would
    ///   miss the throw. Likewise when the ball comes down: the catch is a new play
    ///   to frame, not a judgement to sit on. The camera's own damping is what
    ///   makes every one of these cuts a move rather than a jump.
    /// </summary>
    public sealed class Systems_ShotDirector
    {
        /// <summary>
        /// A rusher this close to a quarterback still holding the ball brings in
        /// the pocket shot. About five yards: the last stride before contact, which
        /// is the moment the throw-or-sack question becomes the whole play.
        /// </summary>
        public const float POCKET_ENTER_METRES = 4.5f;

        /// <summary>The pocket shot goes when the nearest rusher is back past this.</summary>
        public const float POCKET_EXIT_METRES = 7f;

        /// <summary>
        /// Seconds a breakaway shot outlives the projection that called it, so long
        /// as somebody still has the ball.
        /// </summary>
        public const float BREAKAWAY_RELEASE_SECONDS = 0.6f;

        /// <summary>
        /// Least time a judgement shot is held before the director may change its
        /// mind. Half a second is long enough that a cut reads as a decision and
        /// short enough that the pocket shot still arrives before the sack.
        /// </summary>
        public const float MIN_HOLD_SECONDS = 0.5f;

        private float _heldSeconds = MIN_HOLD_SECONDS;
        private float _breakawayLapseSeconds;

        /// <summary>The shot being framed. Default until the first Advance says otherwise.</summary>
        public Systems_ShotKind Current { get; private set; } = Systems_ShotKind.Default;

        /// <summary>
        /// Back to the default shot with the hold already served, so the first
        /// frame after a snap may cut straight to the goal-line shot. Called by the
        /// camera on every snap: no shot survives from one play to the next.
        /// </summary>
        public void Reset()
        {
            Current = Systems_ShotKind.Default;
            _heldSeconds = MIN_HOLD_SECONDS;
            _breakawayLapseSeconds = 0f;
        }

        /// <summary>Moves the director on by one frame and returns the shot to frame.</summary>
        public Systems_ShotKind Advance(in Systems_ShotSituation situation, float deltaTime)
        {
            _heldSeconds += deltaTime;

            if (Current == Systems_ShotKind.Breakaway && !situation.CarrierGone)
            {
                _breakawayLapseSeconds += deltaTime;
            }
            else
            {
                _breakawayLapseSeconds = 0f;
            }

            Systems_ShotKind wanted = Wanted(situation);

            if (wanted == Current)
            {
                return Current;
            }

            bool immediate = !situation.Enabled
                || FollowsTheBall(wanted)
                || FollowsTheBall(Current);

            if (!immediate && _heldSeconds < MIN_HOLD_SECONDS)
            {
                return Current;
            }

            Current = wanted;
            _heldSeconds = 0f;
            _breakawayLapseSeconds = 0f;
            return Current;
        }

        private Systems_ShotKind Wanted(in Systems_ShotSituation situation)
        {
            if (!situation.Enabled)
            {
                return Systems_ShotKind.Default;
            }

            if (situation.PassInFlight)
            {
                return Systems_ShotKind.BallInAir;
            }

            if (situation.KickInFlight)
            {
                return Systems_ShotKind.Kick;
            }

            if (situation.HasCarrier)
            {
                if (situation.CarrierGone)
                {
                    return Systems_ShotKind.Breakaway;
                }

                if (Current == Systems_ShotKind.Breakaway
                    && _breakawayLapseSeconds < BREAKAWAY_RELEASE_SECONDS)
                {
                    return Systems_ShotKind.Breakaway;
                }

                if (situation.PasserHoldingBall)
                {
                    float threshold = Current == Systems_ShotKind.Pocket
                        ? POCKET_EXIT_METRES
                        : POCKET_ENTER_METRES;

                    if (situation.NearestRusherMetres < threshold)
                    {
                        return Systems_ShotKind.Pocket;
                    }
                }
            }

            return situation.GoalLine ? Systems_ShotKind.GoalLine : Systems_ShotKind.Default;
        }

        private static bool FollowsTheBall(Systems_ShotKind shot)
        {
            return shot == Systems_ShotKind.BallInAir || shot == Systems_ShotKind.Kick;
        }

        /// <summary>
        /// Whether nobody can catch the carrier before the goal line he is running
        /// at. The same projection Systems_Referee.IsGoneForTheGoal makes at the
        /// trainer's ceiling, with the same three constants — the carrier at the
        /// pace he is actually running, every opponent at his role's top speed,
        /// sampled at BREAKAWAY_PATH_SAMPLES points along the straight path — so
        /// the camera goes wide on exactly the runs the officials would call gone.
        ///
        /// A COPY, NOT A CALL. The referee's is private to a system that owns the
        /// rules, and widening it for a camera would put a presentation consumer on
        /// a method whose job is to decide touchdowns. This one reads plain arrays
        /// the camera fills, which is also what lets a test exercise it.
        /// </summary>
        /// <param name="direction">+1 for a carrier attacking +Y, -1 for one returning toward -Y.</param>
        /// <param name="goalLineY">The goal line he is running at.</param>
        public static bool IsBreakaway(
            Vector2 carrierPosition,
            Vector2 carrierVelocity,
            float direction,
            float goalLineY,
            Vector2[] opponentPositions,
            float[] opponentTopSpeeds,
            int opponentCount)
        {
            float forwardSpeed = carrierVelocity.y * direction;

            if (forwardSpeed < Systems_SimConstants.BREAKAWAY_MIN_SPEED)
            {
                return false;
            }

            float distance = (goalLineY - carrierPosition.y) * direction;

            if (distance <= 0f)
            {
                return false;
            }

            for (int opponentIndex = 0; opponentIndex < opponentCount; opponentIndex++)
            {
                float speed = Mathf.Max(opponentTopSpeeds[opponentIndex], 0.01f);

                for (int sample = 0; sample <= Systems_SimConstants.BREAKAWAY_PATH_SAMPLES; sample++)
                {
                    float along = distance * sample / Systems_SimConstants.BREAKAWAY_PATH_SAMPLES;
                    Vector2 point = carrierPosition + new Vector2(0f, along * direction);
                    float carrierTime = along / forwardSpeed;
                    float opponentTime = Mathf.Max(
                        0f,
                        Vector2.Distance(opponentPositions[opponentIndex], point)
                            - Systems_SimConstants.BREAKAWAY_REACH_METRES) / speed;

                    if (opponentTime <= carrierTime)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}

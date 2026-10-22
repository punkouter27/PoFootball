using PoFootball.Models;
using UnityEngine;

namespace PoFootball.Rewards
{
    /// <summary>
    /// Per-agent credit assignment: the small dense terms that tell one player on a
    /// side apart from the other ten.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// Reward_Terminal pays every player on a side the same number, and
    /// Reward_Progress pays every player on a side the same number. Up to base03
    /// that was the entire reward, which means the optimizer had no signal
    /// whatsoever distinguishing a guard who held his block from a guard who
    /// jogged sideways, or a receiver who beat his corner from one who stood
    /// still — a touchdown smeared its credit evenly across all eleven. The only
    /// player with an individually identifiable contribution was whoever happened
    /// to be carrying the ball.
    ///
    /// These terms are deliberately an order of magnitude below the terminal
    /// rewards. They exist to shape which behaviours get EXPLORED, not to decide
    /// who wins the play; if a shaped term ever becomes worth more than a
    /// touchdown, the policy will farm it and ignore the game.
    ///
    /// Pure scalar functions — no state, no allocation, no registry access. The
    /// caller does the geometry, which keeps PoFootball.Rewards a leaf assembly
    /// that depends on nothing but PoFootball.Models.
    /// </summary>
    public static class Reward_Role
    {
        /// <summary>
        /// Whether this role earns the blocking term. Linemen only — a receiver
        /// standing near a defender is running a route, not blocking.
        /// </summary>
        public static bool IsBlocker(Systems_PlayerRole role)
        {
            return role == Systems_PlayerRole.OffensiveLine;
        }

        /// <summary>
        /// Whether this role earns the separation term while it is not the carrier.
        /// </summary>
        public static bool IsReceiver(Systems_PlayerRole role)
        {
            return role.IsEligibleReceiver();
        }

        /// <summary>
        /// Reward for one tick of shielding the ball from the nearest rusher.
        ///
        /// Paid only when the blocker is BETWEEN the rusher and the ball, which is
        /// what "block" actually means — standing next to a defender earns nothing.
        /// Alignment is the cosine between (blocker - rusher) and (ball - rusher):
        /// at 1 the blocker sits squarely on the rusher's path to the ball, at 0 it
        /// is off to the side, and below 0 it has been beaten.
        /// </summary>
        public static float Block(
            Vector2 blockerPosition, Vector2 rusherPosition, Vector2 ballPosition)
        {
            Vector2 toBlocker = blockerPosition - rusherPosition;
            float distance = toBlocker.magnitude;

            if (distance > Systems_SimConstants.BLOCK_ENGAGE_RANGE || distance < 1e-4f)
            {
                return 0f;
            }

            Vector2 toBall = ballPosition - rusherPosition;
            if (toBall.sqrMagnitude < 1e-4f)
            {
                return 0f;
            }

            float alignment = Vector2.Dot(toBlocker / distance, toBall.normalized);
            if (alignment <= 0f)
            {
                return 0f;
            }

            return alignment * Systems_SimConstants.BLOCK_REWARD_PER_TICK;
        }

        /// <summary>
        /// Reward for one tick of separation from the nearest defender, saturating
        /// at SEPARATION_SATURATION_RANGE so a receiver cannot farm it by running
        /// to an empty corner of the field — past the saturation point extra
        /// distance is worth nothing.
        /// </summary>
        public static float Separation(float distanceToNearestDefender)
        {
            float saturated = Mathf.Clamp01(
                distanceToNearestDefender / Systems_SimConstants.SEPARATION_SATURATION_RANGE);

            return saturated * Systems_SimConstants.SEPARATION_REWARD_PER_TICK;
        }

        /// <summary>
        /// 1 when a receiver is at the depth the call asked for, 0 when he is not:
        /// short of DEEP_SHOT_MIN_YARDS on Pass, at or beyond it on PassDeep.
        /// Multiplies <see cref="Separation"/>, so being open only pays on the
        /// route. Any other call — a run, or nothing yet, which is the first
        /// DROPBACK_TICKS of every play — is 1 and the term is what it always was.
        ///
        /// A gate rather than a new term on purpose: it cannot raise what a
        /// receiver earns over a play, so the bound Reward_Tests pins on the
        /// shaped terms holds without being touched.
        /// </summary>
        public static float OnRoute(Systems_PlayCall call, float yardsPastTheLine)
        {
            bool isDeep = yardsPastTheLine >= Systems_SimConstants.DEEP_SHOT_MIN_YARDS;

            switch (call)
            {
                case Systems_PlayCall.Pass:
                    return isDeep ? 0f : 1f;
                case Systems_PlayCall.PassDeep:
                    return isDeep ? 1f : 0f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Signed reward for closing on the ball over one tick — since
        /// football_base24 the OFFENSE's term only, chasing a defender who has the
        /// ball; the defense is paid <see cref="InterceptShaping"/>. metresClosed is the
        /// previous distance minus the current one, so drifting away is a small
        /// negative — which is the half that matters. A purely positive pursuit
        /// term is satisfied by a defender who sits still and lets the play come to
        /// it; making retreat cost something is what forces the defense to commit.
        /// </summary>
        public static float Pursuit(float metresClosed)
        {
            return metresClosed * Systems_SimConstants.PURSUIT_REWARD_PER_METRE;
        }

        // --- The defense's pursuit: an angle, not a chase (football_base24) -----

        /// <summary>
        /// What being one second nearer an intercept is worth to a defender, in
        /// the potential <see cref="InterceptPotential"/>.
        ///
        /// SIZED AGAINST THE TACKLE, NOT AGAINST THE OLD TERM. The potential runs
        /// from 0 (out of reach) to INTERCEPT_POTENTIAL_PER_SECOND times
        /// <see cref="INTERCEPT_HORIZON_SECONDS"/> = 0.096 (on the ball), and the
        /// shaping telescopes, so the most one defender can collect over a whole
        /// play is that 0.096: a fifth of TACKLE_REWARD (0.5), under the quarter of
        /// a touchdown Reward_Tests allows any shaped term. The distance term it
        /// replaces for the defense paid 0.004 a metre, ~0.12 for a 30 m closing
        /// run, so the defense's total shaping budget is about where it was — what
        /// moved is WHICH movement earns it.
        /// </summary>
        public const float INTERCEPT_POTENTIAL_PER_SECOND = 0.012f;

        /// <summary>
        /// Intercepts further off than this all count as "out of reach". Eight
        /// seconds is ~70 m at a corner's top speed, most of the field: anything
        /// longer is a solution that the carrier's next cut will invalidate anyway,
        /// and a cap is what makes the potential (and so the per-play total) bounded.
        /// </summary>
        public const float INTERCEPT_HORIZON_SECONDS = 8f;

        /// <summary>
        /// The extrinsic gamma of every behavior in Config/FootballBase24.yaml (and
        /// every config since base11). Potential-based shaping is only
        /// policy-invariant with the trainer's own discount, so if a config ever
        /// changes gamma this must change with it.
        /// </summary>
        public const float TRAINER_GAMMA = 0.99f;

        /// <summary>
        /// The trainer's per-DECISION gamma spread over the DECISION_PERIOD physics
        /// ticks a decision spans: rewards are added every tick and summed into one
        /// step, so paying gamma_tick * Phi' - Phi per tick telescopes to the
        /// per-step form up to O(1 - gamma_tick) = 0.002 of one tick's potential.
        /// Computed once, never per tick.
        /// </summary>
        public static readonly float TickDiscount =
            Mathf.Pow(TRAINER_GAMMA, 1f / Systems_SimConstants.DECISION_PERIOD);

        /// <summary>
        /// Seconds until a pursuer running flat out at <paramref name="pursuerSpeed"/>
        /// can stand where a target moving at constant <paramref name="targetVelocity"/>
        /// will be — the earliest t with |target + v t - pursuer| = s t — or
        /// +infinity when no such t exists (a faster target running away).
        ///
        /// WHY THIS AND NOT DISTANCE. Distance is minimised by running AT the
        /// carrier, which means arriving where he was; Agent_FootballPlayer's own
        /// InterceptOf says why that catches nobody at equal speed. Time to
        /// intercept is minimised by cutting the angle — the defender who takes
        /// the right one sees it fall by a full second every second, the one
        /// chasing the back of a faster man sees it never come down. Paying its
        /// fall pays for the angle.
        ///
        /// Closed form: with d = target - pursuer, (v.v - s^2) t^2 + 2 (d.v) t +
        /// d.d = 0, smallest non-negative root. Pure, allocation-free, Models only.
        /// </summary>
        public static float InterceptSeconds(
            Vector2 pursuerPosition, float pursuerSpeed,
            Vector2 targetPosition, Vector2 targetVelocity)
        {
            Vector2 offset = targetPosition - pursuerPosition;
            float c = Vector2.Dot(offset, offset);

            if (c < 1e-6f)
            {
                return 0f;
            }

            if (pursuerSpeed <= 1e-3f)
            {
                return float.PositiveInfinity;
            }

            float a = Vector2.Dot(targetVelocity, targetVelocity) - (pursuerSpeed * pursuerSpeed);
            float b = 2f * Vector2.Dot(offset, targetVelocity);

            // Equal speeds: the quadratic is linear, and only a target coming
            // toward the pursuer (b < 0) can be met.
            if (Mathf.Abs(a) < 1e-5f)
            {
                return b < 0f ? -c / b : float.PositiveInfinity;
            }

            float discriminant = (b * b) - (4f * a * c);

            if (discriminant < 0f)
            {
                return float.PositiveInfinity;
            }

            float root = Mathf.Sqrt(discriminant);
            float first = (-b - root) / (2f * a);
            float second = (-b + root) / (2f * a);

            float earliest = float.PositiveInfinity;

            if (first >= 0f)
            {
                earliest = first;
            }

            if (second >= 0f && second < earliest)
            {
                earliest = second;
            }

            return earliest;
        }

        /// <summary>
        /// Phi for a defender: INTERCEPT_POTENTIAL_PER_SECOND times how far INSIDE
        /// the horizon its intercept is. Zero out of reach, 0.096 on the ball.
        ///
        /// NON-NEGATIVE ON PURPOSE. Shaping is gamma Phi' - Phi, so a defender who
        /// holds still at constant Phi is paid (gamma - 1) Phi. With Phi = -k t (the
        /// obvious sign) that is POSITIVE and largest far from the ball — a drip
        /// for standing off, the exact habit base22's defense had. Shifted to
        /// k (horizon - t), the same differences (so the same invariance) but the
        /// drip is zero out of reach and slightly negative close in: shadowing a
        /// carrier without ever closing — what base20's defenders learned when the
        /// stall paid — costs a little every tick instead of nothing.
        /// </summary>
        public static float InterceptPotential(float interceptSeconds)
        {
            if (float.IsNaN(interceptSeconds) || interceptSeconds >= INTERCEPT_HORIZON_SECONDS)
            {
                return 0f;
            }

            return INTERCEPT_POTENTIAL_PER_SECOND
                * (INTERCEPT_HORIZON_SECONDS - Mathf.Max(0f, interceptSeconds));
        }

        /// <summary>
        /// One tick of the defense's pursuit: gamma_tick Phi(now) - Phi(before).
        /// Potential-based (Ng, Harada and Russell 1999), so it cannot change which
        /// policy is optimal, only how fast the defense finds it; and because Phi is
        /// bounded in [0, 0.096] the whole play's sum is bounded by the same.
        /// </summary>
        public static float InterceptShaping(float previousPotential, float currentPotential)
        {
            return (TickDiscount * currentPotential) - previousPotential;
        }
    }
}

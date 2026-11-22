using UnityEngine;

namespace PoFootball.Models
{
    /// <summary>
    /// When a pursuer running flat out can stand where a moving target will be.
    ///
    /// MOVED HERE FROM Reward_Role (2026-11-04) so the defense's pursuit reward and
    /// the broadcast's pursuit cones (Systems_PursuitView) read ONE formula. Rewards
    /// and Views cannot see each other's assemblies, Models is under both, and a
    /// second copy of a closed form is a second place for a sign to drift.
    /// Reward_Role.InterceptSeconds still exists and forwards here, so the reward
    /// path is unchanged call for call.
    /// </summary>
    public static class Systems_InterceptMath
    {
        /// <summary>
        /// Seconds until a pursuer running flat out at <paramref name="pursuerSpeed"/>
        /// can stand where a target moving at constant <paramref name="targetVelocity"/>
        /// will be — the earliest t with |target + v t - pursuer| = s t — or
        /// +infinity when no such t exists (a faster target running away).
        ///
        /// Closed form: with d = target - pursuer, (v.v - s^2) t^2 + 2 (d.v) t +
        /// d.d = 0, smallest non-negative root. Pure and allocation-free.
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
    }
}

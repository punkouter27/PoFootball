using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PoFootball.Views
{
    /// <summary>
    /// Per-slot tracking math for <see cref="Systems_SeparationView"/>: each
    /// player's nearest opponent distance (for receiver separation rings) and
    /// earliest intercept time against the live carrier (for pursuit lines).
    ///
    /// Mirrors <see cref="Models.Systems_InterceptMath"/> in Burst/Mathematics
    /// so the overlay runs off managed loops with zero per-frame allocations.
    /// </summary>
    [BurstCompile]
    internal struct Systems_SeparationJob : IJobParallelFor
    {
        public const int ABSENT = -1;

        /// <summary>Position in xy, velocity in zw, world metres.</summary>
        [ReadOnly] public NativeArray<float4> Players;

        /// <summary>0 offense, 1 defense, <see cref="ABSENT"/> for an empty slot.</summary>
        [ReadOnly] public NativeArray<int> Sides;

        /// <summary>Metres per second, trait-scaled.</summary>
        [ReadOnly] public NativeArray<float> TopSpeeds;

        public float2 CarrierPosition;
        public float2 CarrierVelocity;
        public int CarrierSide;
        public int HasCarrier;
        public int ComputeSeparation;
        public float DefaultSeparation;

        [WriteOnly] public NativeArray<float> NearestOpponentDistances;
        [WriteOnly] public NativeArray<float> InterceptTimes;

        public void Execute(int slotIndex)
        {
            int side = Sides[slotIndex];

            if (side == ABSENT)
            {
                NearestOpponentDistances[slotIndex] = DefaultSeparation;
                InterceptTimes[slotIndex] = float.PositiveInfinity;
                return;
            }

            float4 self = Players[slotIndex];

            if (ComputeSeparation != 0)
            {
                float nearestSqr = float.MaxValue;

                for (int otherIndex = 0; otherIndex < Sides.Length; otherIndex++)
                {
                    int otherSide = Sides[otherIndex];

                    if (otherSide == ABSENT || otherSide == side)
                    {
                        continue;
                    }

                    float2 delta = Players[otherIndex].xy - self.xy;
                    float sqrDist = math.lengthsq(delta);

                    if (sqrDist < nearestSqr)
                    {
                        nearestSqr = sqrDist;
                    }
                }

                NearestOpponentDistances[slotIndex] = nearestSqr == float.MaxValue
                    ? DefaultSeparation
                    : math.sqrt(nearestSqr);
            }
            else
            {
                NearestOpponentDistances[slotIndex] = DefaultSeparation;
            }

            if (HasCarrier != 0 && side != CarrierSide)
            {
                InterceptTimes[slotIndex] = InterceptSeconds(
                    self.xy,
                    TopSpeeds[slotIndex],
                    CarrierPosition,
                    CarrierVelocity);
            }
            else
            {
                InterceptTimes[slotIndex] = float.PositiveInfinity;
            }
        }

        /// <summary>
        /// Closed-form earliest t >= 0 satisfying |target + v*t - pursuer| = s*t,
        /// or +infinity when the pursuer cannot catch the target.
        /// </summary>
        public static float InterceptSeconds(
            float2 pursuerPosition,
            float pursuerSpeed,
            float2 targetPosition,
            float2 targetVelocity)
        {
            float2 offset = targetPosition - pursuerPosition;
            float c = math.dot(offset, offset);

            if (c < 1e-6f)
            {
                return 0f;
            }

            if (pursuerSpeed <= 1e-3f)
            {
                return float.PositiveInfinity;
            }

            float a = math.dot(targetVelocity, targetVelocity) - (pursuerSpeed * pursuerSpeed);
            float b = 2f * math.dot(offset, targetVelocity);

            if (math.abs(a) < 1e-5f)
            {
                return b < 0f ? -c / b : float.PositiveInfinity;
            }

            float discriminant = (b * b) - (4f * a * c);

            if (discriminant < 0f)
            {
                return float.PositiveInfinity;
            }

            float root = math.sqrt(discriminant);
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

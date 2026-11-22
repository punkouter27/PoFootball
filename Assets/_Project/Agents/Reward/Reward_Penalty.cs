namespace PoFootball.Rewards
{
    /// <summary>
    /// The control penalties: what a body pays every live tick for moving badly,
    /// on top of what the play pays it for moving the ball.
    ///
    /// The articulated-body terms of a 3D rig, read for this game's flat
    /// Rigidbody2D players:
    ///
    ///     effort            joint torque    (drive force / max)^2 + (steer torque / max)^2
    ///     angular velocity  body spin       (angular velocity / max turn rate)^2
    ///     action jitter     action rate     |command - previous command|^2
    ///     slide             foot sliding    (speed across the facing / top speed)^2
    ///
    /// Each input is already normalised by the caller, so every term is
    /// dimensionless. Pure functions — no state, no allocation.
    /// </summary>
    public static class Reward_Penalty
    {
        public readonly struct Weights
        {
            public readonly float Effort;
            public readonly float AngularVelocity;
            public readonly float ActionJitter;
            public readonly float Slide;

            public Weights(float effort, float angularVelocity, float actionJitter, float slide)
            {
                Effort = effort;
                AngularVelocity = angularVelocity;
                ActionJitter = actionJitter;
                Slide = slide;
            }

            public static Weights Defaults => new Weights(
                Reward_PenaltyWeights.DEFAULT_EFFORT,
                Reward_PenaltyWeights.DEFAULT_ANGULAR_VELOCITY,
                Reward_PenaltyWeights.DEFAULT_ACTION_JITTER,
                Reward_PenaltyWeights.DEFAULT_SLIDE);
        }

        /// <summary>One tick's four charges, each zero or negative.</summary>
        public readonly struct Terms
        {
            public readonly float Effort;
            public readonly float AngularVelocity;
            public readonly float ActionJitter;
            public readonly float Slide;

            public Terms(float effort, float angularVelocity, float actionJitter, float slide)
            {
                Effort = effort;
                AngularVelocity = angularVelocity;
                ActionJitter = actionJitter;
                Slide = slide;
            }

            public float Total => Effort + AngularVelocity + ActionJitter + Slide;
        }

        /// <summary>
        /// One tick's charges.
        /// </summary>
        /// <param name="driveLoad">Applied drive force over this role's maximum.</param>
        /// <param name="steerLoad">Applied steer torque over this role's maximum.</param>
        /// <param name="spin">Angular velocity over the maximum turn rate.</param>
        /// <param name="commandStep">Magnitude of the change in the velocity command since the last decision.</param>
        /// <param name="slip">Speed perpendicular to the body's facing over top speed.</param>
        public static Terms PerTick(
            in Weights weights,
            float driveLoad,
            float steerLoad,
            float spin,
            float commandStep,
            float slip)
        {
            return new Terms(
                -weights.Effort * ((driveLoad * driveLoad) + (steerLoad * steerLoad)),
                -weights.AngularVelocity * spin * spin,
                -weights.ActionJitter * commandStep * commandStep,
                -weights.Slide * slip * slip);
        }
    }
}

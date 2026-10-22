namespace PoFootball.Views
{
    /// <summary>
    /// What the director needs to know about the field this frame, already reduced
    /// to the handful of facts a shot is chosen on.
    ///
    /// A STRUCT OF FACTS RATHER THAN THE MODELS, ON PURPOSE. Systems_ShotDirector
    /// would be simpler to call if it took the ball model, the play model and the
    /// registry and worked these out itself — and it would then need a scene, a
    /// container and twenty-two bodies to test. The camera reads the models (it
    /// already does, for its own framing) and hands over the answers, so every
    /// rule about WHEN to cut can be pinned by a test that builds one of these by
    /// hand.
    /// </summary>
    public readonly struct Systems_ShotSituation
    {
        /// <summary>Systems_BroadcastSettings.AutoDirector. False forces the default shot.</summary>
        public readonly bool Enabled;

        /// <summary>A forward pass is in the air.</summary>
        public readonly bool PassInFlight;

        /// <summary>A kick is in the air.</summary>
        public readonly bool KickInFlight;

        /// <summary>Somebody holds the ball and the play is live.</summary>
        public readonly bool HasCarrier;

        /// <summary>
        /// The carrier is running clear: <see cref="Systems_ShotDirector.IsBreakaway"/>
        /// said nobody reaches his path to the goal before he does.
        /// </summary>
        public readonly bool CarrierGone;

        /// <summary>
        /// The carrier is the offense's quarterback, behind the line, with no
        /// forward pass thrown yet — the only carrier a pocket shot is about.
        /// </summary>
        public readonly bool PasserHoldingBall;

        /// <summary>
        /// Metres from the carrier to the nearest opponent, or float.MaxValue when
        /// there is no carrier or nobody is within range.
        /// </summary>
        public readonly float NearestRusherMetres;

        /// <summary>The offense is snapping, or has snapped, from inside the five.</summary>
        public readonly bool GoalLine;

        public Systems_ShotSituation(
            bool enabled,
            bool passInFlight,
            bool kickInFlight,
            bool hasCarrier,
            bool carrierGone,
            bool passerHoldingBall,
            float nearestRusherMetres,
            bool goalLine)
        {
            Enabled = enabled;
            PassInFlight = passInFlight;
            KickInFlight = kickInFlight;
            HasCarrier = hasCarrier;
            CarrierGone = carrierGone;
            PasserHoldingBall = passerHoldingBall;
            NearestRusherMetres = nearestRusherMetres;
            GoalLine = goalLine;
        }
    }
}

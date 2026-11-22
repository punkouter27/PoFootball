namespace PoFootball.Systems
{
    /// <summary>
    /// Whether the referee may start the pre-snap count. Asked every tick before
    /// the first one, so a held snap leaves PreSnapTick at zero: the kick-call
    /// window (PreSnapTick under KICK_CALL_WINDOW_TICKS), the flinch draw and the
    /// defense's front all still happen, in order, in the PRESNAP_TICKS after the
    /// gate opens — and the game clock, which runs only while the play is Live,
    /// does not run while it is shut.
    ///
    /// Open in every mode but one: the QB challenge holds the snap until the
    /// human has picked a play and clicked to hike (Systems_QbChallengeSystem).
    /// </summary>
    public interface Systems_ISnapGate
    {
        bool IsOpen { get; }

        /// <summary>
        /// The snap about to happen is a person's (the QB challenge): no drawn
        /// flinch and no delay of game on it — penalties for things he did not do.
        /// </summary>
        bool IsHumanSnap { get; }
    }

    /// <summary>The gate every scripted, trained and training game uses: always open.</summary>
    public sealed class Systems_OpenSnapGate : Systems_ISnapGate
    {
        public bool IsOpen => true;

        public bool IsHumanSnap => false;
    }
}

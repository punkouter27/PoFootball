namespace PoFootball.Models
{
    /// <summary>
    /// Position group. Encoded as a one-hot slice of every agent's observation
    /// vector and as the sprite shape used by the presentation layer.
    ///
    /// RunningBack is the halfback — the deep back in the I-formation. Fullback was
    /// appended in milestone 2 rather than inserted, so the existing values keep
    /// their numbering and any serialized role data stays valid.
    /// </summary>
    public enum Systems_PlayerRole
    {
        Quarterback = 0,
        RunningBack = 1,
        WideReceiver = 2,
        TightEnd = 3,
        OffensiveLine = 4,
        DefensiveLine = 5,
        Linebacker = 6,
        Cornerback = 7,
        Safety = 8,
        Fullback = 9
    }

    public static class Systems_PlayerRoleExtensions
    {
        /// <summary>
        /// Whether this role is a pass catcher by design: the receivers and both
        /// backs. Read by the separation reward, the scripted quarterback's reads and
        /// the officials' pass-interference and holding calls.
        ///
        /// NOT who can physically catch the ball. Systems_BallSystem.IsPassTargetRole
        /// is deliberately broader — anyone but the offensive line — so a trick play
        /// that hands off first can still throw to the quarterback.
        /// </summary>
        public static bool IsEligibleReceiver(this Systems_PlayerRole role)
        {
            switch (role)
            {
                case Systems_PlayerRole.WideReceiver:
                case Systems_PlayerRole.TightEnd:
                case Systems_PlayerRole.RunningBack:
                case Systems_PlayerRole.Fullback:
                    return true;
                default:
                    return false;
            }
        }
    }
}

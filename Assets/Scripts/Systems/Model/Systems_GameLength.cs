namespace PoFootball.Models
{
    /// <summary>
    /// How long a played game is: its periods, how long each runs, and whether a
    /// tie goes to overtime. Carried by <see cref="Systems_GameModel"/>, which is
    /// what every clock rule reads instead of the Systems_GameRules constants.
    ///
    /// Two lengths exist. <see cref="Standard"/> is the game this project has always
    /// played — four 120 s quarters and the NFL's overtime. <see cref="QbChallenge"/>
    /// is the one-player mode: a single three-minute period, football's clock rules
    /// for both sides inside it, and the final whistle at 0:00 however it stands,
    /// because the score being chased is the player's own, not the margin.
    ///
    /// The clock RULES that scale with a quarter (the two-minute warning, the
    /// out-of-bounds windows; Systems_GameRules.CLOCK_SCALE) are unchanged: they
    /// are about how fast the clock runs, which is the same in both.
    /// </summary>
    public readonly struct Systems_GameLength
    {
        public readonly int Periods;

        public readonly float PeriodSeconds;

        public readonly bool HasOvertime;

        public Systems_GameLength(int periods, float periodSeconds, bool hasOvertime)
        {
            Periods = periods;
            PeriodSeconds = periodSeconds;
            HasOvertime = hasOvertime;
        }

        public static Systems_GameLength Standard => new Systems_GameLength(
            Systems_GameRules.QUARTER_COUNT, Systems_GameRules.QUARTER_SECONDS, true);

        /// <summary>One three-minute period, no overtime.</summary>
        public static Systems_GameLength QbChallenge => new Systems_GameLength(
            1, QB_CHALLENGE_SECONDS, false);

        public const float QB_CHALLENGE_SECONDS = 180f;

        /// <summary>The period after which play stops for halftime, or 0 when there is none.</summary>
        public int HalftimeAfter => Periods >= 2 ? Periods / 2 : 0;
    }
}

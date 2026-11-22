namespace PoFootball.Models
{
    /// <summary>Which game a played SCN_GAME is.</summary>
    public enum Systems_GameVariant
    {
        /// <summary>Twenty-two computer players, four quarters: the game this project has always played.</summary>
        Standard = 0,

        /// <summary>
        /// One human quarterback against the clock: three minutes, the human's team
        /// on offense, the computer on every other job (Systems_QbChallengeSystem).
        /// </summary>
        QbChallenge = 1,
    }
}

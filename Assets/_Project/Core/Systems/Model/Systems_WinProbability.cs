using UnityEngine;

namespace PoFootball.Models
{
    /// <summary>
    /// The chance the team with the ball goes on to win, from the score, the clock
    /// and what the possession is worth.
    ///
    /// STERN'S NORMAL MODEL, NOT A FITTED ONE. The final margin of an NFL game is
    /// close to normal about the point spread with a standard deviation of about
    /// 13.9 (Stern, 1991), and the part of it still to be played shrinks with the
    /// square root of the time left. So the offense wins with probability
    /// Phi((margin + EP) / (SIGMA * sqrt(fraction left))): the margin it already
    /// has, plus what this possession is expected to add, over the noise the rest
    /// of the game can still put on it. Three inputs, no table, and it behaves at
    /// both ends — a seven-point lead is a coin with a slight bias at kickoff and a
    /// lock with ten seconds left.
    ///
    /// WHY THE NFL'S SIGMA SUITS THIS GAME. The quarters are 120 s, but the pace
    /// is scaled with them (Systems_GameRules: the same ~75 snaps a game), and the
    /// realism gate holds yards a play and touchdowns a drive inside the NFL's
    /// bands, so the number of possessions — which is what the margin's spread
    /// comes from — is the NFL's too. The time axis is the FRACTION of regulation
    /// left, never seconds, for the same reason. Re-fit SIGMA against a batch of
    /// fixed-seed games if the realism bands ever move.
    ///
    /// The possession's EP is faded out over the last FINAL_POSSESSION_SECONDS of
    /// the game: with the clock nearly gone a drive cannot be finished, and adding
    /// a full field goal's worth to a tied game with three seconds left reads as
    /// "84%" for a team about to run out of time.
    /// </summary>
    public static class Systems_WinProbability
    {
        /// <summary>Standard deviation of a full game's final margin, points.</summary>
        public const float SIGMA = 13.9f;

        /// <summary>
        /// Smallest fraction of the game treated as left, so the spread never
        /// collapses to zero while a play can still be run.
        /// </summary>
        public const float MIN_FRACTION_LEFT = 0.004f;

        /// <summary>Game seconds over which the possession's EP fades out at the end.</summary>
        public const float FINAL_POSSESSION_SECONDS = 20f;

        /// <summary>Regulation, game seconds.</summary>
        public const float REGULATION_SECONDS = Systems_GameRules.QUARTER_SECONDS * 4f;

        /// <param name="margin">The offense's score minus the defense's.</param>
        /// <param name="secondsLeft">
        /// Game seconds left: in regulation, everything still to be played across
        /// all quarters; in overtime, the overtime clock.
        /// </param>
        /// <param name="possessionValue">What the offense's possession is expected to score.</param>
        public static float ForOffense(int margin, float secondsLeft, float possessionValue)
        {
            float fade = Mathf.Clamp01(secondsLeft / FINAL_POSSESSION_SECONDS);
            float expected = margin + (possessionValue * fade);

            if (secondsLeft <= 0f)
            {
                return expected > 0f ? 1f : (expected < 0f ? 0f : 0.5f);
            }

            float fraction = Mathf.Max(MIN_FRACTION_LEFT, secondsLeft / REGULATION_SECONDS);
            float spread = SIGMA * Mathf.Sqrt(fraction);
            return NormalCdf(expected / spread);
        }

        /// <summary>
        /// Game seconds still to be played in regulation from this quarter and
        /// clock, or the overtime clock when the game is in overtime.
        /// </summary>
        public static float SecondsLeft(int quarter, float secondsInQuarter, bool overtime)
        {
            if (overtime)
            {
                return Mathf.Max(0f, secondsInQuarter);
            }

            int quartersAfter = Mathf.Clamp(4 - quarter, 0, 3);
            return Mathf.Max(0f, secondsInQuarter) + (quartersAfter * Systems_GameRules.QUARTER_SECONDS);
        }

        /// <summary>
        /// Phi(z), the standard normal CDF, via Abramowitz and Stegun 7.1.26 —
        /// accurate to about 1e-7, which is three orders finer than a percentage on
        /// a ribbon can show.
        /// </summary>
        public static float NormalCdf(float z)
        {
            double x = System.Math.Abs(z) / System.Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + (0.3275911 * x));
            double poly = t * (0.254829592 + (t * (-0.284496736
                + (t * (1.421413741 + (t * (-1.453152027 + (t * 1.061405429))))))));
            double erf = 1.0 - (poly * System.Math.Exp(-x * x));
            double cdf = 0.5 * (1.0 + (z >= 0f ? erf : -erf));
            return (float)cdf;
        }
    }
}

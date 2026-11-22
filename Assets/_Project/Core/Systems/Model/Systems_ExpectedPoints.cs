using UnityEngine;

namespace PoFootball.Models
{
    /// <summary>
    /// A rough expected-points value for the offense of having the ball here, on
    /// this down, with this far to go: about minus one and a half at its own goal
    /// line rising to five at the other one, less for each down used and each yard
    /// still to gain.
    ///
    /// ONE CURVE, TWO READERS. It was a private method of Systems_GameFlowSystem,
    /// where it decides whether a penalty is accepted — a use that only ever
    /// compares the curve with itself, so the shape mattered and the scale did not.
    /// The broadcast's win probability and the per-play EPA read it too, and there
    /// the scale DOES matter: it is added to a score margin. It lives here so the
    /// officials and the graphics cannot drift onto two different fields, and the
    /// coefficients were checked against the published NFL curve (roughly -1.5 at
    /// a team's own goal line, +0 near its own 25, +5 at the opponent's 1) before
    /// anything began adding it to points.
    /// </summary>
    public static class Systems_ExpectedPoints
    {
        /// <summary>The value of a touchdown with its try, from the offense's side.</summary>
        public const float TOUCHDOWN = 6.9f;

        /// <param name="lineY">Line of scrimmage, attacking frame, metres.</param>
        public static float Of(float lineY, int down, float yardsToGo)
        {
            float yardLine = (lineY - Systems_FieldModel.OWN_GOAL_LINE_Y) / Systems_FieldModel.YARD;

            return -1.6f + (0.068f * yardLine)
                - (0.45f * (Mathf.Clamp(down, 1, 4) - 1))
                - (0.04f * (yardsToGo - Systems_GameRules.YARDS_TO_GAIN));
        }
    }
}

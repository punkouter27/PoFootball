using UnityEngine;

namespace PoFootball.Models
{
    /// <summary>
    /// Where a route takes a player, as a pure function of where he lined up, the
    /// line of scrimmage and how far through the route he is. The offense of the
    /// snap always attacks +Y (Systems_GameFlowSystem mirrors the field), so
    /// "downfield" is +Y; "outside" is toward the sideline the player lined up
    /// nearer, "inside" toward the middle.
    ///
    /// Every route is two legs: a first point (the stem, or the flat for the
    /// routes that release sideways), then either a direction to keep running in
    /// or a spot to settle on. legComplete is the caller's
    /// per-play flag: false at the snap, and set here once the first point is
    /// reached — by depth for a stem, by distance for a release — so a player who
    /// has run past his first point does not turn back to it.
    ///
    /// Settling routes return a non-zero settleRadius so the
    /// velocity controller eases onto the spot rather than overshooting it
    /// (Agent_FootballPlayer's ZONE_SETTLE_RADIUS does the same for zone drops).
    /// </summary>
    public static class Systems_RouteBook
    {
        /// <summary>Distance from a release point that counts as having reached it.</summary>
        public const float REACHED_METRES = 1.5f;

        /// <summary>How far ahead along a break a running target is placed.</summary>
        private const float RUN_ON_METRES = 30f;

        /// <summary>Settle radius for curls and hitches.</summary>
        private const float SETTLE_METRES = 2.5f;

        public static Vector2 TargetFor(
            Systems_Route route,
            Vector2 start,
            float lineOfScrimmageY,
            Vector2 position,
            ref bool legComplete,
            out float settleRadius)
        {
            settleRadius = 0f;

            float outside = start.x >= 0f ? 1f : -1f;
            float inside = -outside;
            float yard = Systems_FieldModel.YARD;
            float depth = (position.y - lineOfScrimmageY) / yard;

            switch (route)
            {
                case Systems_Route.Go:
                    return Clamp(new Vector2(start.x, position.y + RUN_ON_METRES));

                case Systems_Route.Post:
                    return Stem(start, position, depth, 10f, new Vector2(inside * 0.7f, 1f), ref legComplete);

                case Systems_Route.Corner:
                    return Stem(start, position, depth, 10f, new Vector2(outside * 0.7f, 1f), ref legComplete);

                case Systems_Route.Slant:
                    return Stem(start, position, depth, 2f, new Vector2(inside, 0.8f), ref legComplete);

                case Systems_Route.Out:
                    return Stem(start, position, depth, 6f, new Vector2(outside, 0f), ref legComplete);

                case Systems_Route.Dig:
                    return Stem(start, position, depth, 10f, new Vector2(inside, 0f), ref legComplete);

                case Systems_Route.Drag:
                    return Stem(start, position, depth, 1.5f, new Vector2(inside, 0.05f), ref legComplete);

                case Systems_Route.Curl:
                    if (!legComplete && depth < 10f)
                    {
                        return Clamp(new Vector2(start.x, lineOfScrimmageY + (11f * yard)));
                    }

                    legComplete = true;
                    settleRadius = SETTLE_METRES;
                    return Clamp(new Vector2(
                        start.x + (inside * 1.5f * yard), lineOfScrimmageY + (8.5f * yard)));

                case Systems_Route.Hitch:
                    if (!legComplete && depth < 5f)
                    {
                        return Clamp(new Vector2(start.x, lineOfScrimmageY + (6f * yard)));
                    }

                    legComplete = true;
                    settleRadius = SETTLE_METRES;
                    return Clamp(new Vector2(start.x, lineOfScrimmageY + (4f * yard)));

                case Systems_Route.Flat:
                    return Release(
                        position, new Vector2(start.x + (outside * 8f * yard), lineOfScrimmageY + (2f * yard)),
                        new Vector2(outside * 0.3f, 1f), ref legComplete);

                case Systems_Route.Wheel:
                    return Release(
                        position, new Vector2(start.x + (outside * 7f * yard), lineOfScrimmageY + yard),
                        new Vector2(0f, 1f), ref legComplete);

                case Systems_Route.Swing:
                    return Release(
                        position, new Vector2(start.x + (outside * 6f * yard), lineOfScrimmageY - yard),
                        new Vector2(outside * 0.2f, 1f), ref legComplete);

                default:
                    // Block and Handoff are not routes: the caller handles both.
                    return position;
            }
        }

        /// <summary>Straight up to <paramref name="stemYards"/>, then along <paramref name="breakDirection"/>.</summary>
        private static Vector2 Stem(
            Vector2 start, Vector2 position, float depth, float stemYards,
            Vector2 breakDirection, ref bool legComplete)
        {
            if (!legComplete && depth < stemYards)
            {
                return Clamp(new Vector2(start.x, position.y + (2f * Systems_FieldModel.YARD)));
            }

            legComplete = true;
            return Clamp(position + (breakDirection.normalized * RUN_ON_METRES));
        }

        /// <summary>To <paramref name="point"/>, then along <paramref name="onward"/>.</summary>
        private static Vector2 Release(
            Vector2 position, Vector2 point, Vector2 onward, ref bool legComplete)
        {
            if (!legComplete && Vector2.Distance(position, point) > REACHED_METRES)
            {
                return Clamp(point);
            }

            legComplete = true;
            return Clamp(position + (onward.normalized * RUN_ON_METRES));
        }

        /// <summary>Kept a metre inside the sidelines, as Systems_EpisodeDirector keeps formations.</summary>
        private static Vector2 Clamp(Vector2 point)
        {
            const float INSIDE = 1f;

            return new Vector2(
                Mathf.Clamp(point.x, -Systems_FieldModel.HALF_WIDTH + INSIDE,
                    Systems_FieldModel.HALF_WIDTH - INSIDE),
                point.y);
        }
    }
}

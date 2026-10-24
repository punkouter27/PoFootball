namespace PoFootball.Systems
{
    /// <summary>
    /// Published once by Systems_ReplayView at the final whistle, when it has
    /// chosen the game's highlights: how many there are to watch.
    ///
    /// The final sheet's HIGHLIGHTS tab is Systems_HudView's, and the reel it opens
    /// is Systems_ReplayView's. Views do not reference each other
    /// (.claude/rules/architecture.md), so the reel says what it has and the HUD
    /// decides whether a tab for it is worth showing. Zero, or never published at
    /// all — the replay view stands down on the presentation budget — leaves the
    /// tab off, which is what a game with nothing to replay should look like.
    /// </summary>
    public readonly struct Systems_HighlightsReadyMessage
    {
        public readonly int Count;

        public Systems_HighlightsReadyMessage(int count)
        {
            Count = count;
        }
    }
}

namespace PoFootball.Systems
{
    /// <summary>
    /// The final sheet's HIGHLIGHTS tab was pressed: Systems_ReplayView opens its
    /// reel on the first highlight.
    ///
    /// It replaced the reel's own WATCH HIGHLIGHTS button, which floated on a
    /// second document between the result sheet and the status footer — a third
    /// thing stacked at the bottom of the screen for what is one more view of the
    /// same result. The tab puts it in the sheet with the box score and the scoring
    /// summary; this message is how a button the HUD owns reaches a reel it does
    /// not. Systems_HighlightPlaybackMessage carries the answer back.
    /// </summary>
    public readonly struct Systems_OpenHighlightsMessage
    {
    }
}

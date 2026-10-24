namespace PoFootball.Views
{
    /// <summary>
    /// The vertical stack every in-game screen shares, in panel units (the panel
    /// is 1080 wide on every device, so these are the same everywhere), each slot
    /// derived from the one above it.
    ///
    ///     top     status bar        Systems_StatusHudView
    ///             score row         Systems_HudView
    ///             win-prob ribbon   Systems_BroadcastGraphicsView
    ///             banner band       result banner and carrier chip (Systems_HudView),
    ///                               call sheet (Systems_BroadcastGraphicsView)
    ///             hit meter         Systems_BroadcastGraphicsView
    ///             ...the field...
    ///             card dock         focus card (Systems_HudView) or the play-by-play
    ///                               ticker, never both — the ticker yields while a
    ///                               player is focused
    ///             transport         pause and playback speed
    ///     bottom  status footer     Systems_StatusHudView
    ///
    /// WHY THIS EXISTS. The same stack used to be spelled out in two views that
    /// could not see each other's numbers. Systems_HudView kept a BANNER_CLEARANCE
    /// of 200 and Systems_BroadcastGraphicsView a BANNER_TOP of "status bar + 200",
    /// with a comment saying it was the other's; the score row's 146 was "measured
    /// on a capture" because the HUD did not publish it, and the first guess, 120,
    /// put the ribbon behind the scoreboard. Moving any one slot meant finding all
    /// of its copies. Here each slot is the one above it plus its own height, so
    /// moving one moves everything under it and nothing can be left behind.
    ///
    /// Every value is what the two views drew before this class, unit for unit,
    /// except the ribbon, one unit lower — see <see cref="SCORE_ROW_HEIGHT"/>.
    /// </summary>
    internal static class Systems_HudLayout
    {
        // --- From the top ------------------------------------------------------

        public const int SCORE_ROW_TOP = Systems_UiTheme.STATUS_BAR_HEIGHT;

        /// <summary>
        /// The score row's height, held as a minimum by Systems_HudView, which warns
        /// if its contents ever grow past it — that is the day the ribbon goes
        /// behind the scoreboard again.
        ///
        /// 118, WAS 147 (2026-10-22). The team blocks used to stack tag, score and
        /// timeouts three deep; laid on their side (Systems_HudView.TEAM_BLOCK_WIDTH)
        /// the row is as tall as the quarter-over-clock group and no taller, and
        /// every slot below — ribbon, banner band, hit meter — moved up 29 units
        /// with it. Measured, not estimated: with the slot set short, the warning
        /// above reported the row's own height as 118. (The 147 it replaced had the
        /// same history: the first estimate, 146, put the ribbon a unit under the
        /// scoreboard until the warning said so.)
        /// </summary>
        public const int SCORE_ROW_HEIGHT = 118;

        public const int RIBBON_TOP = SCORE_ROW_TOP + SCORE_ROW_HEIGHT;

        public const int RIBBON_HEIGHT = Systems_WinProbabilityRibbon.HEIGHT;

        /// <summary>
        /// Turf left between the ribbon and the banner band. It stays when a viewer
        /// switches the ribbon off: a banner that jumps with a setting is worse than
        /// a strip of grass.
        /// </summary>
        public const int RIBBON_GAP = 13;

        /// <summary>The result banner, the carrier chip and the call sheet's top.</summary>
        public const int BANNER_TOP = RIBBON_TOP + RIBBON_HEIGHT + RIBBON_GAP;

        /// <summary>
        /// How much the banner band is given before the hit meter. The banner's
        /// headline at TEXT_BANNER plus its subline, with room.
        /// </summary>
        public const int BANNER_BAND_HEIGHT = 170;

        public const int HIT_METER_TOP = BANNER_TOP + BANNER_BAND_HEIGHT;

        // --- From the bottom ---------------------------------------------------

        /// <summary>Pause and playback speed, just above the status footer.</summary>
        public const int TRANSPORT_BOTTOM =
            Systems_UiTheme.STATUS_FOOTER_HEIGHT + Systems_UiTheme.SPACE_M;

        /// <summary>
        /// The dock above the transport: the focus card, or the ticker when no one
        /// is focused. The transport's tap target and a gap either side of it.
        /// </summary>
        public const int CARD_DOCK_BOTTOM = TRANSPORT_BOTTOM
            + Systems_UiTheme.TAP_TARGET + Systems_UiTheme.SPACE_M;
    }
}

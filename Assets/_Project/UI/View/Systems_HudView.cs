using System;
using MessagePipe;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// The in-game scoreboard: score, quarter, clock, possession, down and
    /// distance, a result banner after every play, and the full box score on the
    /// final overlay.
    ///
    /// Reads the game model for anything continuous (the clock) and subscribes for
    /// anything discrete (a play resolving, the final whistle). That split is
    /// deliberate — a subscription per clock tick would fire fifty times a second
    /// to move a label that changes once a second, and polling for a touchdown
    /// would mean comparing scores every frame to detect an event the systems layer
    /// already announces.
    ///
    /// WHAT THE LAYOUT PASS CHANGED, AND WHY:
    ///
    ///   HIERARCHY WAS INVERTED. The two scores were 62 px and the down and
    ///   distance was 26 px, which is exactly backwards. The score changes six or
    ///   seven times in a game; the down changes on every single play and is the
    ///   one number that tells a viewer what they are about to watch. It is now the
    ///   largest thing on the bar and the only thing in the accent colour.
    ///
    ///   THE CHROME WAS OPAQUE. A solid band across the top of a portrait screen
    ///   costs a strip of playfield on every frame for information that is static
    ///   between snaps. It is translucent now, and the bottom bar takes itself out
    ///   of the way entirely while the ball is live.
    ///
    ///   NOTHING KNEW HOW TALL THE SCREEN WAS. The banner sat at a hard-coded
    ///   top: 420 and the buttons at hard-coded widths, authored against a 1920
    ///   panel. The panel matches on width, so on a taller handset all of that
    ///   bunched against the top and left several hundred pixels of dead space.
    ///   Everything vertical is a percentage now.
    ///
    ///   THE BAR WAS TWO ROWS, AND THE TOP ONE WAS MOSTLY EMPTY. The scores sat at
    ///   the outside edges of a row whose centre was deliberately blank, with the
    ///   clock and the down on a second row beneath it: 229 panel units of chrome
    ///   under a 76-unit status bar, a sixth of a 16:9 screen. It is one row now —
    ///   score, clock, down, call, score. See BuildScoreboard for why the blank
    ///   centre is no longer needed.
    ///
    ///   QUIT IS GONE. It did exactly what the status HUD's MENU chip does, from a
    ///   96-unit button parked over the field, and it showed through the final
    ///   scrim as a third button. The top-right corner is the one way out.
    /// </summary>
    public sealed class Systems_HudView : Systems_ScreenView, Systems_IInjectableView
    {
        /// <summary>
        /// Width of each team's block at the two ends of the bar. Fixed and equal,
        /// so the situation group between them is centred on the screen rather
        /// than on whichever score happens to be wider.
        ///
        /// 168, WAS 120, BECAUSE THE BLOCK LIES ON ITS SIDE NOW. Tag, score and
        /// timeouts were stacked three deep, which made the score row 147 units
        /// tall for three short things. The tag and its timeouts are one column
        /// beside the score, so the row is as tall as the clock group and no
        /// taller (Systems_HudLayout.SCORE_ROW_HEIGHT). "HOM" at body size and a
        /// two-digit score at TEXT_SCORE come to about 150 side by side.
        /// </summary>
        private const int TEAM_BLOCK_WIDTH = 168;

        /// <summary>
        /// Width reserved on EACH side of the down-and-distance pill: the quarter
        /// and clock on its left, the play call on its right. Equal on purpose —
        /// the pill is then centred however wide either neighbour's text is, and
        /// neither the clock ticking nor the call chip appearing at the snap can
        /// move it.
        ///
        /// WAS 160, SIZED FOR A "FIELD GOAL" CHIP MEASURED AT 141 UNITS. The chip
        /// now says FG (Systems_DisplayText.PlayCall), so the widest chip is
        /// "QB KEEP" and the widest clock is "5:00" — a quarter is five minutes, so
        /// the "14:22" this was once sized for cannot occur. The 80 units freed
        /// across both flanks go to the pill, the one thing on the bar that changes
        /// every play. 120 is estimated from the 141-unit measurement by glyph
        /// count; check it against a capture if the font changes.
        /// </summary>
        private const int FLANK_WIDTH = 120;

        /// <summary>Metres per second to the miles per hour a broadcast graphic shows.</summary>
        private const float MPS_TO_MPH = 2.23694f;

        /// <summary>
        /// Top of the speed range the carrier chip has a label for. Comfortably above
        /// Systems_SimConstants.MAX_BODY_SPEED (12 m/s, 27 mph).
        /// </summary>
        private const int MAX_DISPLAY_MPH = 40;

        /// <summary>
        /// "0 MPH" to "40 MPH", built once. The chip changes as often as the carrier
        /// speeds up or slows down by a whole mile an hour, which on a breaking run is
        /// several times a second; formatting a string each time would be the only
        /// steady allocation on the HUD.
        /// </summary>
        private static readonly string[] MphLabels = BuildMphLabels();

        /// <summary>
        /// How far a pointer may travel, in panel units, and still be a tap. The
        /// panel is 1080 wide on every device, so this is about a fiftieth of the
        /// screen — more than a thumb wobbles, less than anyone drags on purpose.
        /// </summary>
        private const float TAP_SLOP = 22f;

        /// <summary>
        /// Flex weight of REMATCH against the two secondary actions beside it on
        /// the final sheet: half the row, the other two a quarter each.
        /// </summary>
        private const float PRIMARY_DOCK_WEIGHT = 2f;

        /// <summary>
        /// Height of the final sheet's tabs: a chip and a gap either side. Shorter
        /// than the settings sheet's, which are the sheet's only controls; here they
        /// share it with a row of three full tap targets and twelve lines of
        /// numbers, and every unit they take is one the box score does not get.
        /// </summary>
        private const int FINAL_TAB_HEIGHT = Systems_UiTheme.STATUS_CHIP_HEIGHT + Systems_UiTheme.SPACE_M;

        /// <summary>Side of the pause glyph inside its tap-target-sized button.</summary>
        private const int TRANSPORT_ICON_SIZE = 36;

        /// <summary>Side of the dismiss cross on the focus card.</summary>
        private const int DISMISS_ICON_SIZE = 24;

        /// <summary>Longest press, in seconds, that still counts as a tap.</summary>
        private const float TAP_SECONDS = 0.4f;

        /// <summary>Fraction the shot opens or closes per notch of a mouse wheel.</summary>
        private const float WHEEL_ZOOM_STEP = 0.1f;

        /// <summary>Fingers the field surface tracks: one drags, two pinch.</summary>
        private const int MAX_POINTERS = 2;

        private const int NO_POINTER = -1;

        /// <summary>Metres per second at and above which the focus badge reads SPRINT.</summary>
        private const float SPRINT_SPEED = 6.5f;

        /// <summary>Below this a player is standing, whatever his rigidbody is doing.</summary>
        private const float MOVING_SPEED = 1.5f;

        /// <summary>Drive command at or below which a moving player is pulling up.</summary>
        private const float BRAKE_DRIVE = -0.25f;

        /// <summary>
        /// What the focused player is doing, as one word. Ordered by what a viewer
        /// would say first: he has the ball beats he is running.
        /// </summary>
        private enum FocusState
        {
            None = 0,
            Ball = 1,
            Throw = 2,
            Sprint = 3,
            Brake = 4,
            Run = 5,
            Set = 6
        }

        /// <summary>Indexed by <see cref="FocusState"/>. Literals, so the badge never allocates.</summary>
        private static readonly string[] FocusStateLabels =
        {
            string.Empty, "BALL", "THROW", "SPRINT", "BRAKE", "RUN", "SET"
        };

        private Systems_GameModel _game;

        /// <summary>
        /// Read for one thing only: the play call the quarterback has committed to.
        ///
        /// The game model carries the down, the clock and the score — everything a
        /// broadcast scoreboard has ever shown. It does not and should not carry
        /// what the offense decided to do, because that is not a rules fact, it is
        /// a policy's output, and it lives on the play.
        /// </summary>
        private Systems_PlayModel _play;

        private Systems_BallModel _ball;
        private Systems_PlayerRegistry _registry;
        private Systems_SimSpeedSystem _simSpeed;

        private Systems_BoxScore _boxScore;
        private ISubscriber<Systems_DownResolvedMessage> _resolvedSubscriber;
        private ISubscriber<Systems_GameOverMessage> _gameOverSubscriber;
        private ISubscriber<Systems_PlaySnappedMessage> _snappedSubscriber;
        private ISubscriber<Systems_HighlightPlaybackMessage> _highlightSubscriber;
        private ISubscriber<Systems_HighlightsReadyMessage> _readySubscriber;
        private IPublisher<Systems_OpenHighlightsMessage> _openHighlightsPublisher;

        private IDisposable _resolvedSubscription;
        private IDisposable _gameOverSubscription;
        private IDisposable _snappedSubscription;
        private IDisposable _highlightSubscription;
        private IDisposable _readySubscription;

        /// <summary>
        /// Set at the final whistle and never cleared. The final overlay's own
        /// visibility used to double as "the game is over", and it no longer can:
        /// it is taken down while a highlight plays on the field. This is what
        /// keeps that overlay from ever being shown mid-game, and the call chip
        /// and carrier chip from coming back while it is down.
        /// </summary>
        private bool _gameOver;

        private Label _homeScore;
        private Label _awayScore;
        private Label _quarter;
        private Label _clock;
        private Label _downAndDistance;
        private Label _fieldPosition;
        private VisualElement _homePossession;
        private VisualElement _awayPossession;

        private Systems_UiOverlay _bannerOverlay;
        private Label _bannerHeadline;
        private Label _bannerDetail;

        private Systems_UiOverlay _finalOverlay;

        /// <summary>
        /// The down-and-distance pill. Held so the final whistle can take it down —
        /// see <see cref="OnGameOver"/>. The clock beside it is deliberately left up.
        /// </summary>
        private VisualElement _situationPill;
        private VisualElement _callChip;
        private Label _callLabel;
        private Label _finalHeadline;

        /// <summary>
        /// Where the box score card goes at the whistle. Empty until then: the
        /// numbers do not exist before the game ends, and a card of zeroes built
        /// at kickoff would only have to be thrown away.
        /// </summary>
        private VisualElement _finalCardHost;

        // The final sheet's tabs. BOX SCORE and SCORING are panes in one host —
        // the box score in flow, sizing it, and the scoring card pinned over it —
        // and HIGHLIGHTS is a tab that hands the screen to the reel. It stays off
        // until Systems_ReplayView says it has something to show.
        private Button _boxScoreTab;
        private Button _scoringTab;
        private Button _highlightsTab;
        private VisualElement _boxScorePane;
        private VisualElement _scoringPane;
        private int _highlightCount;

        // The scoring summary, collected as the downs resolve. Allocated once at
        // the field declarations; a string per scoring play, which is a handful a
        // game, and nothing on any other down.
        private readonly Systems_ScoringCard.Entry[] _scoring = new Systems_ScoringCard.Entry[32];
        private int _scoringCount;
        private int _homeRunning;
        private int _awayRunning;
        private readonly int[] _homeByPeriod = new int[Systems_ScoringCard.PERIODS];
        private readonly int[] _awayByPeriod = new int[Systems_ScoringCard.PERIODS];

        /// <summary>Last whole second rendered, so the clock label is not rebuilt per frame.</summary>
        private int _lastClockKey = -1;

        private int _lastHomeScore = -1;
        private int _lastAwayScore = -1;
        private int _lastDown = -1;
        private int _lastQuarter = -1;

        /// <summary>
        /// Set the first time the game is seen in overtime, and never cleared.
        /// Sticky because the phase moves on to Final at the whistle, and a game
        /// that was settled in overtime should still say so under the result.
        /// </summary>
        private bool _sawOvertime;

        /// <summary>
        /// Deliberately seeded to a value the enum does not define, so the first
        /// comparison in RefreshCall always misses and the chip is initialised
        /// once. None is a real state — "the quarterback has not decided" — and
        /// starting there would leave the chip unbuilt until the first call.
        /// </summary>
        private Systems_PlayCall _lastCall = (Systems_PlayCall)(-1);
        private Systems_PlayKind _lastKind = (Systems_PlayKind)(-1);

        // The carrier chip: who has the ball, how fast, how tired. Each guarded on
        // its own last value, so a frame in which nothing visibly changed writes
        // nothing.
        private VisualElement _carrierChip;
        private Label _carrierRole;
        private Label _carrierSpeed;
        private VisualElement _carrierFatigueFill;
        private int _lastCarrierId = -1;
        private int _lastMph = -1;
        private int _lastFatiguePercent = -1;

        // The drive in progress, for the line under the result banner. Keyed on the
        // game's drive index captured at the SNAP, because by the time a scoring
        // down is resolved the index may already belong to the next possession.
        private Label _bannerDrive;
        private int _snapDriveIndex = -1;
        private int _countedDriveIndex = -1;
        private int _drivePlays;
        private float _driveYards;

        private Button _speedButton;
        private Systems_UiIcon _pauseIcon;
        private Button _autoCamButton;

        /// <summary>
        /// The pause, speed and AUTO CAM strip. Held so the final whistle can take
        /// it away: there is no simulation left to pause, and the docked result
        /// sheet goes where it was.
        /// </summary>
        private VisualElement _transport;
        private bool _lastPaused;
        private bool _lastManualView;

        // Timeouts left, three pips under each team's name. Guarded on the count.
        private VisualElement[] _homeTimeoutPips;
        private VisualElement[] _awayTimeoutPips;
        private int _lastHomeTimeouts = -1;
        private int _lastAwayTimeouts = -1;

        // The viewer's hand on the broadcast. The system records what was asked
        // for; the model is read back for the card and the AUTO CAM chip.
        private Systems_SpectatorModel _spectator;
        private Systems_SpectatorSystem _spectatorSystem;
        private Systems_IIntentSource _intents;

        /// <summary>
        /// The camera a tap is converted through. Cached once in Start; only ever
        /// read, never written — Systems_BroadcastCameraView stays its one writer.
        /// </summary>
        private Camera _fieldCamera;

        private VisualElement _fieldSurface;
        private readonly int[] _pointerIds = { NO_POINTER, NO_POINTER };
        private readonly Vector2[] _pointerPositions = new Vector2[MAX_POINTERS];
        private Vector2 _tapStart;
        private float _tapStartTime;
        private bool _tapCandidate;

        /// <summary>Set once the score-row warning has been logged; it is one fault, not one per layout.</summary>
        private bool _scoreRowOverflowReported;

        // The focus card: the tapped player's role, state, speed and fatigue.
        private VisualElement _focusCard;
        private Label _focusRole;
        private Label _focusState;
        private Label _focusSpeed;
        private VisualElement _focusFatigueFill;
        private int _lastFocusId = Systems_SpectatorModel.NO_FOCUS;
        private int _lastFocusMph = -1;
        private int _lastFocusFatiguePercent = -1;
        private FocusState _lastFocusState = FocusState.None;

        /// <summary>
        /// The QB challenge's human (Systems_HumanControlModel). When it is
        /// enabled the field surface stops being the spectator's — no pan, no
        /// focus tap — and becomes the quarterback's pointer: a press is a click
        /// (hike, throw, handoff), a move is where he runs.
        /// </summary>
        private Systems_HumanControlModel _humanControl;

        private Systems_GameVariant _variant;

        /// <summary>The nine-play sheet; built only for the QB challenge.</summary>
        private Systems_PlaySheetCard _playSheet;

        /// <summary>
        /// Where the pointer last was on the panel, kept so its point on the field
        /// can be re-read every frame: the camera follows the play, so the man
        /// under a resting mouse changes while it does not move.
        /// </summary>
        private Vector2 _humanPointerPanel;

        private bool _hasHumanPointer;

        /// <summary>
        /// What a tap did and would do, drawn on the field: a ring under the
        /// team-mate a tap would hand or pitch to, and a dot on the spot the
        /// carrier is running to or the pass is coming down on. Without them the
        /// only cue was the hint line's text, a glance away from the play.
        /// </summary>
        private VisualElement _hoverRing;

        private VisualElement _pointerDot;

        /// <summary>Ring diameter in metres on the field: a body and a stride around it.</summary>
        private const float HOVER_RING_METRES = 2.6f;

        private const float POINTER_DOT_UNITS = 22f;

        private bool IsHumanPointer => _humanControl != null && _humanControl.IsEnabled && !_gameOver;

        [Inject]
        public void Construct(
            Systems_GameModel game,
            Systems_PlayModel play,
            Systems_BallModel ball,
            Systems_PlayerRegistry registry,
            Systems_SimSpeedSystem simSpeed,
            Systems_SpectatorModel spectator,
            Systems_SpectatorSystem spectatorSystem,
            Systems_IIntentSource intents,
            Systems_BoxScore boxScore,
            ISubscriber<Systems_DownResolvedMessage> resolvedSubscriber,
            ISubscriber<Systems_GameOverMessage> gameOverSubscriber,
            ISubscriber<Systems_PlaySnappedMessage> snappedSubscriber,
            ISubscriber<Systems_HighlightPlaybackMessage> highlightSubscriber,
            ISubscriber<Systems_HighlightsReadyMessage> readySubscriber,
            IPublisher<Systems_OpenHighlightsMessage> openHighlightsPublisher,
            Systems_HumanControlModel humanControl,
            Systems_GameVariant variant)
        {
            _humanControl = humanControl;
            _variant = variant;
            _game = game;
            _play = play;
            _ball = ball;
            _registry = registry;
            _simSpeed = simSpeed;
            _spectator = spectator;
            _spectatorSystem = spectatorSystem;
            _intents = intents;
            _boxScore = boxScore;
            _resolvedSubscriber = resolvedSubscriber;
            _gameOverSubscriber = gameOverSubscriber;
            _snappedSubscriber = snappedSubscriber;
            _highlightSubscriber = highlightSubscriber;
            _readySubscriber = readySubscriber;
            _openHighlightsPublisher = openHighlightsPublisher;
        }

        protected override void Start()
        {
            // Before base.Start, which builds the UI: once, here, and never per
            // frame (.claude/rules/performance.md).
            _fieldCamera = Camera.main;

            base.Start();

            _resolvedSubscription = _resolvedSubscriber?.Subscribe(OnDownResolved);
            _gameOverSubscription = _gameOverSubscriber?.Subscribe(OnGameOver);
            _snappedSubscription = _snappedSubscriber?.Subscribe(OnSnapped);
            _highlightSubscription = _highlightSubscriber?.Subscribe(OnHighlightPlayback);
            _readySubscription = _readySubscriber?.Subscribe(OnHighlightsReady);
        }

        private void OnDestroy()
        {
            _resolvedSubscription?.Dispose();
            _gameOverSubscription?.Dispose();
            _snappedSubscription?.Dispose();
            _highlightSubscription?.Dispose();
            _readySubscription?.Dispose();
        }

        protected override void BuildUi()
        {
            if (_game == null)
            {
                Debug.LogError(
                    $"{nameof(Systems_HudView)} was never injected — it needs a "
                    + "Systems_GameLifetimeScope in Game mode. The HUD will stay blank.");
                return;
            }

            // A Layer, not a Screen: this tree sits over a live field and must not
            // paint a background or swallow taps aimed at the game.
            VisualElement layer = Systems_UiTheme.Layer();
            Root.Add(layer);

            // FIRST, SO IT IS UNDER EVERYTHING. The field surface is the one element
            // in this tree that takes taps aimed at the game, and it must lose to
            // every button drawn after it.
            layer.Add(BuildFieldSurface());

            layer.Add(BuildScoreboard());

            // ORDER IS Z-ORDER. The carrier chip and the speed control sit over the
            // field, the banner over them, and the final overlay over everything.
            // The way out of a live game is the status HUD's MENU chip, on its own
            // panel above all of this.
            layer.Add(BuildCarrierChip());
            layer.Add(BuildFocusCard());
            layer.Add(BuildSpeedControl());

            // The QB challenge's play sheet, over the field and under the banner.
            if (_humanControl != null && _humanControl.IsEnabled)
            {
                _playSheet = new Systems_PlaySheetCard(_humanControl, _play, _registry);
                layer.Add(_playSheet.Root);

                _hoverRing = new VisualElement { name = "HoverRing" };
                _hoverRing.style.position = Position.Absolute;
                _hoverRing.pickingMode = PickingMode.Ignore;
                Systems_UiTheme.SetBorder(_hoverRing, 4, Systems_UiTheme.Accent);
                _hoverRing.style.display = DisplayStyle.None;
                layer.Add(_hoverRing);

                _pointerDot = new VisualElement { name = "PointerDot" };
                _pointerDot.style.position = Position.Absolute;
                _pointerDot.style.width = POINTER_DOT_UNITS;
                _pointerDot.style.height = POINTER_DOT_UNITS;
                _pointerDot.style.backgroundColor = new Color(1f, 1f, 1f, 0.55f);
                _pointerDot.pickingMode = PickingMode.Ignore;
                Systems_UiTheme.SetRadius(_pointerDot, (int)(POINTER_DOT_UNITS / 2f));
                Systems_UiTheme.SetBorder(_pointerDot, 2, Systems_UiTheme.Surface);
                _pointerDot.style.display = DisplayStyle.None;
                layer.Add(_pointerDot);
            }

            _bannerOverlay = BuildBanner();
            layer.Add(_bannerOverlay.Root);

            _finalOverlay = BuildFinalOverlay();
            layer.Add(_finalOverlay.Root);

            RefreshScoreboard(true);
        }

        // --- Scoreboard --------------------------------------------------------

        /// <summary>
        /// One row: home score, the situation, away score.
        ///
        /// WHY THE TOP CENTRE IS USABLE AGAIN. The previous layout kept the middle
        /// of its first row empty because a centre punch-hole clipped the clock's
        /// leading digit on a 1440x3088 handset. That was true when it was
        /// written — Systems_ScreenView's safe-area inset was never being applied
        /// at all, a bug fixed since. With the inset working, and the status bar
        /// taking the 76 units directly beneath it, the first thing this bar draws
        /// is already a full row clear of any cutout. The blank centre was
        /// protecting against a failure that can no longer reach it, and it cost a
        /// second row to do so.
        /// </summary>
        private void OnScoreRowGeometry(GeometryChangedEvent change)
        {
            if (_scoreRowOverflowReported
                || change.newRect.height <= Systems_HudLayout.SCORE_ROW_HEIGHT + 0.5f)
            {
                return;
            }

            _scoreRowOverflowReported = true;
            Debug.LogWarning(
                "[PoFootball] The score row is " + Mathf.RoundToInt(change.newRect.height)
                + " units tall and its slot in Systems_HudLayout is "
                + Systems_HudLayout.SCORE_ROW_HEIGHT
                + ": the win-probability ribbon is drawn behind it. Raise SCORE_ROW_HEIGHT.");
        }

        private VisualElement BuildScoreboard()
        {
            VisualElement bar = Systems_UiTheme.Row();
            bar.name = "ScoreRow";
            bar.style.position = Position.Absolute;
            bar.style.left = 0;
            bar.style.right = 0;
            bar.style.justifyContent = Justify.SpaceBetween;

            // BELOW THE STATUS HUD, NOT UNDER IT. Systems_StatusHudView reserves the
            // top strip on every screen for the title, the frame rate and MENU; at
            // top: 0 the score row rendered underneath it and the away team's points
            // came out behind the MENU chip.
            bar.style.top = Systems_HudLayout.SCORE_ROW_TOP;

            // HELD AT ITS SLOT, AND WATCHED. The ribbon under this row is placed at
            // the slot's bottom, not at wherever this row happens to end. A minimum
            // keeps the row from coming up short of it; the callback says so if the
            // contents ever push past it, because then the ribbon is behind the
            // scoreboard and nothing else would notice.
            bar.style.minHeight = Systems_HudLayout.SCORE_ROW_HEIGHT;
            bar.RegisterCallback<GeometryChangedEvent>(OnScoreRowGeometry);

            // OPAQUE (2026-10-16 review). It was translucent "so the top of the field
            // still reads through the chrome", and what read through was every yard
            // line and hash mark, drawn across the clock and the down-and-distance;
            // on a tall phone the camera holds the field under the bar more often,
            // not less. A scoreboard is a surface in front of the play.
            bar.style.backgroundColor = Systems_UiTheme.Surface;

            // A lit top edge and a dark bottom one, which is the only way to say
            // "this is a surface in front of the field" on a panel that has no
            // shadows. Without it a translucent bar over dark turf reads as a patch
            // where the grass happens to be a different colour.
            Systems_UiTheme.ApplyElevation(bar);
            Systems_UiTheme.SetPadding(
                bar, Systems_UiTheme.SPACE_S, Systems_UiTheme.SPACE_M);

            bar.Add(BuildTeamBlock(
                Systems_TeamId.Home, out _homeScore, out _homePossession, out _homeTimeoutPips));
            bar.Add(BuildSituation());
            bar.Add(BuildTeamBlock(
                Systems_TeamId.Away, out _awayScore, out _awayPossession, out _awayTimeoutPips));
            return bar;
        }

        /// <summary>
        /// Clock, down and distance, play call — in that order, with the down in
        /// the middle of the screen.
        ///
        /// The down and distance keeps the emphasis it was given: a pill in the
        /// accent colour, the only thing on the bar that is. The two groups either
        /// side of it are the same fixed width, which is what holds it still.
        /// </summary>
        private VisualElement BuildSituation()
        {
            VisualElement situation = Systems_UiTheme.Row();

            // The quarter ABOVE the clock, both against the pill. Side by side they
            // needed a reserved box for the clock to stop the row re-flowing every
            // second, and the box left a four-glyph time floating a hundred units
            // from its own quarter — "1ST        3:14" read as two unrelated
            // labels. Stacked and right-aligned in a fixed-width column there is
            // nothing beside the digits to push: the text changes width and
            // nothing moves, which is the same guarantee without the gap.
            VisualElement clockGroup = Systems_UiTheme.Column();
            clockGroup.style.width = FLANK_WIDTH;
            clockGroup.style.alignItems = Align.FlexEnd;

            _quarter = Systems_UiTheme.Caption("1ST");

            _clock = Systems_UiTheme.Text(
                "5:00", Systems_UiTheme.TEXT_TITLE,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);

            clockGroup.Add(_quarter);
            clockGroup.Add(_clock);
            situation.Add(clockGroup);

            VisualElement pill = Systems_UiTheme.Row();
            pill.style.marginLeft = Systems_UiTheme.SPACE_M;
            pill.style.marginRight = Systems_UiTheme.SPACE_M;
            pill.style.backgroundColor = new Color(
                Systems_UiTheme.Accent.r, Systems_UiTheme.Accent.g,
                Systems_UiTheme.Accent.b, 0.14f);
            Systems_UiTheme.SetPadding(pill, Systems_UiTheme.SPACE_XS, Systems_UiTheme.SPACE_M);
            Systems_UiTheme.SetRadius(pill, Systems_UiTheme.RADIUS);

            _downAndDistance = Systems_UiTheme.Text(
                "1st & 10", Systems_UiTheme.TEXT_TITLE,
                Systems_UiTheme.Accent, FontStyle.Bold);

            _fieldPosition = Systems_UiTheme.Text(
                "OWN 25", Systems_UiTheme.TEXT_BODY, Systems_UiTheme.TextMuted);
            _fieldPosition.style.marginLeft = Systems_UiTheme.SPACE_M;

            Systems_UiTheme.ApplyElevation(pill);
            Systems_UiTheme.EnablePulse(pill);

            pill.Add(_downAndDistance);
            pill.Add(_fieldPosition);
            situation.Add(pill);

            VisualElement callSlot = Systems_UiTheme.Row();
            callSlot.style.width = FLANK_WIDTH;
            callSlot.Add(BuildCallChip());
            situation.Add(callSlot);

            _situationPill = pill;
            return situation;
        }

        /// <summary>
        /// The play the quarterback committed to, the moment it commits.
        ///
        /// WHY IT IS WORTH A PLACE ON A PORTRAIT SCOREBOARD. The call is the single
        /// most consequential decision in the game and it is made by a discrete
        /// action head that has its own brain, its own entropy bonus and its own
        /// telemetry channel (Agent_Telemetry writes Call/Entropy precisely because
        /// a collapsed play-caller is invisible in the trainer's own numbers). On
        /// screen it was invisible too: the only way to know a pass had been called
        /// was to watch for a throw, which is the outcome, not the decision.
        ///
        /// IT APPEARS LATE, AND THAT IS THE SIMULATION SHOWING THROUGH. Nothing is
        /// committed until DROPBACK_TICKS have elapsed — the quarterback reads the
        /// rush first (see Agent_ActionContract, revision 2) — so the chip is empty
        /// for the first fraction of a second of every snap. That gap is the read,
        /// and it is worth seeing.
        /// </summary>
        private VisualElement BuildCallChip()
        {
            VisualElement chip = Systems_UiTheme.Row();
            chip.style.backgroundColor = new Color(
                Systems_UiTheme.TextPrimary.r,
                Systems_UiTheme.TextPrimary.g,
                Systems_UiTheme.TextPrimary.b,
                0.10f);

            Systems_UiTheme.SetPadding(
                chip, Systems_UiTheme.SPACE_XS, Systems_UiTheme.SPACE_S);

            Systems_UiTheme.SetRadius(chip, Systems_UiTheme.RADIUS);
            Systems_UiTheme.ApplyElevation(chip);

            _callLabel = Systems_UiTheme.Text(
                string.Empty,
                Systems_UiTheme.TEXT_BODY,
                Systems_UiTheme.TextPrimary,
                FontStyle.Bold);

            chip.Add(_callLabel);

            // Hidden, NOT removed from layout. As display: none the chip took its
            // width out of a centred row at every snap and put it back at every
            // whistle, so the clock and the pill stepped sideways twice a play.
            // The slot it sits in is a fixed width either way; visibility only
            // decides whether anything is drawn in it.
            chip.style.visibility = Visibility.Hidden;

            _callChip = chip;
            return chip;
        }

        /// <summary>
        /// Guarded on the call itself, so a frame in which nothing was decided
        /// writes nothing — the same discipline the rest of Update follows.
        /// </summary>
        private void RefreshCall()
        {
            // Nothing is being called once the game is over, and the final overlay
            // has taken the chip down — see OnGameOver.
            if (_play == null || _callChip == null || _gameOver || _finalOverlay.IsVisible)
            {
                return;
            }

            Systems_PlayCall call = _play.Call;
            Systems_PlayKind kind = _play.Kind;

            if (call == _lastCall && kind == _lastKind)
            {
                return;
            }

            _lastCall = call;
            _lastKind = kind;

            // Named by the kind of snap where its call is forced — PAT, KNEEL,
            // SPIKE — rather than "FG", "QB KEEP" and "PASS". Literals throughout.
            string label = Systems_DisplayText.PlayCall(kind, call);

            if (label.Length == 0)
            {
                _callChip.style.visibility = Visibility.Hidden;
                return;
            }

            _callLabel.text = label;
            _callChip.style.visibility = Visibility.Visible;
        }

        private static VisualElement BuildTeamBlock(
            Systems_TeamId team,
            out Label scoreLabel,
            out VisualElement possessionDot,
            out VisualElement[] timeoutPips)
        {
            // ON ITS SIDE: tag over timeouts in one column, the score beside it,
            // nearest the middle — "HOM 7 ... 14 AWY", the order a broadcast bar
            // reads in. See TEAM_BLOCK_WIDTH for the height this gave back.
            bool home = team == Systems_TeamId.Home;

            VisualElement block = Systems_UiTheme.Row();
            block.style.alignItems = Align.Center;
            block.style.justifyContent = home ? Justify.FlexStart : Justify.FlexEnd;
            block.style.width = TEAM_BLOCK_WIDTH;

            VisualElement identity = Systems_UiTheme.Column();
            identity.style.alignItems = Align.Center;

            VisualElement nameRow = Systems_UiTheme.Row();
            nameRow.style.justifyContent = Justify.Center;

            // The team's own colour, not the accent. This dot answers "who has the
            // ball", so it should be the same colour as that team's shapes on the
            // field and its column in the box score — and the accent is reserved
            // for the chains, which this is not.
            possessionDot = new VisualElement();
            possessionDot.style.width = Systems_UiTheme.DOT_SIZE;
            possessionDot.style.height = Systems_UiTheme.DOT_SIZE;
            possessionDot.style.backgroundColor = Systems_UiTheme.ColorOf(team);
            possessionDot.style.marginRight = Systems_UiTheme.SPACE_XS;
            possessionDot.style.visibility = Visibility.Hidden;
            Systems_UiTheme.SetRadius(possessionDot, Systems_UiTheme.DOT_SIZE / 2);

            // Was TEXT_CAPTION at 20 px, which is smaller than the muted field
            // position and unreadable at arm's length. A team tag is an identity,
            // not a footnote.
            Label name = Systems_UiTheme.Text(
                Systems_DisplayText.TeamTag(team), Systems_UiTheme.TEXT_BODY,
                Systems_UiTheme.ColorOf(team), FontStyle.Bold);
            name.style.letterSpacing = 2f;

            nameRow.Add(possessionDot);
            nameRow.Add(name);

            // Still the second-largest thing on the bar, but no longer shouting over
            // the down and distance.
            scoreLabel = Systems_UiTheme.Text(
                "0", Systems_UiTheme.TEXT_SCORE, Systems_UiTheme.TextPrimary, FontStyle.Bold);

            // Scoring is the rarest and most important thing that happens. It used
            // to redraw a glyph in place and nothing else.
            Systems_UiTheme.EnablePulse(scoreLabel);

            identity.Add(nameRow);
            identity.Add(BuildTimeoutPips(team, out timeoutPips));

            if (home)
            {
                scoreLabel.style.marginLeft = Systems_UiTheme.SPACE_M;
                block.Add(identity);
                block.Add(scoreLabel);
            }
            else
            {
                scoreLabel.style.marginRight = Systems_UiTheme.SPACE_M;
                block.Add(scoreLabel);
                block.Add(identity);
            }

            return block;
        }

        /// <summary>
        /// Timeouts left, as the three bars every broadcast scoreboard draws under
        /// a team's name. A spent one dims rather than disappears, so the row keeps
        /// its width and a viewer can count what is gone as well as what is left.
        /// </summary>
        private static VisualElement BuildTimeoutPips(Systems_TeamId team, out VisualElement[] pips)
        {
            const int PIP_WIDTH = 20;
            const int PIP_HEIGHT = 4;

            VisualElement row = Systems_UiTheme.Row();
            row.style.justifyContent = Justify.Center;
            row.style.marginTop = Systems_UiTheme.SPACE_XS;
            row.pickingMode = PickingMode.Ignore;

            pips = new VisualElement[Systems_GameRules.TIMEOUTS_PER_HALF];

            for (int pipIndex = 0; pipIndex < pips.Length; pipIndex++)
            {
                VisualElement pip = new VisualElement();
                pip.style.width = PIP_WIDTH;
                pip.style.height = PIP_HEIGHT;
                pip.style.marginLeft = Systems_UiTheme.SPACE_XS / 2;
                pip.style.marginRight = Systems_UiTheme.SPACE_XS / 2;
                pip.style.backgroundColor = Systems_UiTheme.ColorOf(team);
                pip.pickingMode = PickingMode.Ignore;
                Systems_UiTheme.SetRadius(pip, PIP_HEIGHT / 2);

                pips[pipIndex] = pip;
                row.Add(pip);
            }

            return row;
        }

        private static void RefreshTimeoutPips(VisualElement[] pips, int remaining)
        {
            const float SPENT_OPACITY = 0.18f;

            for (int pipIndex = 0; pipIndex < pips.Length; pipIndex++)
            {
                pips[pipIndex].style.opacity = pipIndex < remaining ? 1f : SPENT_OPACITY;
            }
        }

        // --- Carrier chip and speed control ------------------------------------

        /// <summary>
        /// Who has the ball, how fast he is going and how much he has left, while
        /// the ball is live.
        ///
        /// WHY THESE THREE. The carrier's rim glow says who; nothing on screen said
        /// how fast, and on a ten-pixel shape a back at full speed and one jogging
        /// look alike. Fatigue is a real input to the simulation — it scales every
        /// force the carrier applies (Agent_FootballPlayer.OnActionReceived) — and
        /// the only trace of it was a shader tint too subtle to read on a phone.
        ///
        /// ONE CHIP, NOT TWENTY-TWO. A bar over every player would bury the shapes
        /// it is labelling. The carrier is the one player a viewer is watching.
        ///
        /// IT SHARES THE BANNER'S PLACE, AND NEVER ITS TIME. The chip is up only while
        /// the ball is live and the result banner only while it is dead, so the
        /// space directly under the scoreboard is used by both without overlap.
        /// </summary>
        private VisualElement BuildCarrierChip()
        {
            VisualElement strip = Systems_UiTheme.Row();
            strip.style.position = Position.Absolute;
            strip.style.left = 0;
            strip.style.right = 0;
            strip.style.top = Systems_HudLayout.BANNER_TOP;
            strip.style.justifyContent = Justify.Center;
            strip.pickingMode = PickingMode.Ignore;

            VisualElement chip = Systems_UiTheme.Row();
            chip.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            chip.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetPadding(chip, Systems_UiTheme.SPACE_XS, Systems_UiTheme.SPACE_M);
            Systems_UiTheme.SetRadius(chip, Systems_UiTheme.RADIUS);
            Systems_UiTheme.ApplyElevation(chip);

            _carrierRole = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_BODY,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);

            _carrierSpeed = Systems_UiTheme.Text(
                MphLabels[0], Systems_UiTheme.TEXT_BODY, Systems_UiTheme.TextPrimary);
            _carrierSpeed.style.marginLeft = Systems_UiTheme.SPACE_M;

            VisualElement track = BuildFatigueGauge(out _carrierFatigueFill);

            chip.Add(_carrierRole);
            chip.Add(_carrierSpeed);
            chip.Add(track);
            strip.Add(chip);

            strip.style.visibility = Visibility.Hidden;
            _carrierChip = strip;
            return strip;
        }

        /// <summary>
        /// The transport: pause, and the viewer's playback speed — 1x / 2x / 4x /
        /// 0.5x, one tap to cycle — with AUTO CAM beside them once the shot has
        /// been moved by hand.
        ///
        /// Bottom centre, above the status footer: the thumb's natural reach on a
        /// portrait phone, and the one edge of the screen the status HUD leaves
        /// free between its two bottom corners. Systems_SimSpeedSystem owns the
        /// time scale and its reset; this only asks it to move.
        /// </summary>
        private VisualElement BuildSpeedControl()
        {
            VisualElement strip = Systems_UiTheme.Row();
            strip.style.position = Position.Absolute;
            strip.style.left = 0;
            strip.style.right = 0;
            strip.style.bottom = Systems_HudLayout.TRANSPORT_BOTTOM;
            strip.style.justifyContent = Justify.Center;
            strip.pickingMode = PickingMode.Ignore;

            // PAUSE, SPEED, AUTO CAM — in that order, with the speed in the middle
            // where it has always been. AUTO CAM takes no room until the viewer has
            // moved the shot, so the two controls that are always there do not
            // shift when it appears on a screen where it never has.
            //
            // PAUSE IS A GLYPH, AND SQUARE. The word needed a button one and a half
            // tap targets wide and swapped to "PLAY" — a different width in a
            // different colour — every time it was pressed. Two bars and a
            // triangle are the same size, need no reading, and give 48 units of
            // the field back.
            Button pauseButton = BuildTransportButton(string.Empty, OnPauseTapped);
            pauseButton.style.minWidth = Systems_UiTheme.TAP_TARGET;
            pauseButton.style.marginRight = Systems_UiTheme.SPACE_S;
            _pauseIcon = Systems_UiIcon.AddTo(
                pauseButton, Systems_UiIcon.Glyph.Pause, TRANSPORT_ICON_SIZE,
                Systems_UiTheme.TextPrimary, "Pause");
            strip.Add(pauseButton);

            _speedButton = BuildTransportButton(
                SpeedLabel(_simSpeed == null ? 1f : _simSpeed.Speed), OnSpeedTapped);
            _speedButton.style.fontSize = Systems_UiTheme.TEXT_TITLE;
            strip.Add(_speedButton);

            _autoCamButton = BuildTransportButton("AUTO CAM", OnAutoCamTapped);
            _autoCamButton.style.position = Position.Absolute;
            _autoCamButton.style.right = Systems_UiTheme.SPACE_M;
            _autoCamButton.style.display = DisplayStyle.None;
            strip.Add(_autoCamButton);

            _transport = strip;
            return strip;
        }

        private static Button BuildTransportButton(string label, Action onClick)
        {
            Button button = Systems_UiTheme.Button(label, Systems_UiTheme.SurfaceRaised, onClick);
            button.style.color = Systems_UiTheme.TextPrimary;
            button.style.minWidth = Systems_UiTheme.TAP_TARGET * 1.5f;
            button.style.height = Systems_UiTheme.TAP_TARGET;
            button.style.fontSize = Systems_UiTheme.TEXT_BODY;
            button.style.opacity = 0.85f;
            return button;
        }

        private void OnSpeedTapped()
        {
            if (_simSpeed == null)
            {
                return;
            }

            _simSpeed.Cycle();
            _speedButton.text = SpeedLabel(_simSpeed.Speed);
        }

        private void OnPauseTapped()
        {
            if (_simSpeed == null)
            {
                return;
            }

            _simSpeed.TogglePause();
        }

        private void OnAutoCamTapped()
        {
            _spectatorSystem?.ResetView();
        }

        /// <summary>
        /// Keeps the pause label and the AUTO CAM chip in step with what they
        /// control. Polled, because the things they reflect have other writers: a
        /// drag on the field makes the view manual, and a rematch resets a pause.
        /// </summary>
        private void RefreshTransport()
        {
            if (_pauseIcon == null)
            {
                return;
            }

            bool paused = _simSpeed != null && _simSpeed.IsPaused;

            if (paused != _lastPaused)
            {
                _lastPaused = paused;

                // The icon shows what a press will DO, as every transport does:
                // paused, it is the play triangle, in the colour of good news.
                _pauseIcon.Set(
                    paused ? Systems_UiIcon.Glyph.Play : Systems_UiIcon.Glyph.Pause,
                    paused ? Systems_UiTheme.Positive : Systems_UiTheme.TextPrimary);
            }

            bool manual = _spectator != null && _spectator.IsManual && !_gameOver;

            if (manual != _lastManualView)
            {
                _lastManualView = manual;
                _autoCamButton.style.display = manual ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static string SpeedLabel(float speed)
        {
            if (speed >= 4f)
            {
                return "4×";
            }

            if (speed >= 2f)
            {
                return "2×";
            }

            return speed >= 1f ? "1×" : "0.5×";
        }

        // --- The field surface: tap, drag, pinch ---------------------------------

        /// <summary>
        /// A transparent, full-screen element under every other piece of HUD, which
        /// is what turns a touch on the field into something the game can act on.
        ///
        /// WHY UI TOOLKIT EVENTS AND NOT AN InputAction. A pointer action fires for
        /// every touch on the display, including the ones that landed on PAUSE or
        /// MENU, and working out which is which means repeating the panel's own hit
        /// test. Taking the events here means the panel has already done it: this
        /// element only ever hears the touches nothing above it wanted.
        ///
        /// THE ONE PICKABLE THING IN A TREE THAT IS OTHERWISE DELIBERATELY NOT.
        /// Systems_ScreenView explains at length why a full-screen pickable element
        /// swallows taps meant for panels underneath. There is nothing underneath
        /// during a game — the field is not UI — and the two documents drawn above
        /// this one (the replay strip and the status HUD) are picked first. It is
        /// switched off at the final whistle, when the post-game screens take over.
        /// </summary>
        private VisualElement BuildFieldSurface()
        {
            _fieldSurface = new VisualElement { name = "FieldSurface" };
            Systems_UiTheme.FillParent(_fieldSurface);
            _fieldSurface.pickingMode = PickingMode.Position;

            _fieldSurface.RegisterCallback<PointerDownEvent>(OnFieldPointerDown);
            _fieldSurface.RegisterCallback<PointerMoveEvent>(OnFieldPointerMove);
            _fieldSurface.RegisterCallback<PointerUpEvent>(OnFieldPointerUp);
            _fieldSurface.RegisterCallback<PointerCancelEvent>(OnFieldPointerCancel);
            _fieldSurface.RegisterCallback<WheelEvent>(OnFieldWheel);
            _fieldSurface.RegisterCallback<PointerLeaveEvent>(OnFieldPointerLeave);

            return _fieldSurface;
        }

        private void OnFieldPointerDown(PointerDownEvent evt)
        {
            if (IsHumanPointer)
            {
                // The primary button or a finger; anything else is ignored.
                if (evt.button == 0)
                {
                    HumanPointer(evt.position, true);
                }

                return;
            }

            int slot = PointerSlot(NO_POINTER);

            if (slot < 0)
            {
                return;
            }

            Vector2 position = evt.position;

            _pointerIds[slot] = evt.pointerId;
            _pointerPositions[slot] = position;
            _fieldSurface.CapturePointer(evt.pointerId);

            // Only a lone finger can tap. A second one down makes it a pinch, and
            // lifting either afterwards must not select whoever was underneath.
            _tapCandidate = ActivePointerCount() == 1;
            _tapStart = position;
            _tapStartTime = Time.unscaledTime;
        }

        private void OnFieldPointerMove(PointerMoveEvent evt)
        {
            if (IsHumanPointer)
            {
                HumanPointer(evt.position, false);
                return;
            }

            int slot = PointerSlot(evt.pointerId);

            if (slot < 0)
            {
                return;
            }

            Vector2 position = evt.position;
            Vector2 previous = _pointerPositions[slot];

            if (ActivePointerCount() == MAX_POINTERS)
            {
                Vector2 other = _pointerPositions[1 - slot];
                float before = (previous - other).magnitude;
                float after = (position - other).magnitude;

                // Fingers apart closes in, so the framing shrinks by the inverse.
                if (before > 1f && after > 1f)
                {
                    _spectatorSystem.Zoom(before / after);
                }
            }
            else if (!_tapCandidate || (position - _tapStart).sqrMagnitude > TAP_SLOP * TAP_SLOP)
            {
                _tapCandidate = false;
                PanBy(position - previous);
            }

            _pointerPositions[slot] = position;
        }

        private void OnFieldPointerUp(PointerUpEvent evt)
        {
            if (IsHumanPointer)
            {
                // A lifted finger is not hovering over anybody; a mouse still is.
                if (evt.pointerType == UnityEngine.UIElements.PointerType.touch)
                {
                    _hasHumanPointer = false;
                    _humanControl.ClearPointer();
                }

                return;
            }

            int slot = PointerSlot(evt.pointerId);

            if (slot < 0)
            {
                return;
            }

            bool tapped = _tapCandidate
                && Time.unscaledTime - _tapStartTime <= TAP_SECONDS;

            ReleasePointerSlot(slot, evt.pointerId);

            if (tapped && TryPanelToWorld(evt.position, out Vector2 world))
            {
                _spectatorSystem.TapAt(world);
            }

            _tapCandidate = false;
        }

        private void OnFieldPointerCancel(PointerCancelEvent evt)
        {
            int slot = PointerSlot(evt.pointerId);

            if (slot >= 0)
            {
                ReleasePointerSlot(slot, evt.pointerId);
            }

            _tapCandidate = false;
        }

        private void OnFieldWheel(WheelEvent evt)
        {
            // Wheel towards the viewer opens the shot, away closes it, a fixed step
            // a notch — the delta's size differs by platform and mouse, its sign
            // does not.
            if (Mathf.Approximately(evt.delta.y, 0f))
            {
                return;
            }

            _spectatorSystem.Zoom(1f + (Mathf.Sign(evt.delta.y) * WHEEL_ZOOM_STEP));
        }

        /// <summary>Index of the slot tracking this pointer, or of a free one for NO_POINTER; -1 if none.</summary>
        private int PointerSlot(int pointerId)
        {
            for (int slot = 0; slot < MAX_POINTERS; slot++)
            {
                if (_pointerIds[slot] == pointerId)
                {
                    return slot;
                }
            }

            return -1;
        }

        private int ActivePointerCount()
        {
            int count = 0;

            for (int slot = 0; slot < MAX_POINTERS; slot++)
            {
                if (_pointerIds[slot] != NO_POINTER)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Lets go of every finger the field is tracking, without a tap.
        ///
        /// FOR THE FINAL WHISTLE. Switching the surface to PickingMode.Ignore stops
        /// NEW touches reaching it, but a pointer it has already captured keeps
        /// being delivered to it until it lifts — so a thumb resting on the field
        /// as the game ended went on panning the post-game shot, its release was a
        /// tap that focused a player nobody could then release, and until it
        /// lifted that pointer could not press REMATCH.
        /// </summary>
        private void ReleaseFieldPointers()
        {
            for (int slot = 0; slot < MAX_POINTERS; slot++)
            {
                if (_pointerIds[slot] != NO_POINTER)
                {
                    ReleasePointerSlot(slot, _pointerIds[slot]);
                }
            }

            _tapCandidate = false;
        }

        private void ReleasePointerSlot(int slot, int pointerId)
        {
            _pointerIds[slot] = NO_POINTER;

            if (_fieldSurface.HasPointerCapture(pointerId))
            {
                _fieldSurface.ReleasePointer(pointerId);
            }
        }

        /// <summary>
        /// A drag, in panel units, turned into metres on the field. The picture
        /// follows the finger, so the SHOT moves the other way across and — because
        /// the panel's y runs down and the world's runs up — the same way vertically.
        /// </summary>
        private void PanBy(Vector2 panelDelta)
        {
            Rect bounds = UnsafeRoot.worldBound;

            if (_fieldCamera == null || bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            float worldHeight = _fieldCamera.orthographicSize * 2f;
            float worldWidth = worldHeight * _fieldCamera.aspect;

            _spectatorSystem.Pan(new Vector2(
                -panelDelta.x / bounds.width * worldWidth,
                panelDelta.y / bounds.height * worldHeight));
        }

        /// <summary>
        /// Panel position to a point on the field. Through the viewport rather than
        /// through screen pixels, because the panel scales on width and a panel unit
        /// is not a pixel on any real device.
        /// </summary>
        private bool TryPanelToWorld(Vector2 panelPosition, out Vector2 world)
        {
            Rect bounds = UnsafeRoot.worldBound;

            if (_fieldCamera == null || bounds.width <= 0f || bounds.height <= 0f)
            {
                world = Vector2.zero;
                return false;
            }

            float viewportX = (panelPosition.x - bounds.xMin) / bounds.width;
            float viewportY = 1f - ((panelPosition.y - bounds.yMin) / bounds.height);

            world = _fieldCamera.ViewportToWorldPoint(new Vector3(viewportX, viewportY, 0f));
            return true;
        }

        // --- The focus card -------------------------------------------------------

        /// <summary>
        /// The player the viewer tapped: who he is, what he is doing, how fast and
        /// how tired.
        ///
        /// ONE CARD, FOR THE ONE PLAYER SOMEBODY ASKED ABOUT — the same reasoning
        /// as the carrier chip, which answers "who has the ball" without being
        /// asked. Twenty-two bars over twenty-two ten-pixel shapes would bury them.
        ///
        /// THE BADGE IS THE DECISION, NOT A GUESS AT IT. Whether he is braking comes
        /// from the drive command his policy last emitted (Systems_IIntentSource),
        /// which is the same tap the intent overlay draws from; the rest is his
        /// rigidbody. There is no balance gauge because there is no balance: a
        /// player here is one rigid shape, and a bar for a quantity the simulation
        /// does not have would be decoration presented as telemetry.
        ///
        /// Tapping the card lets him go, as tapping him again does.
        /// </summary>
        private VisualElement BuildFocusCard()
        {
            VisualElement strip = Systems_UiTheme.Row();
            strip.style.position = Position.Absolute;
            strip.style.left = 0;
            strip.style.right = 0;
            strip.style.bottom = Systems_HudLayout.CARD_DOCK_BOTTOM;
            strip.style.justifyContent = Justify.Center;
            strip.pickingMode = PickingMode.Ignore;

            VisualElement card = Systems_UiTheme.Row();
            card.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            Systems_UiTheme.SetPadding(card, Systems_UiTheme.SPACE_S, Systems_UiTheme.SPACE_M);
            Systems_UiTheme.SetRadius(card, Systems_UiTheme.RADIUS);
            Systems_UiTheme.ApplyElevation(card);
            card.RegisterCallback<ClickEvent>(OnFocusCardClicked);

            _focusRole = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_BODY,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);
            _focusRole.pickingMode = PickingMode.Ignore;

            _focusState = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);
            _focusState.style.marginLeft = Systems_UiTheme.SPACE_M;
            _focusState.style.minWidth = 96;
            _focusState.style.unityTextAlign = TextAnchor.MiddleCenter;
            _focusState.style.backgroundColor = new Color(
                Systems_UiTheme.TextPrimary.r, Systems_UiTheme.TextPrimary.g,
                Systems_UiTheme.TextPrimary.b, 0.12f);
            _focusState.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetPadding(_focusState, Systems_UiTheme.SPACE_XS, Systems_UiTheme.SPACE_S);
            Systems_UiTheme.SetRadius(_focusState, Systems_UiTheme.RADIUS / 2);

            _focusSpeed = Systems_UiTheme.Text(
                MphLabels[0], Systems_UiTheme.TEXT_BODY, Systems_UiTheme.TextPrimary);
            _focusSpeed.style.marginLeft = Systems_UiTheme.SPACE_M;
            _focusSpeed.pickingMode = PickingMode.Ignore;

            VisualElement track = BuildFatigueGauge(out _focusFatigueFill);

            // A cross, where "TAP TO RELEASE" used to be: fourteen letters of
            // instruction on a card whose other four items are one word or less,
            // and the widest thing on it. The whole card is still the target.
            Systems_UiIcon release = Systems_UiIcon.Create(
                Systems_UiIcon.Glyph.Close, DISMISS_ICON_SIZE, Systems_UiTheme.TextMuted);
            release.style.marginLeft = Systems_UiTheme.SPACE_M;

            card.Add(_focusRole);
            card.Add(_focusState);
            card.Add(_focusSpeed);
            card.Add(track);
            card.Add(release);
            strip.Add(card);

            strip.style.display = DisplayStyle.None;
            _focusCard = strip;
            return strip;
        }

        private void OnFocusCardClicked(ClickEvent evt)
        {
            _spectatorSystem?.ReleaseFocus();
        }

        private void RefreshFocus()
        {
            if (_focusCard == null || _spectator == null)
            {
                return;
            }

            Systems_IPlayerHandle focus = _spectator.HasFocus && !_gameOver
                ? _registry.FindById(_spectator.FocusedPlayerId)
                : null;

            if (focus == null)
            {
                if (_lastFocusId != Systems_SpectatorModel.NO_FOCUS)
                {
                    _lastFocusId = Systems_SpectatorModel.NO_FOCUS;
                    _focusCard.style.display = DisplayStyle.None;
                }

                return;
            }

            if (focus.Id != _lastFocusId)
            {
                _lastFocusId = focus.Id;
                _focusRole.text = Systems_DisplayText.RoleTag(focus.Role);
                _focusCard.style.display = DisplayStyle.Flex;
            }

            // Every frame, not only on a new focus: possession can change under a
            // player who stays focused, and his team colour is derived from it.
            Systems_TeamId team = focus.Side == Systems_TeamSide.Offense
                ? _game.Possession
                : _game.Possession.Opponent();

            _focusRole.style.color = Systems_UiTheme.ColorOf(team);

            float speed = focus.Velocity.magnitude;

            int mph = Mathf.Clamp(Mathf.RoundToInt(speed * MPS_TO_MPH), 0, MAX_DISPLAY_MPH);

            if (mph != _lastFocusMph)
            {
                _lastFocusMph = mph;
                _focusSpeed.text = MphLabels[mph];
            }

            int fatiguePercent = Mathf.RoundToInt(Mathf.Clamp01(focus.Fatigue) * 100f);

            if (fatiguePercent != _lastFocusFatiguePercent)
            {
                _lastFocusFatiguePercent = fatiguePercent;
                _focusFatigueFill.style.width = Length.Percent(fatiguePercent);
            }

            FocusState state = FocusStateOf(focus, speed);

            if (state != _lastFocusState)
            {
                _lastFocusState = state;
                _focusState.text = FocusStateLabels[(int)state];
            }
        }

        private FocusState FocusStateOf(Systems_IPlayerHandle player, float speed)
        {
            bool live = _play != null && _play.Phase == Systems_PlayPhase.Live;

            Systems_PlayerIntent intent = _intents != null && _intents.Available
                ? _intents.Read(player.Id)
                : default;

            // A throw is armed for a decision or two before the ball leaves, and
            // that is the moment worth a word of its own.
            if (live && player.IsCarrier && intent.HasAim && intent.ThrowArmed)
            {
                return FocusState.Throw;
            }

            if (player.IsCarrier)
            {
                return FocusState.Ball;
            }

            if (speed < MOVING_SPEED)
            {
                return FocusState.Set;
            }

            if (live && intent.Drive <= BRAKE_DRIVE)
            {
                return FocusState.Brake;
            }

            return speed >= SPRINT_SPEED ? FocusState.Sprint : FocusState.Run;
        }

        /// <summary>
        /// A track and a fill rather than a number: "how tired" is a gauge, and a
        /// percentage beside a speed reads as a second speed. Shared by the carrier
        /// chip and the focus card.
        /// </summary>
        private static VisualElement BuildFatigueGauge(out VisualElement fill)
        {
            VisualElement track = new VisualElement();
            track.style.width = 96;
            track.style.height = Systems_UiTheme.SPACE_S;
            track.style.marginLeft = Systems_UiTheme.SPACE_M;
            track.style.backgroundColor = new Color(
                Systems_UiTheme.TextMuted.r, Systems_UiTheme.TextMuted.g,
                Systems_UiTheme.TextMuted.b, 0.3f);
            track.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(track, Systems_UiTheme.SPACE_S / 2);

            fill = new VisualElement();
            fill.style.height = Length.Percent(100f);
            fill.style.width = Length.Percent(0f);
            fill.style.backgroundColor = Systems_UiTheme.Negative;
            fill.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(fill, Systems_UiTheme.SPACE_S / 2);
            track.Add(fill);

            return track;
        }

        /// <summary>
        /// The player holding the ball while the play is live, or null — the same
        /// test as Systems_BroadcastCameraView, from Systems_PlayerRegistry.LiveCarrier.
        /// </summary>
        private Systems_IPlayerHandle LiveCarrier()
        {
            return _registry == null ? null : _registry.LiveCarrier(_play, _ball);
        }

        private void RefreshCarrier()
        {
            if (_carrierChip == null)
            {
                return;
            }

            Systems_IPlayerHandle carrier =
                _gameOver || _finalOverlay.IsVisible ? null : LiveCarrier();

            if (carrier == null)
            {
                if (_lastCarrierId != -1)
                {
                    _lastCarrierId = -1;
                    _carrierChip.style.visibility = Visibility.Hidden;
                }

                return;
            }

            if (carrier.Id != _lastCarrierId)
            {
                _lastCarrierId = carrier.Id;
                _carrierRole.text = Systems_DisplayText.RoleTag(carrier.Role);

                // After a turnover the man with the ball is on the team that was
                // defending; his tag takes his own team's colour, as on the field.
                Systems_TeamId team = carrier.Side == Systems_TeamSide.Offense
                    ? _game.Possession
                    : _game.Possession.Opponent();

                _carrierRole.style.color = Systems_UiTheme.ColorOf(team);
                _carrierChip.style.visibility = Visibility.Visible;
            }

            int mph = Mathf.Clamp(
                Mathf.RoundToInt(carrier.Velocity.magnitude * MPS_TO_MPH), 0, MAX_DISPLAY_MPH);

            if (mph != _lastMph)
            {
                _lastMph = mph;
                _carrierSpeed.text = MphLabels[mph];
            }

            int fatiguePercent = Mathf.RoundToInt(Mathf.Clamp01(carrier.Fatigue) * 100f);

            if (fatiguePercent != _lastFatiguePercent)
            {
                _lastFatiguePercent = fatiguePercent;
                _carrierFatigueFill.style.width = Length.Percent(fatiguePercent);
            }
        }

        private static string[] BuildMphLabels()
        {
            string[] labels = new string[MAX_DISPLAY_MPH + 1];

            for (int mph = 0; mph <= MAX_DISPLAY_MPH; mph++)
            {
                labels[mph] = mph + " MPH";
            }

            return labels;
        }

        // --- Overlays ----------------------------------------------------------

        private Systems_UiOverlay BuildBanner()
        {
            // No scrim and no input blocking: an announcement must never dim the
            // play it is announcing, nor eat a tap meant for the field.
            Systems_UiOverlay overlay = new Systems_UiOverlay(
                "ResultBanner", blocksInput: false, scrim: Color.clear);

            overlay.Content.style.alignItems = Align.Center;

            // DIRECTLY UNDER THE SCOREBOARD, AND SAID SO IN UNITS. This was
            // paddingTop: 30%, described as "a third of the way down whatever screen
            // this is". It never was: a percentage padding resolves against the
            // WIDTH of the containing block, on every axis, and this panel is 1080
            // wide on every device — so it was a fixed 324 units that only looked
            // like a proportion. The banner belongs just below the bar on every
            // screen, clear of the middle of the field where the ball usually is,
            // and that is a distance, not a ratio.
            overlay.Content.style.paddingTop = Systems_HudLayout.BANNER_TOP;

            _bannerHeadline = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_BANNER,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);

            _bannerDetail = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_BODY, Systems_UiTheme.TextMuted);

            // The drive so far — the broadcast's "7 plays, 54 yards" — under the
            // play that just ended. It is what makes a punt read as the end of a
            // stalled drive rather than an isolated event.
            _bannerDrive = Systems_UiTheme.Caption(string.Empty);
            _bannerDrive.style.marginTop = Systems_UiTheme.SPACE_XS;

            // ON A PLATE (2026-10-16 review). The three lines sat straight on the
            // turf, and the two under the headline are muted grey: on the light
            // mow stripe "FB DIVE +3 yd" and the drive line were close to
            // invisible. A dark, rounded plate sized to the text keeps the banner
            // an announcement over the play rather than paint on it, and still
            // covers only the band it needs.
            VisualElement plate = Systems_UiTheme.Column();
            plate.style.alignItems = Align.Center;
            plate.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            Systems_UiTheme.SetRadius(plate, Systems_UiTheme.RADIUS);
            Systems_UiTheme.SetPadding(plate, Systems_UiTheme.SPACE_S, Systems_UiTheme.SPACE_L);
            plate.pickingMode = PickingMode.Ignore;

            plate.Add(_bannerHeadline);
            plate.Add(_bannerDetail);
            plate.Add(_bannerDrive);
            overlay.Content.Add(plate);
            return overlay;
        }

        /// <summary>
        /// The result, the box score and every way on from it, as ONE sheet docked
        /// to the bottom of the screen.
        ///
        /// IT USED TO BE A CENTRED COLUMN UNDER A 92% SCRIM: a headline, the card,
        /// then REMATCH, REPLAY THIS GAME and MENU stacked one per row — 336 units
        /// of buttons, the primary one in the middle of the screen where a thumb
        /// holding a phone does not reach, and the field blacked out behind all of
        /// it. Docked, the three actions are one row along the bottom edge, the
        /// sheet ends where the status footer begins, and everything above it is
        /// the final frame of the game under the scoreboard — dimmed, not hidden.
        ///
        /// NOTHING SCROLLS AND NOTHING NEEDS TO. The sheet measures about 830 panel
        /// units with its tab row, and the panel is never shorter than 1920 (it
        /// scales on width, and 9:16 is the squarest portrait handset there is), so
        /// its top edge is at about 1020 at the lowest — well clear of the
        /// scoreboard. Systems_AspectSweepTests holds that on five ratios.
        ///
        /// THREE TABS, ONE SHEET: BOX SCORE, SCORING, HIGHLIGHTS. The result, when
        /// it was scored and the replay of it are three views of the same game,
        /// and they were three places — this card, nowhere at all, and a WATCH
        /// HIGHLIGHTS chip floating on another document between this sheet and the
        /// status footer, which the sheet had to be lifted to clear. HIGHLIGHTS is
        /// a tab that hands the screen to the reel rather than a pane, because the
        /// highlight plays on the field and the field is what this sheet covers.
        /// </summary>
        private Systems_UiOverlay BuildFinalOverlay()
        {
            // SurfaceOverField, not SurfaceScrim: the sheet is opaque, so the scrim
            // no longer has to make digits legible over players — only to say the
            // field behind it is no longer live.
            Systems_UiOverlay overlay = new Systems_UiOverlay(
                "FinalScore", blocksInput: true, scrim: Systems_UiTheme.SurfaceOverField);

            overlay.Content.style.justifyContent = Justify.FlexEnd;

            // Clear of the status footer, and of nothing else: the highlights chip
            // that used to dock between the two is the sheet's third tab now.
            overlay.Content.style.paddingBottom = Systems_UiTheme.STATUS_FOOTER_HEIGHT;

            VisualElement sheet = Systems_UiTheme.BottomSheet();
            sheet.name = "FinalSheet";
            Systems_UiTheme.SetPadding(sheet, Systems_UiTheme.SPACE_M, Systems_UiTheme.SPACE_L);

            _finalHeadline = Systems_UiTheme.Text(
                "FINAL", Systems_UiTheme.TEXT_BANNER,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);

            _finalHeadline.style.unityTextAlign = TextAnchor.MiddleCenter;

            // The whole box score, on the screen that announces the result. It used
            // to be two lines of totals here and the full table on the menu, a tap
            // and a scene load away — and not at all for anyone who chose REMATCH.
            _finalCardHost = new VisualElement();
            _finalCardHost.style.marginBottom = Systems_UiTheme.SPACE_M;
            _finalCardHost.pickingMode = PickingMode.Ignore;

            VisualElement tabs = Systems_UiTheme.Row();
            tabs.name = "FinalTabs";
            tabs.style.marginTop = Systems_UiTheme.SPACE_S;
            tabs.style.marginBottom = Systems_UiTheme.SPACE_S;

            _boxScoreTab = FinalTab("BOX SCORE", () => SelectFinalPane(false));
            _scoringTab = FinalTab("SCORING", () => SelectFinalPane(true));
            _highlightsTab = FinalTab("HIGHLIGHTS", OnHighlightsTab);
            _scoringTab.style.marginLeft = Systems_UiTheme.SPACE_S;
            _highlightsTab.style.marginLeft = Systems_UiTheme.SPACE_S;

            // Off until the reel reports in. A tab that opens nothing is worse
            // than no tab, and on a budget that builds no replay it never will.
            _highlightsTab.style.display = DisplayStyle.None;

            tabs.Add(_boxScoreTab);
            tabs.Add(_scoringTab);
            tabs.Add(_highlightsTab);

            // REMATCH reloads SCN_GAME rather than resetting the models in place.
            // Everything a second game needs to forget — the scoreboard, the box
            // score, the episode director that stopped itself at the final whistle,
            // twenty-two rigidbodies parked where the last tackle left them — is
            // scene state, and rebuilding the scene clears all of it through the
            // same path PLAY already uses. Resetting each piece by hand would be a
            // second, less-travelled way to reach the same state.
            // The QB challenge plays the QB challenge again.
            Button rematchButton = Systems_UiTheme.Button(
                "REMATCH", Systems_UiTheme.Action,
                () => Systems_SceneRouter.LoadGame(_variant));
            ApplyDockActionSize(rematchButton, PRIMARY_DOCK_WEIGHT);

            // The same destination as the status HUD's MENU chip above it, and now
            // the same result: the summary is offered to the router at the whistle
            // (see OnGameOver), so neither button has to remember to carry it.
            Button menuButton = Systems_UiTheme.Button(
                "MENU", Systems_UiTheme.SurfaceRaised, Systems_SceneRouter.LoadMenu);
            menuButton.style.color = Systems_UiTheme.TextPrimary;
            ApplyDockActionSize(menuButton, 1f);
            menuButton.style.fontSize = Systems_UiTheme.TEXT_BODY;

            // THE SAME GAME AGAIN, NOT ANOTHER ONE. Everything downstream of the seed
            // is deterministic, so the seed this game was played on is the whole
            // game — REMATCH draws a new one, this hands the old one back. The
            // number is shown so it can be written down or sent to someone: typed
            // into the menu's GAME SEED sheet it plays this game on their phone.
            uint seed = Systems_EpisodeSeed.Value;

            // THE SEED IS THE BUTTON'S OWN SECOND LINE. It was a caption at the
            // bottom of the column, three rows from the button it explained.
            Button replayButton = Systems_UiTheme.Button(
                "SAME GAME\nSEED " + seed,
                Systems_UiTheme.SurfaceOverField,
                () => Systems_SceneRouter.ReplayGame(seed, _variant));
            replayButton.name = "ReplaySeed";
            replayButton.style.color = Systems_UiTheme.TextPrimary;
            ApplyDockActionSize(replayButton, 1f);
            replayButton.style.fontSize = Systems_UiTheme.TEXT_CAPTION;
            replayButton.style.whiteSpace = WhiteSpace.Normal;

            // The two quieter actions sit on the field-chrome tint so they read as
            // buttons against the sheet, which is itself SurfaceRaised.
            menuButton.style.backgroundColor = Systems_UiTheme.SurfaceOverField;

            // REMATCH FIRST AND WIDEST, MENU LAST. The primary action is under the
            // thumb of whichever hand is holding the phone because it spans the
            // middle; MENU is in the corner under the status HUD's MENU chip, the
            // same side of the screen for the same destination.
            VisualElement actions = Systems_UiTheme.Row();
            actions.name = "FinalActions";
            actions.Add(rematchButton);
            actions.Add(replayButton);
            actions.Add(menuButton);

            sheet.Add(_finalHeadline);
            sheet.Add(tabs);
            sheet.Add(_finalCardHost);
            sheet.Add(actions);

            overlay.Content.Add(sheet);
            return overlay;
        }

        private static Button FinalTab(string label, Action onClick)
        {
            Button tab = Systems_UiTheme.Tab(label, onClick);
            tab.style.minHeight = FINAL_TAB_HEIGHT;
            tab.style.height = FINAL_TAB_HEIGHT;
            tab.style.fontSize = Systems_UiTheme.TEXT_CAPTION;
            return tab;
        }

        /// <summary>
        /// VISIBILITY, NOT DISPLAY, unlike the settings sheet's panes: the box
        /// score is the pane that gives the host its height, so it has to stay in
        /// layout while the scoring card is drawn over it.
        /// </summary>
        private void SelectFinalPane(bool scoring)
        {
            Systems_UiTheme.PaintTab(_boxScoreTab, !scoring);
            Systems_UiTheme.PaintTab(_scoringTab, scoring);
            Systems_UiTheme.PaintTab(_highlightsTab, false);

            if (_boxScorePane != null)
            {
                _boxScorePane.style.visibility = scoring ? Visibility.Hidden : Visibility.Visible;
            }

            if (_scoringPane != null)
            {
                _scoringPane.style.visibility = scoring ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void OnHighlightsTab()
        {
            _openHighlightsPublisher?.Publish(new Systems_OpenHighlightsMessage());
        }

        /// <summary>
        /// The reel has chosen its highlights. Either order against the whistle is
        /// fine: the count is kept, and the tab follows it whenever both are in.
        /// </summary>
        private void OnHighlightsReady(Systems_HighlightsReadyMessage message)
        {
            _highlightCount = message.Count;

            if (_highlightsTab != null)
            {
                _highlightsTab.style.display =
                    _highlightCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// One of the actions on the final sheet's single row: a full tap target
        /// tall, sharing the width by weight rather than each claiming 78% of it.
        /// Zero basis, so the split is the weights' and not the labels'.
        /// </summary>
        private static void ApplyDockActionSize(Button button, float weight)
        {
            button.style.flexGrow = weight;
            button.style.flexBasis = 0f;
            button.style.minHeight = Systems_UiTheme.TAP_TARGET;
            button.style.marginLeft = Systems_UiTheme.SPACE_XS;
            button.style.marginRight = Systems_UiTheme.SPACE_XS;
        }


        // --- Per-frame ----------------------------------------------------------

        /// <summary>
        /// The base class re-applies the safe area here. Everything below is
        /// guarded on a changed value, so the common frame writes nothing —
        /// including, now, the result banner, which used to run a countdown on
        /// every frame of every game to animate a two-second fade.
        /// </summary>
        protected override void Update()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            base.Update();

            if (!IsBuilt || _game == null)
            {
                return;
            }

            RefreshClock();
            RefreshScoreboard(false);
            RefreshCall();
            RefreshCarrier();
            RefreshFocus();
            RefreshTransport();
            RefreshHumanPointer();
        }

        /// <summary>
        /// The QB challenge: re-reads the pointer's point on the field against this
        /// frame's camera, and refreshes the play sheet.
        /// </summary>
        private void RefreshHumanPointer()
        {
            if (_playSheet == null)
            {
                return;
            }

            if (IsHumanPointer && _hasHumanPointer
                && TryPanelToWorld(_humanPointerPanel, out Vector2 world))
            {
                _humanControl.SetPointer(world);
            }

            _playSheet.Refresh();
            RefreshPointerMarks();
        }

        /// <summary>
        /// The dot marks where the human sent things: the spot his pass will come
        /// down on while it is in the air, else the spot his carrier is running
        /// to. The ring sits under whoever a tap would hand or pitch the ball to.
        /// </summary>
        private void RefreshPointerMarks()
        {
            bool playing = IsHumanPointer && _humanControl.HumanOnOffense
                && _play.Phase == Systems_PlayPhase.Live;

            bool hasMark = false;
            Vector2 markWorld = Vector2.zero;

            if (playing && _ball != null && _ball.HasLandingSpot)
            {
                hasMark = true;
                markWorld = _ball.LandingSpot;
            }
            else if (playing && _humanControl.HasMoveTarget)
            {
                hasMark = true;
                markWorld = _humanControl.MoveTarget;
            }

            Vector2 markPanel = Vector2.zero;
            float markUnits = 0f;
            bool showDot = hasMark && TryWorldToPanel(markWorld, out markPanel, out markUnits);
            _pointerDot.style.display = showDot ? DisplayStyle.Flex : DisplayStyle.None;

            if (showDot)
            {
                Vector2 local = _pointerDot.parent.WorldToLocal(markPanel);
                _pointerDot.style.left = local.x - (POINTER_DOT_UNITS / 2f);
                _pointerDot.style.top = local.y - (POINTER_DOT_UNITS / 2f);
            }

            int hovered = playing ? _humanControl.HoveredSlot : -1;
            Systems_IPlayerHandle mate = hovered >= 0 && _registry != null ? _registry.Get(hovered) : null;
            Vector2 centre = Vector2.zero;
            float unitsPerMetre = 0f;
            bool showRing = mate != null && mate.Role != Systems_PlayerRole.OffensiveLine
                && TryWorldToPanel(mate.Position, out centre, out unitsPerMetre);

            _hoverRing.style.display = showRing ? DisplayStyle.Flex : DisplayStyle.None;

            if (showRing)
            {
                Vector2 local = _hoverRing.parent.WorldToLocal(centre);
                float diameter = HOVER_RING_METRES * unitsPerMetre;
                _hoverRing.style.width = diameter;
                _hoverRing.style.height = diameter;
                _hoverRing.style.left = local.x - (diameter / 2f);
                _hoverRing.style.top = local.y - (diameter / 2f);
                Systems_UiTheme.SetRadius(_hoverRing, Mathf.RoundToInt(diameter / 2f));
            }
        }

        /// <summary>
        /// A point on the field to the panel, and how many panel units a metre is
        /// there — TryPanelToWorld run backwards, through the same viewport.
        /// </summary>
        private bool TryWorldToPanel(Vector2 world, out Vector2 panel, out float unitsPerMetre)
        {
            Rect bounds = UnsafeRoot.worldBound;
            panel = Vector2.zero;
            unitsPerMetre = 0f;

            if (_fieldCamera == null || bounds.width <= 0f || bounds.height <= 0f)
            {
                return false;
            }

            Vector3 viewport = _fieldCamera.WorldToViewportPoint(new Vector3(world.x, world.y, 0f));
            panel = new Vector2(
                bounds.xMin + (viewport.x * bounds.width),
                bounds.yMin + ((1f - viewport.y) * bounds.height));
            unitsPerMetre = bounds.height / (_fieldCamera.orthographicSize * 2f);
            return true;
        }

        /// <summary>
        /// The pointer left the field. The player keeps the command he last had
        /// (Systems_HumanControlModel.ClearPointer) rather than chasing the spot
        /// where the pointer was last seen.
        /// </summary>
        private void OnFieldPointerLeave(PointerLeaveEvent evt)
        {
            if (!IsHumanPointer)
            {
                return;
            }

            _hasHumanPointer = false;
            _humanControl.ClearPointer();
        }

        /// <summary>A press or a move on the field, as the human quarterback's pointer.</summary>
        private void HumanPointer(Vector2 panelPosition, bool pressed)
        {
            _humanPointerPanel = panelPosition;
            _hasHumanPointer = true;

            if (!TryPanelToWorld(panelPosition, out Vector2 world))
            {
                return;
            }

            if (pressed)
            {
                // A finger's clock, not the simulation's: the double-tap window
                // must not shrink at 8x (Systems_HumanControlModel.Tap).
                _humanControl.Tap(world, Time.unscaledTime);
            }
            else
            {
                _humanControl.SetPointer(world);
            }
        }

        private void RefreshClock()
        {
            int key = Systems_DisplayText.ClockKey(_game.SecondsRemaining);

            if (key == _lastClockKey)
            {
                return;
            }

            _lastClockKey = key;
            _clock.text = Systems_DisplayText.Clock(_game.SecondsRemaining);

            // A stopped clock is dimmed rather than hidden — a viewer needs to be
            // able to tell "stopped" from "the game is over".
            //
            // NO TWO-MINUTE COLOUR, DELIBERATELY. An amber clock inside two minutes
            // was written here and taken out again: amber is Systems_UiTheme.Accent,
            // which that class reserves for the chains "so it always means the
            // line", and the palette note is explicit that every other hue is
            // already spoken for by the teams or by good and bad news. There is no
            // free colour for urgency, and quietly spending the chains' one to
            // invent a signal nobody asked for is how the accent came to mean four
            // things the last time.
            _clock.style.color = _game.IsClockRunning
                ? Systems_UiTheme.TextPrimary
                : Systems_UiTheme.TextMuted;
        }

        /// <summary>
        /// Updates the parts of the scoreboard that change on a play boundary.
        /// Guarded on the values themselves so the common frame does no work at all.
        /// </summary>
        private void RefreshScoreboard(bool force)
        {
            // Pulsed only when the value actually moved, never on the forced build.
            // A HUD that animated everything on the opening frame would spend the
            // kickoff drawing attention to numbers nobody has read yet.
            if (force || _game.HomeScore != _lastHomeScore)
            {
                _lastHomeScore = _game.HomeScore;
                _homeScore.text = _lastHomeScore.ToString();

                if (!force)
                {
                    Systems_UiTheme.Pulse(_homeScore);
                }
            }

            if (force || _game.AwayScore != _lastAwayScore)
            {
                _lastAwayScore = _game.AwayScore;
                _awayScore.text = _lastAwayScore.ToString();

                if (!force)
                {
                    Systems_UiTheme.Pulse(_awayScore);
                }
            }

            // OVERTIME IS A PHASE, NOT A FIFTH QUARTER. Systems_GameModel.BeginOvertime
            // resets the clock and sets the phase and leaves Quarter at four, so
            // keying this label on Quarter alone showed "4TH" beside a fresh clock
            // for the whole of sudden death — and Systems_DisplayText's "OT" branch
            // had never once been reached. Seen on a 38-45 game that ended with
            // "4TH 2:28" on the board.
            _sawOvertime |= _game.Phase == Systems_GamePhase.Overtime;

            int period = _sawOvertime
                ? Systems_GameRules.QUARTER_COUNT + 1
                : _game.Quarter;

            if (force || period != _lastQuarter)
            {
                _lastQuarter = period;
                _quarter.text = Systems_DisplayText.QuarterLabel(period);
            }

            if (force || _game.HomeTimeouts != _lastHomeTimeouts)
            {
                _lastHomeTimeouts = _game.HomeTimeouts;
                RefreshTimeoutPips(_homeTimeoutPips, _lastHomeTimeouts);
            }

            if (force || _game.AwayTimeouts != _lastAwayTimeouts)
            {
                _lastAwayTimeouts = _game.AwayTimeouts;
                RefreshTimeoutPips(_awayTimeoutPips, _lastAwayTimeouts);
            }

            if (force || _game.Down != _lastDown)
            {
                _lastDown = _game.Down;
                RefreshSituation();

                if (!force && _situationPill != null)
                {
                    Systems_UiTheme.Pulse(_situationPill);
                }
            }
        }

        private void RefreshSituation()
        {
            _downAndDistance.text = Systems_DisplayText.Situation(
                _game.NextPlayKind, _game.Down, _game.YardsToGo, _game.IsGoalToGo);

            _fieldPosition.text = Systems_DisplayText.FieldPosition(_game.LineOfScrimmageY);

            bool homeHasBall = _game.Possession == Systems_TeamId.Home;
            _homePossession.style.visibility =
                homeHasBall ? Visibility.Visible : Visibility.Hidden;
            _awayPossession.style.visibility =
                homeHasBall ? Visibility.Hidden : Visibility.Visible;
        }

        // --- Events --------------------------------------------------------------

        /// <summary>
        /// The ball is live. Take the last play's banner down — it is describing
        /// a down that is over, on top of the one that has started.
        /// </summary>
        private void OnSnapped(Systems_PlaySnappedMessage message)
        {
            if (!IsBuilt)
            {
                return;
            }

            _bannerOverlay.Hide();

            _snapDriveIndex = _game.DriveIndex;
        }

        private void OnDownResolved(Systems_DownResolvedMessage message)
        {
            if (!IsBuilt)
            {
                return;
            }

            RecordScoring(message);

            // A KICKOFF AND A TRY BELONG TO NO DRIVE. Neither advances
            // Systems_GameModel.DriveIndex, so both were added to the drive of
            // whoever had the ball last: the opening kickoff read "DRIVE 1 play,
            // 0 yd", and every extra point and the kickoff after it padded the
            // scoring drive. A punt or a field goal is the end of the drive it
            // finished, which is how a broadcast counts it.
            if (IsPartOfDrive(message.Kind))
            {
                if (_snapDriveIndex != _countedDriveIndex)
                {
                    _countedDriveIndex = _snapDriveIndex;
                    _drivePlays = 0;
                    _driveYards = 0f;
                }

                _drivePlays++;
                _driveYards += message.YardsGained;
                _bannerDrive.text = Systems_DisplayText.DriveSummary(_drivePlays, _driveYards);
                _bannerDrive.style.display = DisplayStyle.Flex;
            }
            else
            {
                _bannerDrive.style.display = DisplayStyle.None;
            }

            _bannerHeadline.text =
                Systems_DisplayText.ResultBanner(message.Result, message.Outcome);

            _bannerHeadline.style.color = BannerColorFor(message);

            _bannerDetail.text =
                Systems_DisplayText.PlayDetail(
                    message.Kind, message.Call, message.Outcome,
                    message.YardsGained, message.ReturnYards)
                + Systems_DisplayText.FoulDetail(
                    message.Foul, message.FoulTeam, message.PenaltyAccepted, message.PenaltyYards)
                + Systems_DisplayText.TimeoutDetail(message.TimeoutCalled, message.TimeoutTeam)
                + Systems_DisplayText.TwoMinuteWarningDetail(message.TwoMinuteWarning);

            // UP UNTIL THE NEXT SNAP, NOT FOR A FIXED TIME. It was 2.2 s of real time
            // on the panel's scheduler while the dead ball it describes is
            // DEAD_BALL_TICKS of SIMULATION time: at 4x it was gone in half a
            // second, before a penalty or a two-minute warning on it could be read,
            // and on pause it vanished while the dead ball stood still. OnSnapped
            // takes it down, so it now lasts exactly as long as the dead ball at
            // any speed, and holds while paused.
            _bannerOverlay.Show();

            // Field position and possession can change without the down number
            // changing — a turnover resets to 1st down from 1st down — so refresh
            // the situation line here rather than relying on the change guard.
            RefreshSituation();
        }

        /// <summary>
        /// Keeps the SCORING tab's line score and summary. Points are counted from
        /// the message, not read off the scoreboard, so the order in which the game
        /// model and this handler see the down cannot put a touchdown's points on
        /// the line before it. A try is folded into the touchdown it follows, the
        /// way a broadcast summary prints "(kick good)": seven lines would be four
        /// scores otherwise.
        /// </summary>
        private void RecordScoring(Systems_DownResolvedMessage message)
        {
            bool isTry = message.Kind.IsTry();

            if (message.PointsScored <= 0 && !isTry)
            {
                return;
            }

            if (message.PointsScored > 0)
            {
                bool home = message.ScoringTeam == Systems_TeamId.Home;

                if (home)
                {
                    _homeRunning += message.PointsScored;
                }
                else
                {
                    _awayRunning += message.PointsScored;
                }

                int period = _sawOvertime || _game.Phase == Systems_GamePhase.Overtime
                    ? Systems_ScoringCard.PERIODS - 1
                    : Mathf.Clamp(_game.Quarter, 1, Systems_GameRules.QUARTER_COUNT) - 1;

                if (home)
                {
                    _homeByPeriod[period] += message.PointsScored;
                }
                else
                {
                    _awayByPeriod[period] += message.PointsScored;
                }
            }

            bool foldIntoLast = isTry && message.Result != Systems_DownResult.DefensiveTry
                && _scoringCount > 0;

            if (foldIntoLast)
            {
                int last = _scoringCount - 1;
                _scoring[last].What += TrySuffix(message.Result);
                _scoring[last].HomeScore = _homeRunning;
                _scoring[last].AwayScore = _awayRunning;
                return;
            }

            if (message.PointsScored <= 0 || _scoringCount >= _scoring.Length)
            {
                return;
            }

            int quarter = _sawOvertime ? Systems_GameRules.QUARTER_COUNT + 1 : _game.Quarter;

            _scoring[_scoringCount] = new Systems_ScoringCard.Entry
            {
                When = Systems_DisplayText.QuarterLabel(quarter) + " "
                    + Systems_DisplayText.Clock(_game.SecondsRemaining),
                Team = message.ScoringTeam,
                What = ScoreTag(message) + "  " + Systems_DisplayText.PlayLabel(
                    message.Kind, message.Call, message.Outcome)
                    + "  " + Systems_DisplayText.YardageDetail(message.YardsGained + message.ReturnYards),
                HomeScore = _homeRunning,
                AwayScore = _awayRunning,
            };

            _scoringCount++;
        }

        private static string ScoreTag(Systems_DownResolvedMessage message)
        {
            switch (message.PointsScored)
            {
                case 6: return "TD";
                case 3: return "FG";
                case 2: return message.Kind.IsTry() ? "DEF 2PT" : "SAFETY";
                default: return message.PointsScored + " PTS";
            }
        }

        private static string TrySuffix(Systems_DownResult result)
        {
            switch (result)
            {
                case Systems_DownResult.ExtraPointGood: return " · XP";
                case Systems_DownResult.ExtraPointMissed: return " · XP NO GOOD";
                case Systems_DownResult.TwoPointGood: return " · 2PT";
                case Systems_DownResult.TwoPointFailed: return " · 2PT NO GOOD";
                default: return string.Empty;
            }
        }

        private static Color BannerColorFor(Systems_DownResolvedMessage message)
        {
            switch (message.Result)
            {
                // The scorer's colour, not the offense's: a pick-six or a kick
                // returned all the way is the other team's touchdown.
                case Systems_DownResult.Touchdown:
                    return Systems_UiTheme.ColorOf(message.ScoringTeam);
                case Systems_DownResult.Interception:
                case Systems_DownResult.TurnoverOnDowns:
                case Systems_DownResult.Safety:
                case Systems_DownResult.FumbleLost:
                case Systems_DownResult.BlockedKick:
                case Systems_DownResult.Sack:
                case Systems_DownResult.DefensiveTry:
                    return Systems_UiTheme.Negative;
                case Systems_DownResult.FirstDown:
                case Systems_DownResult.KickRecovered:
                case Systems_DownResult.ExtraPointGood:
                case Systems_DownResult.TwoPointGood:
                    return Systems_UiTheme.Positive;
                default:
                    return Systems_UiTheme.TextPrimary;
            }
        }

        private static bool IsPartOfDrive(Systems_PlayKind kind)
        {
            return kind != Systems_PlayKind.Kickoff && !kind.IsTry();
        }

        private void OnGameOver(Systems_GameOverMessage message)
        {
            _gameOver = true;

            if (!IsBuilt)
            {
                return;
            }

            _finalHeadline.text = message.IsTie
                ? "TIE GAME"
                : $"{Systems_DisplayText.TeamName(message.Winner)} WINS";

            // The QB challenge is scored on the human's own points, against his
            // best: the margin is the computer's business.
            if (_variant == Systems_GameVariant.QbChallenge)
            {
                int points = _game.ScoreOf(Systems_QbChallengeSystem.HUMAN_TEAM);
                int previousBest = Systems_GameSettings.RecordQbChallengeScore(points);

                _finalHeadline.text = points > previousBest
                    ? $"YOU SCORED {points} · NEW BEST"
                    : $"YOU SCORED {points} · BEST {previousBest}";
            }

            // Frozen once, here, and used twice: the card on this overlay, and the
            // menu's LAST GAME card after the scene that owns the box score is gone.
            // Offered to the router now rather than by a button later, so MENU on
            // the overlay and MENU in the status bar lead to the same screen.
            Systems_GameSummary summary = Systems_GameSummary.From(_boxScore);
            Systems_SceneRouter.OfferSummary(summary);

            _finalCardHost.Clear();
            _boxScorePane = Systems_BoxScoreCard.Build("FINAL", summary);
            _finalCardHost.Add(_boxScorePane);

            _scoringPane = Systems_ScoringCard.Build(
                _scoring, _scoringCount, _homeByPeriod, _awayByPeriod, _sawOvertime);
            _scoringPane.name = "ScoringPane";
            Systems_UiTheme.FillParent(_scoringPane);
            _finalCardHost.Add(_scoringPane);

            SelectFinalPane(false);
            OnHighlightsReady(new Systems_HighlightsReadyMessage(_highlightCount));

            _bannerOverlay.Hide();

            // NEITHER THE DOWN NOR THE CALL IS TRUE ANY MORE. Systems_GameFlowSystem
            // resolves the last play like any other, so the chains are left showing
            // whatever the next snap WOULD have been — a finished game sat under a
            // "1st & 10 OPP 46" pill above a 0:00 clock, describing a down that will
            // never be played, beside the call from a play that is over. Hidden
            // rather than removed, so the clock beside them does not move; it
            // stays, because 0:00 is the true and interesting fact about a game
            // that has ended.
            if (_situationPill != null)
            {
                _situationPill.style.visibility = Visibility.Hidden;
            }

            if (_callChip != null)
            {
                _callChip.style.visibility = Visibility.Hidden;
            }

            // The field stops taking taps: there is nobody left to follow, and the
            // post-game screens that come up now must get every touch. The shot
            // goes back to the operator so the highlights are framed as intended.
            if (_fieldSurface != null)
            {
                ReleaseFieldPointers();
                _fieldSurface.pickingMode = PickingMode.Ignore;
            }

            // Nothing is left to pause or speed up, and the result sheet docks
            // where these were. Removed from layout, not dimmed under the scrim:
            // they were still in the focus order there, so a keyboard or a gamepad
            // could pause a finished game from behind the overlay.
            if (_transport != null)
            {
                _transport.style.display = DisplayStyle.None;
            }

            // ...and with the buttons gone, so must whatever they were set to.
            _simSpeed?.ResetToNormal();

            _spectatorSystem?.ReleaseFocus();
            _spectatorSystem?.ResetView();

            _finalOverlay.Show();
        }

        /// <summary>
        /// A post-game highlight is on the field, or has just left it.
        ///
        /// WHY THE OVERLAY GOES ALL THE WAY DOWN. Its scrim is 92% opaque — right
        /// for a result screen, and exactly what made the highlights unwatchable
        /// on the turf underneath it. Hidden, not dimmed: a lighter scrim would
        /// still lay the box score over the play. The scoreboard stays up, so the
        /// final score is on screen throughout, and closing the reel (its RESULT
        /// button, or the last highlight ending) sends IsPlaying = false and puts
        /// the overlay — with REMATCH and MENU — straight back.
        ///
        /// Both directions are refused before the final whistle, so nothing a
        /// message can say will ever raise or drop the final overlay mid-game.
        /// Published every frame while playing; the visibility checks make every
        /// frame but the first a no-op.
        /// </summary>
        private void OnHighlightPlayback(Systems_HighlightPlaybackMessage message)
        {
            if (!IsBuilt || !_gameOver || _finalOverlay == null)
            {
                return;
            }

            if (message.IsPlaying)
            {
                if (_finalOverlay.IsVisible)
                {
                    _finalOverlay.Hide();
                }

                return;
            }

            if (!_finalOverlay.IsVisible)
            {
                _finalOverlay.Show();
            }
        }
    }
}

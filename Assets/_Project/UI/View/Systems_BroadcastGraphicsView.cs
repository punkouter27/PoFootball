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
    /// The broadcast graphics over a played game: the win-probability ribbon under
    /// the scoreboard (with the stadium's weather in its corner), the coordinators'
    /// call sheet, the hit meter, and the play-by-play ticker with its optional
    /// voice and the haptic buzz on a big hit.
    ///
    /// ONE DOCUMENT, FOUR WIDGETS. Each widget is a plain class that builds its
    /// own elements and redraws itself (Systems_WinProbabilityRibbon,
    /// Systems_CallSheetCard, Systems_HitMeter, Systems_CommentaryTicker), the
    /// same composition Systems_ReplayView uses for its reel. This class owns the
    /// document, the subscriptions and the layout — the one place that knows where
    /// every piece sits relative to Systems_HudView, which it never touches.
    ///
    /// BELOW THE HUD, NEVER IN FRONT OF IT. The document sorts at
    /// PANEL_SORT_ORDER, under Systems_HudView's 0, so the result banner and the
    /// final sheet draw over all of this; and nothing here is pickable, so not one
    /// tap meant for the field or a button is spent on a graphic.
    ///
    /// FACTS AS THEY HAPPEN, THE SENTENCE AFTER. A play's commentary is composed
    /// one frame after its resolved message, not in the handler: the win
    /// probability is written by a system subscribed to the same message, and the
    /// order two subscribers run in is not something to rely on. A frame later
    /// both have run and the "huge swing" clause reads the right number.
    ///
    /// Game mode only (it reads Systems_GameModel), and gated on
    /// Systems_PresentationBudget like every other decoration. Each widget also
    /// honours its own switch in Systems_BroadcastSettings, live.
    /// </summary>
    public sealed class Systems_BroadcastGraphicsView : Systems_ScreenView, Systems_IInjectableView
    {
        /// <summary>Under Systems_HudView (0): the banner and the final sheet draw over this.</summary>
        private const float PANEL_SORT_ORDER = -10f;

        private Systems_PresentationBudget _budget;
        private Systems_GameModel _game;
        private Systems_PlayModel _play;
        private Systems_PlayerRegistry _registry;
        private Systems_Roster _roster;
        private Systems_SpectatorModel _spectator;
        private Systems_WinProbabilityModel _winProbability;
        private Systems_ICallSheetSource _callSheet;
        private Systems_WeatherModel _weather;

        private ISubscriber<Systems_PlaySnappedMessage> _snappedSubscriber;
        private ISubscriber<Systems_DownResolvedMessage> _resolvedSubscriber;
        private ISubscriber<Systems_TackleMessage> _tackleSubscriber;
        private ISubscriber<Systems_PassThrownMessage> _thrownSubscriber;
        private ISubscriber<Systems_PassCaughtMessage> _caughtSubscriber;
        private ISubscriber<Systems_ScoreMessage> _scoreSubscriber;
        private ISubscriber<Systems_GameOverMessage> _gameOverSubscriber;

        private IDisposable _subscriptions;

        private Systems_WinProbabilityRibbon _ribbon;
        private Systems_CallSheetCard _callSheetCard;
        private Systems_HitMeter _hitMeter;
        private Systems_CommentaryTicker _ticker;

        private readonly Systems_Haptics _haptics = new Systems_Haptics();
        private readonly Systems_SpeechOutput _speech = new Systems_SpeechOutput();
        private readonly Systems_PlayFacts _facts = new Systems_PlayFacts();

        /// <summary>A resolved play is waiting for its sentence; see the summary.</summary>
        private bool _linePending;

        /// <summary>Set at the final whistle. No play is being called after it.</summary>
        private bool _gameOver;

        private bool _ribbonShown = true;

        [Inject]
        public void Construct(
            Systems_PresentationBudget budget,
            Systems_GameModel game,
            Systems_PlayModel play,
            Systems_PlayerRegistry registry,
            Systems_Roster roster,
            Systems_SpectatorModel spectator,
            Systems_WinProbabilityModel winProbability,
            Systems_ICallSheetSource callSheet,
            Systems_WeatherModel weather,
            ISubscriber<Systems_PlaySnappedMessage> snappedSubscriber,
            ISubscriber<Systems_DownResolvedMessage> resolvedSubscriber,
            ISubscriber<Systems_TackleMessage> tackleSubscriber,
            ISubscriber<Systems_PassThrownMessage> thrownSubscriber,
            ISubscriber<Systems_PassCaughtMessage> caughtSubscriber,
            ISubscriber<Systems_ScoreMessage> scoreSubscriber,
            ISubscriber<Systems_GameOverMessage> gameOverSubscriber)
        {
            _budget = budget;
            _game = game;
            _play = play;
            _registry = registry;
            _roster = roster;
            _spectator = spectator;
            _winProbability = winProbability;
            _callSheet = callSheet;
            _weather = weather;
            _snappedSubscriber = snappedSubscriber;
            _resolvedSubscriber = resolvedSubscriber;
            _tackleSubscriber = tackleSubscriber;
            _thrownSubscriber = thrownSubscriber;
            _caughtSubscriber = caughtSubscriber;
            _scoreSubscriber = scoreSubscriber;
            _gameOverSubscriber = gameOverSubscriber;
        }

        protected override void Awake()
        {
            base.Awake();

            if (TryGetComponent(out UIDocument document))
            {
                document.sortingOrder = PANEL_SORT_ORDER;
            }
        }

        /// <summary>Gated before anything is built: "do not construct", per Systems_PresentationBudget.</summary>
        protected override void Start()
        {
            if (_budget == null || !_budget.EffectsEnabled || _game == null || _play == null)
            {
                enabled = false;
                return;
            }

            base.Start();

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            _snappedSubscriber?.Subscribe(OnSnapped).AddTo(bag);
            _resolvedSubscriber?.Subscribe(OnDownResolved).AddTo(bag);
            _tackleSubscriber?.Subscribe(OnTackle).AddTo(bag);
            _thrownSubscriber?.Subscribe(OnPassThrown).AddTo(bag);
            _caughtSubscriber?.Subscribe(OnPassCaught).AddTo(bag);
            _scoreSubscriber?.Subscribe(OnScore).AddTo(bag);
            _gameOverSubscriber?.Subscribe(OnGameOver).AddTo(bag);
            _subscriptions = bag.Build();
        }

        private void OnDestroy()
        {
            _subscriptions?.Dispose();
            _haptics.Dispose();
            _speech.Dispose();
        }

        protected override void BuildUi()
        {
            VisualElement layer = Systems_UiTheme.Layer();
            Root.Add(layer);

            _ribbon = new Systems_WinProbabilityRibbon(_winProbability);
            VisualElement ribbon = _ribbon.Root;
            ribbon.style.position = Position.Absolute;
            ribbon.style.left = 0;
            ribbon.style.right = 0;
            ribbon.style.top = Systems_HudLayout.RIBBON_TOP;
            layer.Add(ribbon);

            _callSheetCard = new Systems_CallSheetCard();
            VisualElement card = _callSheetCard.Root;
            card.style.position = Position.Absolute;
            card.style.left = Systems_UiTheme.SPACE_M;
            card.style.top = Systems_HudLayout.BANNER_TOP;
            layer.Add(card);

            _hitMeter = new Systems_HitMeter();
            VisualElement meter = _hitMeter.Root;
            meter.style.position = Position.Absolute;
            meter.style.right = Systems_UiTheme.SPACE_M;
            meter.style.top = Systems_HudLayout.HIT_METER_TOP;
            layer.Add(meter);

            _ticker = new Systems_CommentaryTicker();
            VisualElement ticker = _ticker.Root;
            ticker.style.position = Position.Absolute;
            ticker.style.left = Systems_UiTheme.SPACE_M;
            ticker.style.right = Systems_UiTheme.SPACE_M;
            ticker.style.bottom = Systems_HudLayout.CARD_DOCK_BOTTOM;
            layer.Add(ticker);
        }

        protected override void Update()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            base.Update();

            if (!IsBuilt)
            {
                return;
            }

            float now = Time.time;

            if (_linePending)
            {
                _linePending = false;
                PublishLine();
            }

            bool ribbonOn = Systems_BroadcastSettings.WinProbability;

            if (ribbonOn != _ribbonShown)
            {
                _ribbonShown = ribbonOn;
                _ribbon.Root.style.display = ribbonOn ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (ribbonOn)
            {
                _ribbon.Refresh(_weather);
            }

            if (_gameOver)
            {
                _callSheetCard.Hide();
            }
            else
            {
                _callSheetCard.Refresh(_callSheet, _play, now);
            }

            _hitMeter.Refresh(now);
            _ticker.Refresh(_spectator != null && _spectator.HasFocus);
        }

        // --- Facts, as the play happens ------------------------------------------

        private void OnSnapped(Systems_PlaySnappedMessage message)
        {
            _facts.Clear();
            _facts.PlayNumber = message.EpisodeIndex;
            _facts.Offense = _game.Possession;
            _facts.Kind = _play.Kind;
            _facts.Down = _play.Down;
            _facts.YardsToGo = _play.YardsToGo;
            _facts.LineY = message.LineOfScrimmageY;
        }

        private void OnPassThrown(Systems_PassThrownMessage message)
        {
            _facts.Thrown = true;

            if (TryRole(message.TargetId, out Systems_PlayerRole role))
            {
                _facts.HasTarget = true;
                _facts.TargetRole = role;
            }
        }

        private void OnPassCaught(Systems_PassCaughtMessage message)
        {
            _facts.Intercepted = message.Intercepted;
            _facts.Caught = !message.Intercepted;
            _facts.AirYards = message.AirYards;

            if (TryRole(message.CatcherId, out Systems_PlayerRole role))
            {
                _facts.CatcherRole = role;
                _facts.HasCarrier = true;
                _facts.CarrierRole = role;
            }
        }

        private void OnScore(Systems_ScoreMessage message)
        {
            if (TryRole(message.CarrierId, out Systems_PlayerRole role))
            {
                _facts.HasCarrier = true;
                _facts.CarrierRole = role;
            }
        }

        private void OnTackle(Systems_TackleMessage message)
        {
            bool hasTackler = TryRole(message.TacklerId, out Systems_PlayerRole tacklerRole);
            bool hasCarrier = TryRole(message.CarrierId, out Systems_PlayerRole carrierRole);

            if (hasTackler)
            {
                _facts.HasTackler = true;
                _facts.TacklerRole = tacklerRole;
            }

            if (hasCarrier)
            {
                _facts.HasCarrier = true;
                _facts.CarrierRole = carrierRole;
            }

            _facts.HitSpeed = message.ClosingSpeed;

            if (!IsBuilt || !hasTackler || !hasCarrier)
            {
                return;
            }

            float strength = _hitMeter.Show(
                message.ClosingSpeed,
                MassOf(message.TacklerId, tacklerRole),
                MassOf(message.CarrierId, carrierRole),
                Time.time);

            if (strength >= 0f)
            {
                _haptics.Pulse(strength);
            }
        }

        private void OnDownResolved(Systems_DownResolvedMessage message)
        {
            _facts.Resolved = true;
            _facts.Offense = message.Offense;
            _facts.Kind = message.Kind;
            _facts.Result = message.Result;
            _facts.Outcome = message.Outcome;
            _facts.Call = message.Call;
            _facts.YardsGained = message.YardsGained;
            _facts.ReturnYards = message.ReturnYards;
            _facts.PointsScored = message.PointsScored;
            _facts.ScoringTeam = message.ScoringTeam;
            _facts.Foul = message.Foul;
            _facts.FoulTeam = message.FoulTeam;
            _facts.PenaltyAccepted = message.PenaltyAccepted;
            _facts.PenaltyYards = message.PenaltyYards;
            _facts.TwoMinuteWarning = message.TwoMinuteWarning;
            _facts.TimeoutCalled = message.TimeoutCalled;
            _facts.TimeoutTeam = message.TimeoutTeam;
            _facts.HomeScore = _game.HomeScore;
            _facts.AwayScore = _game.AwayScore;

            _linePending = true;
        }

        private void OnGameOver(Systems_GameOverMessage message)
        {
            _gameOver = true;
            _facts.Resolved = true;
            _facts.Result = Systems_DownResult.EndOfGame;
            _facts.HomeScore = message.HomeScore;
            _facts.AwayScore = message.AwayScore;
            _facts.HomeWinSwing = 0f;

            // Straight away rather than next frame: the final whistle is the last
            // thing this view hears, and the swing clause is not used for it.
            PublishLine();
        }

        private void PublishLine()
        {
            if (_facts.Result != Systems_DownResult.EndOfGame && _winProbability != null)
            {
                _facts.HomeWinSwing = _winProbability.LastSwing;
                _facts.HomeWinChance = _winProbability.HomeNow;
            }

            string line = Systems_CommentaryWriter.Describe(_facts);

            if (line.Length == 0 || !IsBuilt)
            {
                return;
            }

            _ticker.Show(line);

            if (Systems_BroadcastSettings.Commentary)
            {
                _speech.Speak(line);
            }
        }

        private bool TryRole(int playerId, out Systems_PlayerRole role)
        {
            Systems_IPlayerHandle handle = _registry?.FindById(playerId);

            if (handle == null)
            {
                role = Systems_PlayerRole.Quarterback;
                return false;
            }

            role = handle.Role;
            return true;
        }

        private float MassOf(int playerId, Systems_PlayerRole role)
        {
            return _roster == null
                ? Systems_RoleTable.MassOf(role)
                : Systems_RoleTable.MassOf(role, _roster.TraitsOf(playerId));
        }
    }
}

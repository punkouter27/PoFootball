using System;
using MessagePipe;
using PoFootball.Models;
using VContainer.Unity;

namespace PoFootball.Systems
{
    /// <summary>
    /// Writes Systems_WinProbabilityModel: one point per resolved down, and the
    /// expected points the play added for the team that had the ball.
    ///
    /// READ ON THE RESOLVED MESSAGE, AFTER THE CHAINS HAVE MOVED. Systems_GameFlowSystem
    /// publishes it from inside its own play-ended handler once the score, the clock,
    /// the down and the possession all describe the NEXT snap — which is exactly the
    /// state a win probability is a probability of. The value before the play is
    /// taken at the snap, when the same model still describes the down being played.
    ///
    /// EPA IS POINTS PLUS THE CHANGE IN WHAT THE BALL IS WORTH, signed for the team
    /// that had it. A touchdown is six points and the try to come; a punt is the
    /// field position handed over, negated; an interception is the opponent's new
    /// possession, negated. One formula covers every one of those without a switch
    /// over the result, because the game model already says whose ball it is next.
    ///
    /// Game layer only: a training run has no score to win.
    /// </summary>
    public sealed class Systems_WinProbabilitySystem : IStartable, IDisposable
    {
        /// <summary>What a kicked extra point is worth to the team taking it.</summary>
        private const float EXTRA_POINT_VALUE = 0.94f;

        /// <summary>A two-point try: about a coin flip for two points.</summary>
        private const float TWO_POINT_VALUE = 0.96f;

        /// <summary>Where a kickoff is assumed to leave the receiving team, yards from its own goal.</summary>
        private const float KICKOFF_RECEIVING_YARD_LINE = 25f;

        private readonly Systems_WinProbabilityModel _model;
        private readonly Systems_GameModel _game;
        private readonly ISubscriber<Systems_PlaySnappedMessage> _snappedSubscriber;
        private readonly ISubscriber<Systems_DownResolvedMessage> _resolvedSubscriber;
        private readonly ISubscriber<Systems_GameOverMessage> _gameOverSubscriber;

        private IDisposable _subscriptions;

        /// <summary>Expected points of the down being played, for its offense, taken at the snap.</summary>
        private float _valueAtSnap;

        private bool _started;

        public Systems_WinProbabilitySystem(
            Systems_WinProbabilityModel model,
            Systems_GameModel game,
            ISubscriber<Systems_PlaySnappedMessage> snappedSubscriber,
            ISubscriber<Systems_DownResolvedMessage> resolvedSubscriber,
            ISubscriber<Systems_GameOverMessage> gameOverSubscriber)
        {
            _model = model;
            _game = game;
            _snappedSubscriber = snappedSubscriber;
            _resolvedSubscriber = resolvedSubscriber;
            _gameOverSubscriber = gameOverSubscriber;
        }

        public void Start()
        {
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            _snappedSubscriber.Subscribe(OnSnapped).AddTo(bag);
            _resolvedSubscriber.Subscribe(OnDownResolved).AddTo(bag);
            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            _subscriptions = bag.Build();

            _model.Reset(0.5f);
        }

        public void Dispose()
        {
            _subscriptions?.Dispose();
        }

        private void OnSnapped(Systems_PlaySnappedMessage message)
        {
            if (!_started)
            {
                // The first snap of the game is the opening kickoff: anchor the
                // ribbon to the state it was kicked from rather than a flat half.
                _started = true;
                _model.Reset(HomeChance());
            }

            _valueAtSnap = PossessionValue();
        }

        private void OnDownResolved(Systems_DownResolvedMessage message)
        {
            Systems_TeamId offense = message.Offense;

            int pointsForOffense = message.PointsScored == 0
                ? 0
                : (message.ScoringTeam == offense ? message.PointsScored : -message.PointsScored);

            float valueNext = _game.Possession == offense
                ? PossessionValue()
                : -PossessionValue();

            float epa = pointsForOffense + valueNext - _valueAtSnap;

            _model.Record(HomeChance(), epa, offense);
        }

        private void OnGameOver(Systems_GameOverMessage message)
        {
            float home = message.HomeScore > message.AwayScore
                ? 1f
                : (message.HomeScore < message.AwayScore ? 0f : 0.5f);

            _model.Record(home, 0f, _game.Possession);
        }

        /// <summary>The home team's chance of winning from the state the game model describes now.</summary>
        private float HomeChance()
        {
            Systems_TeamId offense = _game.Possession;
            int margin = _game.ScoreOf(offense) - _game.ScoreOf(offense.Opponent());

            float secondsLeft = Systems_WinProbability.SecondsLeft(
                _game.Quarter, _game.SecondsRemaining, _game.Phase == Systems_GamePhase.Overtime);

            float offenseChance = Systems_WinProbability.ForOffense(margin, secondsLeft, PossessionValue());
            return offense == Systems_TeamId.Home ? offenseChance : 1f - offenseChance;
        }

        /// <summary>What the next snap is expected to be worth to the team that will take it.</summary>
        private float PossessionValue()
        {
            switch (_game.NextPlayKind)
            {
                case Systems_PlayKind.Kickoff:
                    // The kicking team is the offense for the snap; what it hands
                    // over is the receiver's first down at about its own 25.
                    return -Systems_ExpectedPoints.Of(
                        Systems_FieldModel.OWN_GOAL_LINE_Y
                            + (KICKOFF_RECEIVING_YARD_LINE * Systems_FieldModel.YARD),
                        1,
                        Systems_GameRules.YARDS_TO_GAIN);

                case Systems_PlayKind.ExtraPoint:
                    return EXTRA_POINT_VALUE;

                case Systems_PlayKind.TwoPoint:
                    return TWO_POINT_VALUE;

                default:
                    return Systems_ExpectedPoints.Of(
                        _game.LineOfScrimmageY, _game.Down, _game.YardsToGo);
            }
        }
    }
}

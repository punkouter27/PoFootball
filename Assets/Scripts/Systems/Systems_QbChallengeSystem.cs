using System;
using MessagePipe;
using PoFootball.Models;
using UnityEngine;
using VContainer.Unity;
using Random = Unity.Mathematics.Random;

namespace PoFootball.Systems
{
    /// <summary>
    /// The QB challenge: the human is the quarterback of the home team for one
    /// three-minute period (Systems_GameLength.QbChallenge), and the computer plays
    /// everything else — his ten team-mates, the whole defense, and every snap his
    /// team is not on offense for.
    ///
    /// Per down of his: deals nine plays from Systems_Playbook (plus the punt and,
    /// in range, the field goal on fourth down), applies the one he picks — a
    /// design and a formation for a play, the call itself for a kick — and holds
    /// the snap (Systems_ISnapGate) until he taps the field to hike. During the
    /// play a tap is where the carrier runs, or a handoff or pitch to a team-mate
    /// beside or behind him (Systems_HumanControlModel.TakeAction), and a double
    /// tap is a pass to the spot (TakePass); the throw, handoff or pitch itself
    /// goes through the agent's own action path, so it is judged by the same
    /// legality and mask as any brain's.
    ///
    /// Game mode only, and only for this variant; every other game binds
    /// Systems_OpenSnapGate instead. Ticks before the director and the referee
    /// (registration order), so the gate and the hand are current for the tick
    /// they are read in.
    /// </summary>
    public sealed class Systems_QbChallengeSystem : Systems_ISnapGate, IFixedTickable, IDisposable
    {
        /// <summary>The team the human plays for.</summary>
        public const Systems_TeamId HUMAN_TEAM = Systems_TeamId.Home;

        /// <summary>Its own stream off the episode seed, as every seeded draw here has.</summary>
        private const uint SEED_OFFSET = 0x51ED270Bu;

        private readonly Systems_HumanControlModel _control;
        private readonly Systems_GameModel _game;
        private readonly Systems_PlayModel _play;
        private readonly Systems_PlayerRegistry _registry;
        private readonly IDisposable _subscription;
        private readonly int[] _deck = new int[Systems_Playbook.Plays.Length];

        private Random _rng;

        public Systems_QbChallengeSystem(
            Systems_HumanControlModel control,
            Systems_GameModel game,
            Systems_PlayModel play,
            Systems_PlayerRegistry registry,
            ISubscriber<Systems_PlaySnappedMessage> snapped)
        {
            _control = control;
            _game = game;
            _play = play;
            _registry = registry;

            uint seed = Systems_EpisodeSeed.Value ^ SEED_OFFSET;
            _rng = new Random(seed == 0u ? 1u : seed);

            for (int index = 0; index < _deck.Length; index++)
            {
                _deck[index] = index;
            }

            _subscription = snapped.Subscribe(OnSnapped);
        }

        /// <summary>Shut only before a human down's snap, until he has hiked.</summary>
        public bool IsOpen => !_control.IsAwaitingSnap;

        public bool IsHumanSnap => _control.HumanOnOffense;

        /// <summary>
        /// Thirty seconds, not the trainer's twelve, on the human's downs: a person
        /// weaving a catch-and-run was whistled dead at the opponent's ten in a
        /// played game (Systems_PlayModel.TickCeiling).
        /// </summary>
        public const int HUMAN_TICK_CEILING = 1500;

        public void FixedTick()
        {
            _control.SetHumanOnOffense(IsHumanDown());

            ApplyPick(_control.TakePick());

            bool clicked = _control.TakeClick();
            bool doubleTapped = _control.TakeDoubleTap();

            if (_play.Phase == Systems_PlayPhase.PreSnap)
            {
                if (clicked && _control.IsAwaitingSnap && _control.SelectedCard >= 0)
                {
                    _control.Hike();

                    // The hike's tap pairs with nothing: a double tap to snap is
                    // not a pass thrown from under centre.
                    _control.ForgetTap();
                }
                else if (clicked && _control.HasHiked && _control.HumanOnOffense)
                {
                    // Tapped during the count after his hike (PRESNAP_TICKS): where
                    // he drops to once the ball is snapped. Swallowed, a quick
                    // "hike, tap behind" lost the drop and he stood under centre.
                    _control.SetMoveTarget(_control.ClickWorld, Systems_Formation.QUARTERBACK_SLOT_INDEX);
                }

                _control.SetHovered(-1);
                return;
            }

            Systems_IPlayerHandle carrier = HumanCarrier();

            _control.SetHovered(_control.HasPointer ? BallToTarget(_control.PointerWorld, carrier) : -1);

            if (_play.Phase != Systems_PlayPhase.Live || !_control.HumanOnOffense)
            {
                return;
            }

            if (clicked)
            {
                OnTap(_control.ClickWorld, carrier);
            }

            // A DOUBLE TAP IS A PASS TO THE SPOT, and only a quarterback throws one;
            // the agent judges whether he still may (Agent_ActionLegality).
            if (doubleTapped && carrier != null
                && carrier.Id == Systems_Formation.QUARTERBACK_SLOT_INDEX)
            {
                _control.QueuePass(_control.DoubleTapWorld);
            }
        }

        /// <summary>
        /// A single tap during his play: on a team-mate beside or behind the
        /// carrier it is a handoff or a pitch to him; anywhere else — open grass,
        /// a man downfield, his own linemen in the pocket — it is where the
        /// carrier runs. A pass is a double tap (OnTap's second call never comes:
        /// Systems_HumanControlModel.Tap reports it as a double instead).
        /// </summary>
        private void OnTap(Vector2 world, Systems_IPlayerHandle carrier)
        {
            int target = BallToTarget(world, carrier);

            if (target >= 0)
            {
                _control.QueueAction(target);
                return;
            }

            _control.SetMoveTarget(world, carrier == null ? -1 : carrier.Id);
        }

        /// <summary>
        /// The team-mate a tap at <paramref name="world"/> would give the ball to —
        /// under it, not a lineman, and beside or behind the carrier — or -1.
        /// </summary>
        private int BallToTarget(Vector2 world, Systems_IPlayerHandle carrier)
        {
            if (carrier == null)
            {
                return -1;
            }

            int target = HoveredTeammate(world);
            Systems_IPlayerHandle mate = target >= 0 ? _registry.Get(target) : null;

            bool isBallTo = mate != null
                && mate.Role != Systems_PlayerRole.OffensiveLine
                && mate.Position.y - carrier.Position.y <= Systems_SimConstants.LATERAL_LEVEL_TOLERANCE;

            return isBallTo ? target : -1;
        }

        /// <summary>Whoever of the human's offense has the ball, or null while it is in the air or loose.</summary>
        private Systems_IPlayerHandle HumanCarrier()
        {
            for (int slot = 0; slot < Systems_FormationBook.SLOTS_PER_SIDE; slot++)
            {
                Systems_IPlayerHandle player = _registry.Get(slot);

                if (player != null && player.IsCarrier)
                {
                    return player;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }

        /// <summary>
        /// A new episode is set up and waiting for its snap. If it is the human's
        /// down, deal his hand and shut the gate; otherwise leave it open.
        /// </summary>
        private void OnSnapped(Systems_PlaySnappedMessage message)
        {
            bool humanDown = IsHumanDown();

            _control.SetHumanOnOffense(humanDown);
            _control.BeginDown(humanDown);

            if (!humanDown)
            {
                return;
            }

            _play.SetTickCeiling(HUMAN_TICK_CEILING);

            Deal();

            // Fourth down: the kicks are on the table as well. The field goal only
            // in range — the same rule the quarterback's mask applies.
            if (_play.Down >= Systems_GameRules.DOWNS_PER_SERIES && _play.Kind == Systems_PlayKind.Scrimmage)
            {
                _control.AddCard(Systems_Playbook.Punt);

                if (IsInFieldGoalRange(_play.LineOfScrimmageY))
                {
                    _control.AddCard(Systems_Playbook.FieldGoal);
                }
            }
        }

        /// <summary>Nine distinct plays, a partial Fisher-Yates shuffle of the whole book.</summary>
        private void Deal()
        {
            int count = Mathf.Min(Systems_Playbook.HAND_SIZE, _deck.Length);

            for (int index = 0; index < count; index++)
            {
                int swap = _rng.NextInt(index, _deck.Length);
                (_deck[index], _deck[swap]) = (_deck[swap], _deck[index]);
                _control.AddCard(Systems_Playbook.Plays[_deck[index]]);
            }
        }

        /// <summary>
        /// A play is a design and a formation; a kick is the call itself, latched
        /// before the snap the way a quarterback's pre-snap kick call is, which is
        /// what brings the kicking unit out (Systems_PlayModel.LatchCall).
        /// </summary>
        private void ApplyPick(int cardIndex)
        {
            if (cardIndex < 0 || cardIndex >= _control.HandCount
                || !_control.IsAwaitingSnap || _play.Phase != Systems_PlayPhase.PreSnap)
            {
                return;
            }

            Systems_PlayDesign design = _control.Hand[cardIndex];

            if (design.IsKick)
            {
                // A kick, once latched, cannot be taken back; the formation is
                // already re-forming for it. Picking it again changes nothing.
                _play.LatchCall(design.Call);

                if (_play.Call == design.Call)
                {
                    _control.Select(cardIndex);
                }

                return;
            }

            if (_play.CallIsLatched)
            {
                return;
            }

            _play.SetDesign(design);
            _control.Select(cardIndex);
        }

        private bool IsHumanDown()
        {
            return _game.Phase != Systems_GamePhase.Final
                && _game.Possession == HUMAN_TEAM
                && _play.Kind.HasLiveCall();
        }

        /// <summary>
        /// The offense player nearest <paramref name="world"/> inside the hover
        /// radius, other than whoever has the ball; -1 if nobody is. Linemen count,
        /// so a tap on one is a move into the pocket rather than snapped to a
        /// back two metres further away (BallToTarget).
        /// </summary>
        private int HoveredTeammate(Vector2 world)
        {
            int best = -1;
            float bestSquared = Systems_HumanControlModel.HOVER_RADIUS_METRES
                * Systems_HumanControlModel.HOVER_RADIUS_METRES;

            for (int slot = 0; slot < Systems_FormationBook.SLOTS_PER_SIDE; slot++)
            {
                Systems_IPlayerHandle player = _registry.Get(slot);

                if (player == null || player.IsCarrier)
                {
                    continue;
                }

                float squared = (player.Position - world).sqrMagnitude;

                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    best = slot;
                }
            }

            return best;
        }

        /// <summary>
        /// Distance to the goal line plus the seventeen yards every real field goal
        /// carries — Agent_ActionLegality.IsInFieldGoalRange's rule, which lives in
        /// an assembly this one cannot see.
        /// </summary>
        private static bool IsInFieldGoalRange(float lineOfScrimmageY)
        {
            float yardsToGoalLine = Systems_FieldModel.YardsToAttackingGoal(lineOfScrimmageY);

            return yardsToGoalLine + Systems_GameRules.FIELD_GOAL_SNAP_YARDS
                <= Systems_GameRules.FIELD_GOAL_MAX_YARDS;
        }
    }
}

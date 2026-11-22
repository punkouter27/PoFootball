using UnityEngine;

namespace PoFootball.Models
{
    /// <summary>
    /// The human quarterback's side of a QB challenge: where the pointer is, what
    /// was tapped, the nine plays on offer and which one is picked.
    ///
    /// TAP TO RUN, DOUBLE-TAP TO PASS (2026-10-23), the same on a phone and with a
    /// mouse. A single tap on the field is where the ball carrier runs — at a
    /// sprint, stopping on the spot — and a tap on a team-mate beside or behind
    /// him is a handoff or a pitch. A second tap within DOUBLE_TAP_SECONDS and
    /// DOUBLE_TAP_METRES of the first is a pass to that spot: thrown to the grass,
    /// not to a man, and caught by whoever gets there. The pointer used to steer
    /// whoever had the ball, and a phone has no pointer between touches; it is
    /// kept only for the desktop's hover ring.
    ///
    /// THREE WRITERS, EACH WITH ITS OWN FIELDS, which is why this is a model and
    /// not a message. The pointer view (Systems_HudView) writes the INPUT half —
    /// SetPointer, Tap, Pick — every frame. Systems_QbChallengeSystem consumes
    /// that once per physics tick and writes the STATE half — whether the human is
    /// on offense, the hand, the hovered team-mate, a hike, a move target, an
    /// action or a pass. The controlled agent (Agent_FootballPlayer) reads the
    /// state and takes the action. Views and Agents cannot see each other; both
    /// see this.
    ///
    /// Registered in every mode, disabled outside the QB challenge, so an agent
    /// asks <see cref="IsControlling"/> without knowing which game it is in.
    /// </summary>
    public sealed class Systems_HumanControlModel
    {
        /// <summary>A move target this close to the controlled player: he has arrived and stops.</summary>
        public const float STAND_RADIUS_METRES = 0.75f;

        /// <summary>
        /// He sprints until the target is this near, then eases off linearly, so
        /// the velocity controller brakes him onto the spot instead of past it.
        /// </summary>
        public const float ARRIVAL_RAMP_METRES = 2.5f;

        /// <summary>A new carrier slower than this has no run to keep: he stands until tapped.</summary>
        public const float KEEP_RUNNING_MIN_SPEED = 1f;

        /// <summary>
        /// How near a tap must be to a team-mate to mean him: his body and a
        /// finger's slop. At 2 m a quarterback tapping the grass behind him to drop
        /// back pitched to the fullback standing there — three downs of a played
        /// game (2026-10-23) — because a tap that misses a man is a move.
        /// </summary>
        public const float HOVER_RADIUS_METRES = 1.2f;

        /// <summary>Two taps this close in real time make a double tap, seconds.</summary>
        public const float DOUBLE_TAP_SECONDS = 0.35f;

        /// <summary>...and this close on the field, metres: a finger's wobble at the default zoom.</summary>
        public const float DOUBLE_TAP_METRES = 3f;

        public Systems_HumanControlModel()
            : this(false)
        {
        }

        public Systems_HumanControlModel(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }

        public bool IsEnabled { get; }

        // --- Input: written by the view ---------------------------------------

        public bool HasPointer { get; private set; }

        public Vector2 PointerWorld { get; private set; }

        public bool IsClickPending { get; private set; }

        public Vector2 ClickWorld { get; private set; }

        /// <summary>A second tap on the same spot: a pass to <see cref="DoubleTapWorld"/>.</summary>
        public bool IsDoubleTapPending { get; private set; }

        public Vector2 DoubleTapWorld { get; private set; }

        /// <summary>A card index the view asked for, or -1.</summary>
        public int PendingPick { get; private set; } = -1;

        private float _lastTapTime = float.NegativeInfinity;
        private Vector2 _lastTapWorld;

        public void SetPointer(Vector2 world)
        {
            HasPointer = true;
            PointerWorld = world;
        }

        /// <summary>The pointer left the field: nobody is hovered. Steering never reads it.</summary>
        public void ClearPointer()
        {
            HasPointer = false;
        }

        /// <summary>
        /// A tap at <paramref name="realSeconds"/> of unscaled time — a finger's
        /// clock, not the simulation's, so 8x does not shrink the double-tap
        /// window. The second of two taps inside the window is a double tap and
        /// NOT a second single tap; a third starts over.
        /// </summary>
        public void Tap(Vector2 world, float realSeconds)
        {
            SetPointer(world);

            bool isDouble = realSeconds - _lastTapTime <= DOUBLE_TAP_SECONDS
                && (world - _lastTapWorld).sqrMagnitude <= DOUBLE_TAP_METRES * DOUBLE_TAP_METRES;

            if (isDouble)
            {
                IsDoubleTapPending = true;
                DoubleTapWorld = world;
                _lastTapTime = float.NegativeInfinity;
                return;
            }

            IsClickPending = true;
            ClickWorld = world;
            _lastTapTime = realSeconds;
            _lastTapWorld = world;
        }

        /// <summary>A single tap now, never half of a double one (tests and harnesses).</summary>
        public void Click(Vector2 world)
        {
            IsClickPending = true;
            ClickWorld = world;
            SetPointer(world);
            ForgetTap();
        }

        /// <summary>A double tap now (tests and harnesses).</summary>
        public void DoubleTap(Vector2 world)
        {
            IsDoubleTapPending = true;
            DoubleTapWorld = world;
            SetPointer(world);
            ForgetTap();
        }

        /// <summary>
        /// The last tap was spent on something that must not pair with the next —
        /// the hike: a person who double-taps to snap the ball has not thrown it.
        /// </summary>
        public void ForgetTap()
        {
            _lastTapTime = float.NegativeInfinity;
        }

        public void Pick(int cardIndex)
        {
            PendingPick = cardIndex;
        }

        // --- State: written by Systems_QbChallengeSystem ------------------------

        /// <summary>This snap is the human's: his team has the ball on a down with a live call.</summary>
        public bool HumanOnOffense { get; private set; }

        /// <summary>Before the snap of a human down, until he has picked a play and hiked.</summary>
        public bool IsAwaitingSnap { get; private set; }

        public bool HasHiked { get; private set; }

        /// <summary>The cards dealt for this down: the nine plays, then any kicks.</summary>
        public readonly Systems_PlayDesign[] Hand =
            new Systems_PlayDesign[Systems_Playbook.HAND_SIZE + 2];

        public int HandCount { get; private set; }

        public int SelectedCard { get; private set; } = -1;

        /// <summary>The team-mate under the pointer, or -1.</summary>
        public int HoveredSlot { get; private set; } = -1;

        /// <summary>A team-mate the human tapped during a live play, waiting for the controlled player.</summary>
        public int PendingActionSlot { get; private set; } = -1;

        /// <summary>Where the carrier runs, while <see cref="HasMoveTarget"/>.</summary>
        public Vector2 MoveTarget { get; private set; }

        public bool HasMoveTarget { get; private set; }

        /// <summary>A double-tapped spot waiting for the quarterback to throw at.</summary>
        public bool HasPendingPass { get; private set; }

        public Vector2 PendingPassSpot { get; private set; }

        /// <summary>
        /// The spot the human just threw to, for Systems_BallSystem.Throw to bring
        /// the ball down on. Written by the throwing agent, taken by the ball system.
        /// </summary>
        public bool HasIntendedSpot { get; private set; }

        public Vector2 IntendedSpot { get; private set; }

        /// <summary>
        /// Whose target it is: the carrier when it was tapped, or -1 when nobody of
        /// the human's had the ball (a pass in the air), and then it is for
        /// whoever catches it.
        /// </summary>
        private int _moveTargetPlayer = -1;

        private Vector2 _lastCommand;
        private int _lastCommandPlayer = -1;

        public void NoteIntendedSpot(Vector2 spot)
        {
            IntendedSpot = spot;
            HasIntendedSpot = true;
        }

        public bool TakeIntendedSpot(out Vector2 spot)
        {
            spot = IntendedSpot;
            bool intended = HasIntendedSpot;
            HasIntendedSpot = false;
            return intended;
        }

        public void SetHumanOnOffense(bool humanOnOffense)
        {
            HumanOnOffense = humanOnOffense;
        }

        public void BeginDown(bool awaitingSnap)
        {
            _lastCommand = Vector2.zero;
            _lastCommandPlayer = -1;
            HasMoveTarget = false;
            _moveTargetPlayer = -1;
            HasPendingPass = false;
            HasIntendedSpot = false;
            IsAwaitingSnap = awaitingSnap;
            HasHiked = false;
            SelectedCard = -1;
            PendingActionSlot = -1;
            HandCount = 0;
        }

        public void AddCard(Systems_PlayDesign design)
        {
            if (HandCount < Hand.Length)
            {
                Hand[HandCount++] = design;
            }
        }

        public void Select(int cardIndex)
        {
            SelectedCard = cardIndex;
        }

        public void Hike()
        {
            HasHiked = true;
            IsAwaitingSnap = false;
        }

        public void SetHovered(int slot)
        {
            HoveredSlot = slot;
        }

        public void QueueAction(int slot)
        {
            PendingActionSlot = slot;
        }

        /// <param name="carrierSlot">The human's carrier now, or -1 while the ball
        /// is in the air: the spot is then for whoever catches it.</param>
        public void SetMoveTarget(Vector2 world, int carrierSlot)
        {
            MoveTarget = world;
            HasMoveTarget = true;
            _moveTargetPlayer = carrierSlot;
        }

        public void QueuePass(Vector2 spot)
        {
            PendingPassSpot = spot;
            HasPendingPass = true;
        }

        /// <summary>The view's pick, consumed: -1 when there was none.</summary>
        public int TakePick()
        {
            int pick = PendingPick;
            PendingPick = -1;
            return pick;
        }

        /// <summary>Whether the view tapped since the last tick, consumed.</summary>
        public bool TakeClick()
        {
            bool clicked = IsClickPending;
            IsClickPending = false;
            return clicked;
        }

        /// <summary>Whether the view double-tapped since the last tick, consumed.</summary>
        public bool TakeDoubleTap()
        {
            bool tapped = IsDoubleTapPending;
            IsDoubleTapPending = false;
            return tapped;
        }

        /// <summary>The tapped team-mate for the controlled player to act on, consumed: -1 when none.</summary>
        public int TakeAction()
        {
            int slot = PendingActionSlot;
            PendingActionSlot = -1;
            return slot;
        }

        /// <summary>The double-tapped spot for the quarterback to throw at, consumed.</summary>
        public bool TakePass(out Vector2 spot)
        {
            spot = PendingPassSpot;
            bool pending = HasPendingPass;
            HasPendingPass = false;
            return pending;
        }

        /// <summary>
        /// Whether the human steers this player now: his team on offense, the play
        /// live, and this player — on the offense of the snap — carrying the ball.
        /// The quarterback before he throws or hands off; whoever catches or takes
        /// it after. Nobody while the ball is in the air.
        /// </summary>
        public bool IsControlling(bool isOffenseOfSnap, bool isCarrier, bool isLive)
        {
            return IsEnabled && HumanOnOffense && isLive && isOffenseOfSnap && isCarrier;
        }

        /// <summary>
        /// The velocity command for the controlled player, in the world frame, as a
        /// share of his top speed: a sprint toward the tapped spot, easing off over
        /// ARRIVAL_RAMP, and standing still once he is on it.
        ///
        /// A player who has just taken the ball with no spot tapped for him keeps
        /// running the way he was going, flat out: a receiver who stopped at the
        /// catch would be ruled down where he stood (Systems_Referee's stall rule),
        /// and a carrier the human has not touched yet has not given himself up.
        /// </summary>
        /// <param name="velocity">His velocity now, for that kept run.</param>
        /// <param name="playerId">Who is being steered: a target and a held command
        /// are his alone, so a receiver who has just caught the ball does not
        /// inherit the quarterback's drop.</param>
        public Vector2 CommandToward(Vector2 position, Vector2 velocity, int playerId)
        {
            if (playerId != _lastCommandPlayer)
            {
                _lastCommandPlayer = playerId;

                if (HasMoveTarget && _moveTargetPlayer >= 0 && _moveTargetPlayer != playerId)
                {
                    HasMoveTarget = false;
                }

                float speed = velocity.magnitude;
                _lastCommand = speed >= KEEP_RUNNING_MIN_SPEED ? velocity / speed : Vector2.zero;
            }

            if (!HasMoveTarget)
            {
                return _lastCommand;
            }

            _moveTargetPlayer = playerId;

            Vector2 toTarget = MoveTarget - position;
            float distance = toTarget.magnitude;

            if (distance <= STAND_RADIUS_METRES)
            {
                HasMoveTarget = false;
                _lastCommand = Vector2.zero;
                return _lastCommand;
            }

            float share = Mathf.Clamp01((distance - STAND_RADIUS_METRES) / ARRIVAL_RAMP_METRES);

            _lastCommand = (toTarget / distance) * share;
            return _lastCommand;
        }
    }
}

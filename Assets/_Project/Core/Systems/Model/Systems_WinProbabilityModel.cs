namespace PoFootball.Models
{
    /// <summary>
    /// The game's win probability, one point per play, always from the HOME
    /// team's side so the line reads the same way all game however the field is
    /// mirrored for the offense. Systems_WinProbabilitySystem is the only writer;
    /// the broadcast ribbon reads.
    ///
    /// Plain fields and a <see cref="Version"/> counter rather than
    /// ReactiveProperty, matching Systems_GameModel: it changes once a play, and a
    /// view that redraws a polyline only when the counter moves does nothing at all
    /// on the other ninety-nine frames out of a hundred.
    /// </summary>
    public sealed class Systems_WinProbabilityModel
    {
        /// <summary>
        /// Points kept. A game is about 75 scrimmage snaps plus kicks and tries, and
        /// an overtime adds a dozen; past this the oldest half is thinned, so the
        /// whole game always fits on the ribbon.
        /// </summary>
        public const int CAPACITY = 256;

        private readonly float[] _history = new float[CAPACITY];

        /// <summary>Home team's chance of winning, history[0] being kickoff.</summary>
        public float HomeNow { get; private set; } = 0.5f;

        public int Count { get; private set; }

        /// <summary>Bumped on every write. A reader redraws when it has moved.</summary>
        public int Version { get; private set; }

        /// <summary>Change in the home team's chance on the last play.</summary>
        public float LastSwing { get; private set; }

        /// <summary>Expected points added by the last play, for the team that had the ball.</summary>
        public float LastEpa { get; private set; }

        /// <summary>Which team had the ball on the last play.</summary>
        public Systems_TeamId LastOffense { get; private set; } = Systems_TeamId.Home;

        /// <summary>The largest single-play change in the home team's chance so far.</summary>
        public float BiggestSwing { get; private set; }

        /// <summary>Index into the history of the point the biggest swing arrived at.</summary>
        public int BiggestSwingIndex { get; private set; } = -1;

        public float HistoryAt(int index)
        {
            return _history[index];
        }

        // --- Mutators. Systems_WinProbabilitySystem only. ---------------------

        public void Reset(float homeNow)
        {
            Count = 0;
            BiggestSwing = 0f;
            BiggestSwingIndex = -1;
            LastSwing = 0f;
            LastEpa = 0f;
            HomeNow = homeNow;
            Append(homeNow);
            Version++;
        }

        public void Record(float homeNow, float epa, Systems_TeamId offense)
        {
            float swing = homeNow - HomeNow;

            HomeNow = homeNow;
            LastSwing = swing;
            LastEpa = epa;
            LastOffense = offense;

            Append(homeNow);

            if (System.Math.Abs(swing) > System.Math.Abs(BiggestSwing))
            {
                BiggestSwing = swing;
                BiggestSwingIndex = Count - 1;
            }

            Version++;
        }

        private void Append(float value)
        {
            if (Count == CAPACITY)
            {
                // Thin to every other point. The ribbon is a shape, not a log; a
                // game long enough to fill this has its first half drawn at half
                // the resolution, which nobody can see at 1080 units wide.
                int kept = 0;

                for (int index = 0; index < CAPACITY; index += 2)
                {
                    _history[kept] = _history[index];
                    kept++;
                }

                if (BiggestSwingIndex >= 0)
                {
                    BiggestSwingIndex /= 2;
                }

                Count = kept;
            }

            _history[Count] = value;
            Count++;
        }
    }
}

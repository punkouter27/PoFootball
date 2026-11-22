namespace PoFootball.Models
{
    public enum Systems_AppMode
    {
        Watch = 0,
        Play = 1,
        Testbed = 2,
    }

    /// <summary>
    /// What the simulation knows about its own health, for the DEBUG sheet.
    ///
    /// WHY IT EXISTS. The sheet could say the frame was slow and could not say the
    /// two things that matter most about a build of this game: whether anything on
    /// the field is a trained policy, and whether what is being played is football.
    /// Both answers already existed — one in a console warning logged once at
    /// startup, the other in the REALISM line logged once at the final whistle —
    /// and neither is readable on a handset, mid-game, by someone holding it.
    ///
    /// Three writers, one reader. The agents report whether they got a brain,
    /// when the speed clamp fires and what their fatigue was loaded by;
    /// Systems_GameFlowSystem reports each resolved play; Systems_RoleShapeApplier
    /// reports how well the drawn bodies match their colliders;
    /// Systems_StatusHudView reads. Nothing in the simulation reads any of
    /// it back, so it cannot change a result.
    ///
    /// Plain fields rather than ReactiveProperty, for the reason Systems_PlayModel
    /// gives: the clamp counter is on the per-tick path, and the one reader polls
    /// four times a second while its sheet is open.
    /// </summary>
    public sealed class Systems_SimHealthModel
    {
        private const int PLAYERS_ON_FIELD = Systems_RoleTable.SQUAD_SIZE * 2;

        /// <summary>Agents that have configured their brain so far. 22 once the scene is up.</summary>
        public int PlayersReported { get; private set; }

        /// <summary>Of those, how many were handed a trained model rather than the heuristic.</summary>
        public int PlayersWithBrain { get; private set; }

        /// <summary>
        /// Why there is no brain, in the registry's own words, or empty when there
        /// is one. Carried as text because the reason is the useful part — "revision
        /// 10 against a build on 11" tells the reader what to do; a boolean does not.
        /// </summary>
        public string BrainStatus { get; private set; } = string.Empty;

        /// <summary>Times the pileup-explosion guard fired this session.</summary>
        public int SpeedClampHits { get; private set; }

        /// <summary>Physics ticks the ball has been live this session.</summary>
        public int LiveTicks { get; private set; }

        /// <summary>Count of non-finite (NaN/Inf) observation or action values sanitized this session.</summary>
        public int NanInfCount { get; private set; }

        /// <summary>Active viewer mode (Watch, Play, Testbed), cycled via F1.</summary>
        public Systems_AppMode AppMode { get; set; } = Systems_AppMode.Watch;

        /// <summary>When set via F4, toggles policy override visibility/hot-swap state.</summary>
        public bool UseFallbackPolicy { get; set; }

        public void CountNanInf() => NanInfCount++;

        public int ScrimmagePlays { get; private set; }

        public float ScrimmageYards { get; private set; }

        public int FourthDowns { get; private set; }

        public int Drives { get; private set; }

        public int Touchdowns { get; private set; }

        public float YardsPerPlay => ScrimmagePlays > 0 ? ScrimmageYards / ScrimmagePlays : 0f;

        public float TouchdownsPerDrive => Drives > 0 ? Touchdowns / (float)Drives : 0f;

        /// <summary>
        /// Clamp hits per player per live tick — the same quantity as the trainer's
        /// Control/SpeedClampRate, which the promotion gate refuses above 0.001.
        /// </summary>
        public float SpeedClampRate =>
            LiveTicks > 0 ? SpeedClampHits / (float)(LiveTicks * PLAYERS_ON_FIELD) : 0f;

        // --- Fatigue load, by where it came from ------------------------------
        //
        // CLAUDE.md section 2 says fatigue reads load from APPLIED force and
        // torque — contact impulse included since revision 12, because a braced
        // lineman is a near-zero action at near-maximum force. That is a claim
        // about Agent_FootballPlayer.AccumulateFatigue that nothing checked at
        // run time: these sums are the check. Each is dimensionless load (a
        // term over that role's own maximum) summed over every player-tick.
        //
        // Doubles, because a full game is about two million player-ticks and a
        // float sum stops moving long before that.

        /// <summary>Load from the drive force, summed over every player-tick.</summary>
        public double DriveLoad { get; private set; }

        /// <summary>Load from the steering torque.</summary>
        public double SteerLoad { get; private set; }

        /// <summary>Load from the contact impulse the solver resolved against the body.</summary>
        public double ContactLoad { get; private set; }

        /// <summary>Player-ticks the three sums cover.</summary>
        public int LoadTicks { get; private set; }

        // NO "THE SOLVER IS BLOWING CONTACTS APART" COUNTER HERE, and two were
        // tried. Ticks at FATIGUE_CONTACT_LOAD_MAX read 5.9% of player-ticks in a
        // played game — but the cap is one body's own full drive force, which a
        // lineman held in a block meets legitimately. Ticks past four times the
        // cap read 0.8% — but a tackle delivers its whole impulse inside one
        // 20 ms step, about seven times a lineman's drive force, so that counted
        // tackles. An impulse a body could NOT survive throws it past
        // MAX_BODY_SPEED, and the speed clamp already counts exactly that.

        /// <summary>Share of all load that came from contact, in [0, 1].</summary>
        public float ContactShare
        {
            get
            {
                double total = DriveLoad + SteerLoad + ContactLoad;
                return total > 0.0 ? (float)(ContactLoad / total) : 0f;
            }
        }

        // --- Bodies against colliders -----------------------------------------
        //
        // Systems_RoleShapeApplier promises a player's sprite is exactly as big
        // as the 0.5 m collider the tackle rule is calibrated on. This is what it
        // measured when it painted them.

        /// <summary>Players whose drawn size was compared with their collider. 0 until checked.</summary>
        public int BodiesChecked { get; private set; }

        /// <summary>Largest difference between a drawn body and its collider, in metres.</summary>
        public float WorstBodyMismatch { get; private set; }

        /// <summary>Who that was, in words, for the sheet.</summary>
        public string WorstBodyName { get; private set; } = string.Empty;

        public void RecordLoad(float drive, float steer, float contact)
        {
            DriveLoad += drive;
            SteerLoad += steer;
            ContactLoad += contact;
            LoadTicks++;
        }

        public void ReportBodyFit(int checkedCount, float worstMismatch, string worstName)
        {
            BodiesChecked = checkedCount;
            WorstBodyMismatch = worstMismatch;
            WorstBodyName = worstName ?? string.Empty;
        }

        public void ReportBrain(bool hasModel, string status)
        {
            PlayersReported++;

            if (hasModel)
            {
                PlayersWithBrain++;
            }

            BrainStatus = status ?? string.Empty;
        }

        // --- RL readout for the TAB overlay -----------------------------------
        //
        // What the players were paid. Agent_FootballPlayer reports its cumulative
        // reward as each play closes; the mean over the last finished play is the
        // "episode reward" the overlay shows. Penalty sums are lifetime totals per
        // control penalty (Reward_Penalty) over every player-tick.

        private int _rewardEpisode = -1;
        private float _rewardEpisodeSum;
        private int _rewardEpisodeCount;

        /// <summary>Mean reward per player over the last finished play; NaN before the first.</summary>
        public float LastEpisodeReward { get; private set; } = float.NaN;

        /// <summary>Plays whose reward has been reported.</summary>
        public int EpisodesReported { get; private set; }

        public double PenaltyEffort { get; private set; }

        public double PenaltyAngularVelocity { get; private set; }

        public double PenaltyActionJitter { get; private set; }

        public double PenaltySlide { get; private set; }

        /// <summary>
        /// The trainer's curriculum lesson, or -1 where there is no curriculum (a
        /// played game, or a training scene with no trainer attached).
        /// </summary>
        public int CurriculumLesson { get; set; } = -1;

        public void RecordEpisodeReward(int episode, float reward)
        {
            if (episode != _rewardEpisode)
            {
                if (_rewardEpisodeCount > 0)
                {
                    LastEpisodeReward = _rewardEpisodeSum / _rewardEpisodeCount;
                    EpisodesReported++;
                }

                _rewardEpisode = episode;
                _rewardEpisodeSum = 0f;
                _rewardEpisodeCount = 0;
            }

            _rewardEpisodeSum += reward;
            _rewardEpisodeCount++;

            // Every player reports at the same whistle: once all have, the play is done.
            if (_rewardEpisodeCount >= PLAYERS_ON_FIELD)
            {
                LastEpisodeReward = _rewardEpisodeSum / _rewardEpisodeCount;
                EpisodesReported++;
                _rewardEpisodeCount = 0;
                _rewardEpisodeSum = 0f;
                _rewardEpisode = int.MinValue;
            }
        }

        public void RecordPenalties(float effort, float angularVelocity, float actionJitter, float slide)
        {
            PenaltyEffort += effort;
            PenaltyAngularVelocity += angularVelocity;
            PenaltyActionJitter += actionJitter;
            PenaltySlide += slide;
        }

        public void CountSpeedClamp()
        {
            SpeedClampHits++;
        }

        public void CountLiveTick()
        {
            LiveTicks++;
        }

        /// <summary>
        /// One resolved play. <paramref name="isScrimmagePlay"/> is false for a
        /// kick, which is spotted at the line and would drag the average down
        /// without describing anything the offense did.
        /// </summary>
        public void RecordPlay(
            bool isScrimmagePlay, float yardsGained, bool facedFourthDown, bool touchdown, int drives)
        {
            if (isScrimmagePlay)
            {
                ScrimmagePlays++;
                ScrimmageYards += yardsGained;
            }

            if (facedFourthDown)
            {
                FourthDowns++;
            }

            if (touchdown)
            {
                Touchdowns++;
            }

            Drives = drives;
        }
    }
}

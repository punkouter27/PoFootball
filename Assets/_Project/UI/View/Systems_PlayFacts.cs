using PoFootball.Models;

namespace PoFootball.Views
{
    /// <summary>
    /// Everything the commentary needs to say about one play, gathered as it
    /// happens: the situation at the snap, who threw, caught, carried and
    /// tackled, and how it resolved.
    ///
    /// A plain bag with settable properties rather than a message, because it is
    /// filled from five different messages at five different moments and read
    /// once, at the end. One instance is reused for every play (Clear at the
    /// snap), so a game of a hundred plays allocates it once.
    ///
    /// Roles rather than player ids: the commentary names "the halfback", not a
    /// slot index, and a test can build a play without standing up a registry.
    /// </summary>
    public sealed class Systems_PlayFacts
    {
        // --- At the snap ------------------------------------------------------

        public int PlayNumber { get; set; }

        public Systems_TeamId Offense { get; set; }

        public Systems_PlayKind Kind { get; set; }

        public int Down { get; set; }

        public float YardsToGo { get; set; }

        /// <summary>Line of scrimmage, attacking frame, metres.</summary>
        public float LineY { get; set; }

        // --- During the play ---------------------------------------------------

        public bool Thrown { get; set; }

        public bool HasTarget { get; set; }

        public Systems_PlayerRole TargetRole { get; set; }

        public bool Caught { get; set; }

        public bool Intercepted { get; set; }

        public Systems_PlayerRole CatcherRole { get; set; }

        public float AirYards { get; set; }

        public bool HasCarrier { get; set; }

        public Systems_PlayerRole CarrierRole { get; set; }

        public bool HasTackler { get; set; }

        public Systems_PlayerRole TacklerRole { get; set; }

        /// <summary>Closing speed of the tackle, m/s. Zero when nobody made one.</summary>
        public float HitSpeed { get; set; }

        // --- At the whistle ------------------------------------------------------

        public bool Resolved { get; set; }

        public Systems_DownResult Result { get; set; }

        public Systems_PlayOutcome Outcome { get; set; }

        public Systems_PlayCall Call { get; set; }

        public float YardsGained { get; set; }

        public float ReturnYards { get; set; }

        public int PointsScored { get; set; }

        public Systems_TeamId ScoringTeam { get; set; }

        public Systems_FoulType Foul { get; set; }

        public Systems_TeamId FoulTeam { get; set; }

        public bool PenaltyAccepted { get; set; }

        public float PenaltyYards { get; set; }

        public bool TwoMinuteWarning { get; set; }

        public bool TimeoutCalled { get; set; }

        public Systems_TeamId TimeoutTeam { get; set; }

        /// <summary>Score after the play, for the line that follows a score.</summary>
        public int HomeScore { get; set; }

        public int AwayScore { get; set; }

        /// <summary>Change in the home team's chance of winning on this play.</summary>
        public float HomeWinSwing { get; set; }

        /// <summary>The home team's chance of winning after this play.</summary>
        public float HomeWinChance { get; set; } = 0.5f;

        public void Clear()
        {
            PlayNumber = 0;
            Offense = Systems_TeamId.Home;
            Kind = Systems_PlayKind.Scrimmage;
            Down = 1;
            YardsToGo = Systems_GameRules.YARDS_TO_GAIN;
            LineY = 0f;
            Thrown = false;
            HasTarget = false;
            TargetRole = Systems_PlayerRole.WideReceiver;
            Caught = false;
            Intercepted = false;
            CatcherRole = Systems_PlayerRole.WideReceiver;
            AirYards = 0f;
            HasCarrier = false;
            CarrierRole = Systems_PlayerRole.RunningBack;
            HasTackler = false;
            TacklerRole = Systems_PlayerRole.Linebacker;
            HitSpeed = 0f;
            Resolved = false;
            Result = Systems_DownResult.NextDown;
            Outcome = Systems_PlayOutcome.None;
            Call = Systems_PlayCall.None;
            YardsGained = 0f;
            ReturnYards = 0f;
            PointsScored = 0;
            ScoringTeam = Systems_TeamId.Home;
            Foul = Systems_FoulType.None;
            FoulTeam = Systems_TeamId.Home;
            PenaltyAccepted = false;
            PenaltyYards = 0f;
            TwoMinuteWarning = false;
            TimeoutCalled = false;
            TimeoutTeam = Systems_TeamId.Home;
            HomeScore = 0;
            AwayScore = 0;
            HomeWinSwing = 0f;
            HomeWinChance = 0.5f;
        }
    }
}

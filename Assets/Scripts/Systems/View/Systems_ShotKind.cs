namespace PoFootball.Views
{
    /// <summary>
    /// The shot the broadcast director has chosen for the situation on the field.
    /// Read by Systems_BroadcastCameraView to frame it and exposed from there so
    /// the HUD can label it; chosen by <see cref="Systems_ShotDirector"/>.
    ///
    /// <see cref="Default"/> is the operator's shot that existed before there was
    /// a director — ball-follow with the lead, the threat pull and the zoom that
    /// opens as the play gets away from the line. Every other value is a framing
    /// the director lays over it for a moment, and every one of them hands back to
    /// Default when the moment is over.
    /// </summary>
    public enum Systems_ShotKind
    {
        Default = 0,

        /// <summary>Tight on a quarterback still holding the ball with the rush arriving.</summary>
        Pocket = 1,

        /// <summary>Wide and leading a carrier nobody can catch before the goal line.</summary>
        Breakaway = 2,

        /// <summary>Pulled back to hold the passer, the ball and its target during a pass.</summary>
        BallInAir = 3,

        /// <summary>The ball and the man who will field it while a kick is in the air.</summary>
        Kick = 4,

        /// <summary>The whole end zone in frame when the snap is inside the five.</summary>
        GoalLine = 5
    }
}

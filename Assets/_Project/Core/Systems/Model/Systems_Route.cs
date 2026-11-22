namespace PoFootball.Models
{
    /// <summary>
    /// What one eligible player does on a designed play (Systems_PlayDesign). The
    /// geometry is Systems_RouteBook's; "inside" and "outside" are relative to the
    /// side of the ball the player lined up on.
    ///
    /// Before these existed every receiver ran straight at the goal line, split
    /// left or right by slot parity, on every play — Pass and PassDeep differed only
    /// in when the quarterback let go. They are used only where a play has been
    /// designed, which today is the human quarterback's offense; the scripted
    /// offense still runs the old fan, so the CPU-only game is unchanged.
    /// </summary>
    public enum Systems_Route
    {
        /// <summary>Block the nearest defender: a lineman's job, given to anyone.</summary>
        Block = 0,

        /// <summary>The designed ball carrier on a handoff: close on the quarterback.</summary>
        Handoff = 1,

        /// <summary>Straight down the field.</summary>
        Go = 2,

        /// <summary>Ten yards, then deep toward the posts.</summary>
        Post = 3,

        /// <summary>Ten yards, then deep toward the sideline.</summary>
        Corner = 4,

        /// <summary>Two yards, then hard inside.</summary>
        Slant = 5,

        /// <summary>Six yards, then flat to the sideline.</summary>
        Out = 6,

        /// <summary>Ten yards, then flat across the middle.</summary>
        Dig = 7,

        /// <summary>Ten yards, then back to eight and settle.</summary>
        Curl = 8,

        /// <summary>Five yards, then turn and settle at four.</summary>
        Hitch = 9,

        /// <summary>Out to the flat two yards deep, then upfield.</summary>
        Flat = 10,

        /// <summary>To the flat, then straight up the sideline.</summary>
        Wheel = 11,

        /// <summary>A back's arc outside, a yard behind the line, then upfield.</summary>
        Swing = 12,

        /// <summary>A yard and a half, then shallow across the formation.</summary>
        Drag = 13,
    }
}

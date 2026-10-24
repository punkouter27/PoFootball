namespace PoFootball.Models
{
    /// <summary>
    /// One play out of the human quarterback's playbook (Systems_Playbook): the
    /// alignment, the call it is designed around, and a job for each of the five
    /// eligible slots — the tight end, the two wide receivers and the two backs.
    /// The five linemen always block.
    ///
    /// THE CALL IS A DESIGN, NOT A COMMITMENT. Systems_PlayModel latches the real
    /// call only when the quarterback acts — a handoff, a throw, crossing the line —
    /// so a run can still be thrown and a pass handed off. The design is what the
    /// ten other players do from the snap: run blocking or pass protection, the
    /// back coming for the ball or releasing into a route.
    /// </summary>
    public readonly struct Systems_PlayDesign
    {
        public readonly string Name;

        public readonly Systems_OffensiveFormation Formation;

        public readonly Systems_PlayCall Call;

        public readonly Systems_Route TightEnd;

        /// <summary>Slot 6, the receiver who lines up on the left.</summary>
        public readonly Systems_Route LeftReceiver;

        /// <summary>Slot 7, the receiver who lines up on the right.</summary>
        public readonly Systems_Route RightReceiver;

        public readonly Systems_Route Fullback;

        public readonly Systems_Route Halfback;

        public Systems_PlayDesign(
            string name, Systems_OffensiveFormation formation, Systems_PlayCall call,
            Systems_Route tightEnd, Systems_Route leftReceiver, Systems_Route rightReceiver,
            Systems_Route fullback, Systems_Route halfback)
        {
            Name = name;
            Formation = formation;
            Call = call;
            TightEnd = tightEnd;
            LeftReceiver = leftReceiver;
            RightReceiver = rightReceiver;
            Fullback = fullback;
            Halfback = halfback;
        }

        /// <summary>No design: the default value, which the scripted offense plays under.</summary>
        public bool IsNone => Name == null;

        public bool IsKick => Call.IsKick();

        public bool IsPass => Call.IsPass();

        /// <summary>The job of formation slot <paramref name="slot"/>, or Block for any other slot.</summary>
        public Systems_Route RouteOf(int slot)
        {
            switch (slot)
            {
                case TIGHT_END_SLOT:
                    return TightEnd;
                case LEFT_RECEIVER_SLOT:
                    return LeftReceiver;
                case RIGHT_RECEIVER_SLOT:
                    return RightReceiver;
                case FULLBACK_SLOT:
                    return Fullback;
                case HALFBACK_SLOT:
                    return Halfback;
                default:
                    return Systems_Route.Block;
            }
        }

        // The eligible slots. Systems_FormationBook's slot constants live in the
        // Systems assembly; Systems_PlaybookTests pins these to them.
        public const int TIGHT_END_SLOT = 5;
        public const int LEFT_RECEIVER_SLOT = 6;
        public const int RIGHT_RECEIVER_SLOT = 7;
        public const int FULLBACK_SLOT = 9;
        public const int HALFBACK_SLOT = 10;
    }
}

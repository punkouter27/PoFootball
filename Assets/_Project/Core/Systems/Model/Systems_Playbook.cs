namespace PoFootball.Models
{
    /// <summary>
    /// The human quarterback's playbook: twenty plays across all eight offensive
    /// formations, nine of which are dealt at random before every down
    /// (Systems_QbChallengeSystem), plus the two kicks, offered on fourth down only.
    ///
    /// Eight runs and twelve passes. Every run names its ball carrier through the
    /// Handoff route and every eligible player on it blocks; every pass sends at
    /// least three players into routes at more than one depth, so there is always a
    /// short read and a long one. Systems_PlaybookTests holds each play to that.
    /// </summary>
    public static class Systems_Playbook
    {
        public const int HAND_SIZE = 9;

        private const Systems_Route B = Systems_Route.Block;
        private const Systems_Route H = Systems_Route.Handoff;

        public static readonly Systems_PlayDesign[] Plays =
        {
            // --- Runs ---------------------------------------------------------
            new Systems_PlayDesign("HB DIVE", Systems_OffensiveFormation.ProI,
                Systems_PlayCall.HandoffHalfback, B, B, B, B, H),
            new Systems_PlayDesign("FB BLAST", Systems_OffensiveFormation.StrongI,
                Systems_PlayCall.HandoffFullback, B, B, B, H, B),
            new Systems_PlayDesign("WEAK ISO", Systems_OffensiveFormation.WeakI,
                Systems_PlayCall.HandoffHalfback, B, B, B, B, H),
            new Systems_PlayDesign("SPLIT ZONE", Systems_OffensiveFormation.SplitBacks,
                Systems_PlayCall.HandoffHalfback, B, B, B, B, H),
            new Systems_PlayDesign("HB STRETCH", Systems_OffensiveFormation.Singleback,
                Systems_PlayCall.HandoffHalfback, B, B, B, B, H),
            new Systems_PlayDesign("GUN DRAW", Systems_OffensiveFormation.Shotgun,
                Systems_PlayCall.HandoffHalfback, B, B, B, B, H),
            new Systems_PlayDesign("PISTOL POWER", Systems_OffensiveFormation.Pistol,
                Systems_PlayCall.HandoffHalfback, B, B, B, B, H),
            new Systems_PlayDesign("QB KEEPER", Systems_OffensiveFormation.Shotgun,
                Systems_PlayCall.KeepQuarterback, B, B, B, B, B),

            // --- Passes: tight end, left WR, right WR, fullback, halfback -----
            new Systems_PlayDesign("SLANTS", Systems_OffensiveFormation.Singleback,
                Systems_PlayCall.Pass, Systems_Route.Go, Systems_Route.Slant,
                Systems_Route.Slant, B, Systems_Route.Flat),
            new Systems_PlayDesign("CURL FLAT", Systems_OffensiveFormation.ProI,
                Systems_PlayCall.Pass, Systems_Route.Flat, Systems_Route.Curl,
                Systems_Route.Curl, B, Systems_Route.Swing),
            new Systems_PlayDesign("OUTS", Systems_OffensiveFormation.Singleback,
                Systems_PlayCall.Pass, Systems_Route.Curl, Systems_Route.Out,
                Systems_Route.Out, B, Systems_Route.Flat),
            new Systems_PlayDesign("MESH", Systems_OffensiveFormation.Shotgun,
                Systems_PlayCall.Pass, Systems_Route.Drag, Systems_Route.Drag,
                Systems_Route.Dig, B, Systems_Route.Swing),
            new Systems_PlayDesign("SMASH", Systems_OffensiveFormation.Pistol,
                Systems_PlayCall.Pass, Systems_Route.Post, Systems_Route.Hitch,
                Systems_Route.Corner, B, Systems_Route.Flat),
            new Systems_PlayDesign("HB WHEEL", Systems_OffensiveFormation.SplitBacks,
                Systems_PlayCall.Pass, Systems_Route.Flat, Systems_Route.Curl,
                Systems_Route.Dig, B, Systems_Route.Wheel),
            new Systems_PlayDesign("TE SEAM", Systems_OffensiveFormation.Wing,
                Systems_PlayCall.Pass, Systems_Route.Go, Systems_Route.Out,
                Systems_Route.Curl, B, Systems_Route.Flat),
            new Systems_PlayDesign("HITCHES", Systems_OffensiveFormation.StrongI,
                Systems_PlayCall.Pass, Systems_Route.Go, Systems_Route.Hitch,
                Systems_Route.Hitch, Systems_Route.Flat, B),
            new Systems_PlayDesign("FOUR VERTS", Systems_OffensiveFormation.Shotgun,
                Systems_PlayCall.PassDeep, Systems_Route.Go, Systems_Route.Go,
                Systems_Route.Go, Systems_Route.Flat, Systems_Route.Swing),
            new Systems_PlayDesign("POST CORNER", Systems_OffensiveFormation.Shotgun,
                Systems_PlayCall.PassDeep, Systems_Route.Dig, Systems_Route.Post,
                Systems_Route.Corner, B, Systems_Route.Flat),
            new Systems_PlayDesign("PA DEEP", Systems_OffensiveFormation.ProI,
                Systems_PlayCall.PassDeep, Systems_Route.Dig, Systems_Route.Post,
                Systems_Route.Go, B, Systems_Route.Flat),
            new Systems_PlayDesign("WING POST", Systems_OffensiveFormation.Wing,
                Systems_PlayCall.PassDeep, Systems_Route.Corner, Systems_Route.Post,
                Systems_Route.Slant, B, Systems_Route.Swing),
        };

        public static readonly Systems_PlayDesign Punt = new Systems_PlayDesign(
            "PUNT", Systems_OffensiveFormation.ProI, Systems_PlayCall.Punt, B, B, B, B, B);

        public static readonly Systems_PlayDesign FieldGoal = new Systems_PlayDesign(
            "FIELD GOAL", Systems_OffensiveFormation.ProI, Systems_PlayCall.FieldGoal,
            B, B, B, B, B);
    }
}

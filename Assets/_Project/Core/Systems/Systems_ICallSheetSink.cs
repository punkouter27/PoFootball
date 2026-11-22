using PoFootball.Models;

namespace PoFootball.Systems
{
    /// <summary>
    /// Where the two coordinators report what they were weighing before the snap:
    /// the defense's chances over its eight fronts and the offense's over its
    /// play calls. The broadcast's call sheet reads it through
    /// <see cref="Systems_ICallSheetSource"/>.
    ///
    /// AN INTERFACE THIS ASSEMBLY OWNS, for the same reason as
    /// Systems_IIntentSink: the writers are ML-Agents agents and this assembly may
    /// not name them. The agents push, the view reads, and neither knows the other.
    ///
    /// BOUND TO A NO-OP IN TRAINING. Reading a trained brain's chances means
    /// sampling its policy a few dozen times, which a trainer has no use for and
    /// must not pay for; the null binding says Available false and the agents skip
    /// the work entirely.
    /// </summary>
    public interface Systems_ICallSheetSink
    {
        /// <summary>False when nobody is watching; a writer skips its work on this.</summary>
        bool Available { get; }

        /// <param name="chances">
        /// Indexed by Systems_DefensiveFormation, Systems_FormationBook.DefensiveFormationCount long.
        /// Copied; the caller may reuse the array.
        /// </param>
        void ReportDefense(int episodeIndex, float[] chances, Systems_CallSource source);

        /// <param name="chances">
        /// Indexed by (int)Systems_PlayCall, CALL_SLOTS long, or null when only the
        /// choice is known. Copied; the caller may reuse the array.
        /// </param>
        void ReportOffense(int episodeIndex, float[] chances, Systems_CallSource source);
    }

    /// <summary>The read side of <see cref="Systems_ICallSheetSink"/>.</summary>
    public interface Systems_ICallSheetSource
    {
        bool Available { get; }

        /// <summary>Bumped on every report. A reader redraws when it has moved.</summary>
        int Version { get; }

        int DefenseEpisode { get; }

        Systems_CallSource DefenseSource { get; }

        float DefenseChance(int front);

        int OffenseEpisode { get; }

        Systems_CallSource OffenseSource { get; }

        float OffenseChance(Systems_PlayCall call);
    }
}

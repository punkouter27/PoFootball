namespace PoFootball.Models
{
    /// <summary>
    /// Where a call sheet's chances came from, which the broadcast says out loud:
    /// a reader who is told "78% run" deserves to know whether that is a trained
    /// brain's judgement or the scripted fallback's table.
    /// </summary>
    public enum Systems_CallSource
    {
        /// <summary>Nothing reported for this play.</summary>
        None = 0,

        /// <summary>The heuristic's own chances, computed exactly from its rules.</summary>
        Scripted = 1,

        /// <summary>A trained brain, estimated by sampling its policy on this play's observation.</summary>
        Policy = 2,

        /// <summary>A trained brain whose chances could not be read; only its choice is known.</summary>
        PolicyChoiceOnly = 3,

        /// <summary>The QB challenge's human called it: there are no odds to show.</summary>
        Human = 4
    }
}

using PoFootball.Models;

namespace PoFootball.Systems
{
    /// <summary>
    /// The live binding of the call sheet: the last front chances and play-call
    /// chances reported, each stamped with the play they belong to so a reader
    /// can tell this snap's sheet from the last one's.
    /// </summary>
    public sealed class Systems_CallSheet : Systems_ICallSheetSink, Systems_ICallSheetSource
    {
        /// <summary>One slot per Systems_PlayCall value, None included.</summary>
        public const int CALL_SLOTS = (int)Systems_PlayCall.PassDeep + 1;

        /// <summary>
        /// Draws per estimate when a trained brain's chances are sampled
        /// (Agent_PolicySampler), and what the call sheet says it did.
        /// </summary>
        public const int POLICY_SAMPLES = 64;

        private readonly float[] _defense = new float[Systems_FormationBook.DefensiveFormationCount];
        private readonly float[] _offense = new float[CALL_SLOTS];

        public bool Available => true;

        public int Version { get; private set; }

        public int DefenseEpisode { get; private set; } = -1;

        public Systems_CallSource DefenseSource { get; private set; }

        public int OffenseEpisode { get; private set; } = -1;

        public Systems_CallSource OffenseSource { get; private set; }

        public void ReportDefense(int episodeIndex, float[] chances, Systems_CallSource source)
        {
            Copy(chances, _defense);
            DefenseEpisode = episodeIndex;
            DefenseSource = source;
            Version++;
        }

        public void ReportOffense(int episodeIndex, float[] chances, Systems_CallSource source)
        {
            Copy(chances, _offense);
            OffenseEpisode = episodeIndex;
            OffenseSource = chances == null && source == Systems_CallSource.Policy
                ? Systems_CallSource.PolicyChoiceOnly
                : source;
            Version++;
        }

        public float DefenseChance(int front)
        {
            return front >= 0 && front < _defense.Length ? _defense[front] : 0f;
        }

        public float OffenseChance(Systems_PlayCall call)
        {
            int index = (int)call;
            return index >= 0 && index < _offense.Length ? _offense[index] : 0f;
        }

        private static void Copy(float[] source, float[] target)
        {
            for (int index = 0; index < target.Length; index++)
            {
                target[index] = source != null && index < source.Length ? source[index] : 0f;
            }
        }
    }

    /// <summary>Training and headless: nobody reads a call sheet, so nothing is kept.</summary>
    public sealed class Systems_NullCallSheet : Systems_ICallSheetSink, Systems_ICallSheetSource
    {
        public bool Available => false;

        public int Version => 0;

        public int DefenseEpisode => -1;

        public Systems_CallSource DefenseSource => Systems_CallSource.None;

        public int OffenseEpisode => -1;

        public Systems_CallSource OffenseSource => Systems_CallSource.None;

        public void ReportDefense(int episodeIndex, float[] chances, Systems_CallSource source)
        {
        }

        public void ReportOffense(int episodeIndex, float[] chances, Systems_CallSource source)
        {
        }

        public float DefenseChance(int front)
        {
            return 0f;
        }

        public float OffenseChance(Systems_PlayCall call)
        {
            return 0f;
        }
    }
}

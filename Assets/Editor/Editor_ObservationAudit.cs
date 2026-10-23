using System;
using System.Globalization;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoFootball.EditorTools
{
    /// <summary>
    /// Records what every brain actually observes, for Tools/audit_observations.py
    /// to audit: which floats sit at their clamp, which never move, and which pairs
    /// carry the same information twice.
    ///
    /// WHY THIS EXISTS. Every observation is clamped to [-1, 1] (Sensor_FootballState,
    /// normalize: false in the config), and a float that is pinned at its clamp most
    /// of the time is a float the network cannot read — relative distances are cut
    /// off at OBSERVATION_RANGE, 40 m. Revision 12 found two sideline distances that
    /// were the same coordinate three times; nothing looked for that again.
    ///
    /// HOW. Plays SCN_TRAIN_FOOTBALL — the training distribution, every kind of snap
    /// — at 8x with no trainer, so every player runs its heuristic, and puts an
    /// ML-Agents DemonstrationRecorder on every Agent, the defensive coordinator
    /// included. The .demo files are ML-Agents' own format, so the audit reads the
    /// rays as well as the vector, and nothing in PoFootball.Agents had to change
    /// to be watched. The recording stops itself after <see cref="DEFAULT_DECISIONS"/>
    /// quarterback decisions (about a hundred plays).
    ///
    /// A HEURISTIC'S DISTRIBUTION, NOT A POLICY'S. Saturation and constancy are
    /// properties of the field geometry and mostly hold for both; a correlation
    /// between two floats can be a habit of the script. The audit says so.
    ///
    /// Headless: `Unity.exe -batchmode -projectPath . -executeMethod
    /// PoFootball.EditorTools.Editor_ObservationAudit.RunBatch [-auditDecisions N]`
    /// without -quit; it exits when the recording is written. Never while a training
    /// run is using the machine (CLAUDE.md: close the Editor during long runs).
    /// </summary>
    [InitializeOnLoad]
    internal static class Editor_ObservationAudit
    {
        private const string TRAIN_SCENE_PATH = "Assets/Scenes/SCN_TRAIN_FOOTBALL.unity";

        /// <summary>Quarterback decisions to record: ten a second, ~100 plays.</summary>
        private const int DEFAULT_DECISIONS = 6000;

        private const float SIM_SPEED = 8f;

        /// <summary>Real seconds between scans for agents that have no recorder yet.</summary>
        private const double ATTACH_INTERVAL_SECONDS = 0.5;

        private const string QUARTERBACK_BEHAVIOR = "Quarterback";

        private const string KEY_ACTIVE = "PoFootball.ObsAudit.Active";
        private const string KEY_BATCH = "PoFootball.ObsAudit.Batch";
        private const string KEY_DIRECTORY = "PoFootball.ObsAudit.Directory";
        private const string KEY_DECISIONS = "PoFootball.ObsAudit.Decisions";

        private static double _lastAttach;

        static Editor_ObservationAudit()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += AttachRecorders;
        }

        private static bool IsActive => SessionState.GetBool(KEY_ACTIVE, false);

        [MenuItem("Tools/PoFootball/Audit/Record Observations (training distribution)")]
        private static void RunFromMenu()
        {
            Begin(false, DEFAULT_DECISIONS);
        }

        /// <summary>Entry point for -executeMethod. Leaves the Editor running; it exits when done.</summary>
        public static void RunBatch()
        {
            int decisions = DEFAULT_DECISIONS;
            string[] arguments = Environment.GetCommandLineArgs();

            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == "-auditDecisions"
                    && int.TryParse(arguments[index + 1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int parsed)
                    && parsed > 0)
                {
                    decisions = parsed;
                }
            }

            Begin(true, decisions);
        }

        private static void Begin(bool batch, int decisions)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[PoFootball] Observation audit: leave play mode first.");
                return;
            }

            if (!batch && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string directory = Path.Combine(
                Directory.GetCurrentDirectory(), "results", "audit", "demos",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);

            SessionState.SetBool(KEY_ACTIVE, true);
            SessionState.SetBool(KEY_BATCH, batch);
            SessionState.SetString(KEY_DIRECTORY, directory);
            SessionState.SetInt(KEY_DECISIONS, decisions);

            EditorSceneManager.OpenScene(TRAIN_SCENE_PATH);

            Debug.Log(
                $"[PoFootball] Observation audit: recording {decisions} quarterback "
                + $"decisions at {SIM_SPEED:F0}x into {directory}.");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!IsActive)
            {
                return;
            }

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += () => Time.timeScale = SIM_SPEED;
                return;
            }

            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            // The recorders closed their files in OnDestroy, on the way out.
            string directory = SessionState.GetString(KEY_DIRECTORY, string.Empty);
            bool batch = SessionState.GetBool(KEY_BATCH, false);

            SessionState.EraseBool(KEY_ACTIVE);
            SessionState.EraseBool(KEY_BATCH);
            SessionState.EraseString(KEY_DIRECTORY);
            SessionState.EraseInt(KEY_DECISIONS);

            int files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.demo").Length
                : 0;

            Debug.Log(
                $"[PoFootball] Observation audit: {files} recordings in {directory}. Audit them "
                + "with .venv\\Scripts\\python.exe Tools\\audit_observations.py --demos \"" + directory + "\"");

            if (batch)
            {
                EditorApplication.Exit(files > 0 ? 0 : 1);
            }
        }

        /// <summary>
        /// Every Agent in play gets one recorder, as soon as it exists — the
        /// coordinator is created in the middle linebacker's Start, a frame or two
        /// after the players. The first quarterback's recorder carries the stopping
        /// count; DemonstrationRecorder ends play mode itself when it is reached.
        /// </summary>
        private static void AttachRecorders()
        {
            if (!IsActive || !EditorApplication.isPlaying)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _lastAttach < ATTACH_INTERVAL_SECONDS)
            {
                return;
            }

            _lastAttach = EditorApplication.timeSinceStartup;

            string directory = SessionState.GetString(KEY_DIRECTORY, string.Empty);
            bool hasStopper = false;

            Agent[] agents = UnityEngine.Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);

            for (int agentIndex = 0; agentIndex < agents.Length; agentIndex++)
            {
                if (agents[agentIndex].TryGetComponent(out DemonstrationRecorder existing))
                {
                    hasStopper |= existing.NumStepsToRecord > 0;
                }
            }

            for (int agentIndex = 0; agentIndex < agents.Length; agentIndex++)
            {
                Agent agent = agents[agentIndex];

                if (agent.TryGetComponent(out DemonstrationRecorder _))
                {
                    continue;
                }

                string behavior = agent.TryGetComponent(out BehaviorParameters parameters)
                    ? parameters.BehaviorName
                    : agent.GetType().Name;

                DemonstrationRecorder recorder = agent.gameObject.AddComponent<DemonstrationRecorder>();
                recorder.DemonstrationName = behavior;
                recorder.DemonstrationDirectory = directory;

                if (!hasStopper && behavior == QUARTERBACK_BEHAVIOR)
                {
                    recorder.NumStepsToRecord = SessionState.GetInt(KEY_DECISIONS, DEFAULT_DECISIONS);
                    hasStopper = true;
                }

                recorder.Record = true;
            }
        }
    }
}

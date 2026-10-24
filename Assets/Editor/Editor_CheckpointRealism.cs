using System;
using System.IO;
using PoFootball.Agents;
using PoFootball.Models;
using UnityEditor;
using UnityEngine;

namespace PoFootball.EditorTools
{
    /// <summary>
    /// Plays the four fixed-seed realism games with a TRAINING CHECKPOINT's brains,
    /// without promoting it. Driven by Tools/checkpoint_realism.py.
    ///
    /// WHY THIS EXISTS. A run was judged on the realism line exactly once, after it
    /// was promoted — football_base22 trained for three million steps, was promoted
    /// as Football_v03 and then failed all five bands, and nothing said so earlier
    /// because nothing could. mlagents-learn already writes an .onnx at every
    /// checkpoint; this puts one set of them on the field the way a promoted table
    /// would be, so a run's milestones are "inside the bands" rather than a step
    /// count, and the checkpoint to promote is the one that plays football rather
    /// than the last one written.
    ///
    /// HOW. The four `{group}.onnx` files in -checkpointDir are copied to
    /// <see cref="STAGING_FOLDER"/>, imported, and listed in a throwaway table built
    /// exactly as Build Brain Table builds the real one (same revision stamp, same
    /// group check). Agent_BrainRegistry loads that table instead of Resources for
    /// this one evaluation, and Editor_RealismEval clears the override when it ends.
    /// The promoted table and Assets/Agents/Football_vNN are never touched. The
    /// staging folder is git-ignored and overwritten by the next checkpoint.
    ///
    /// `Unity.exe -batchmode -projectPath . -executeMethod
    /// PoFootball.EditorTools.Editor_CheckpointRealism.RunBatch -checkpointDir DIR
    /// -checkpointLabel run@step` without -quit. A checkpoint from another contract
    /// revision is refused by the table's own stamp check, and the report then says
    /// brains_loaded: false, which the Python side treats as a failure to evaluate.
    /// </summary>
    internal static class Editor_CheckpointRealism
    {
        private const string STAGING_FOLDER = "Assets/Agents/_Checkpoint";
        private const string STAGING_TABLE = STAGING_FOLDER + "/CheckpointBrains.asset";

        /// <summary>Entry point for -executeMethod. Leaves the Editor running; it exits when done.</summary>
        public static void RunBatch()
        {
            string source = Argument("-checkpointDir");
            string label = Argument("-checkpointLabel") ?? "checkpoint";

            if (source == null || !Directory.Exists(source))
            {
                Debug.LogError($"[PoFootball] Checkpoint realism: -checkpointDir '{source}' does not exist.");
                EditorApplication.Exit(2);
                return;
            }

            if (!Stage(source))
            {
                EditorApplication.Exit(2);
                return;
            }

            SessionState.SetString(Agent_BrainTable.EDITOR_OVERRIDE_SESSION_KEY, STAGING_TABLE);
            SessionState.SetString(Editor_RealismEval.KEY_BRAIN_LABEL, label);

            Debug.Log($"[PoFootball] Checkpoint realism: {label} staged from {source}.");
            Editor_RealismEval.RunBatch();
        }

        /// <summary>
        /// The same staging as <see cref="RunBatch"/>, in the Editor that is already
        /// open: Tools/checkpoint_realism.py --in-editor calls it through the Unity
        /// CLI's eval. A second, batch-mode Unity cannot open a project the Editor
        /// holds — it exits 1 within two seconds (2026-10-24, every checkpoint of a
        /// sweep) — so with the Editor open this is the only way in. Returns what
        /// happened, for the caller to print.
        /// </summary>
        public static string RunInEditor(string source, string label)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "refused: leave play mode first";
            }

            if (!Directory.Exists(source))
            {
                return $"refused: no directory {source}";
            }

            if (!Stage(source))
            {
                return "refused: staging failed (see the Console)";
            }

            SessionState.SetString(Agent_BrainTable.EDITOR_OVERRIDE_SESSION_KEY, STAGING_TABLE);
            SessionState.SetString(Editor_RealismEval.KEY_BRAIN_LABEL, label);

            Debug.Log($"[PoFootball] Checkpoint realism: {label} staged from {source} (in the open Editor).");
            Editor_RealismEval.RunUnattended();
            return "started";
        }

        /// <summary>
        /// Copies every group's .onnx into the staging folder, imports them and writes
        /// the table. False, with the reason logged, if any group is missing.
        /// </summary>
        private static bool Stage(string source)
        {
            Directory.CreateDirectory(STAGING_FOLDER);

            Array groups = Enum.GetValues(typeof(Systems_BrainGroup));

            for (int index = 0; index < groups.Length; index++)
            {
                string name = $"{(Systems_BrainGroup)groups.GetValue(index)}.onnx";
                string from = Path.Combine(source, name);

                if (!File.Exists(from))
                {
                    Debug.LogError($"[PoFootball] Checkpoint realism: no {name} in {source}.");
                    return false;
                }

                // Overwritten in place, so the .meta and its GUID survive from one
                // checkpoint to the next, as CLAUDE.md asks of every .onnx.
                File.Copy(from, $"{STAGING_FOLDER}/{name}", true);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            return Editor_BrainTableBuilder.WriteTable(STAGING_FOLDER, STAGING_TABLE);
        }

        private static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}

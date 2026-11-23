using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace PoFootball.Agents
{
    /// <summary>
    /// One console line per brain, the first time its promoted .onnx answers a
    /// decision in this run:
    ///
    ///     [PoFootball] Offense: Offense.onnx inference running (Default, 50 vector obs)
    ///
    /// Proof that the trained policy, not Heuristic, is playing. Debug.Log on
    /// purpose rather than Systems_Log.Info: a release WebGL player strips Info,
    /// and this line is what the browser console of the published build is read
    /// for. Four lines a run at most (one per Systems_BrainGroup).
    ///
    /// Nothing is said for a body without a model (the heuristic plays it) or
    /// while a trainer is connected (the trainer acts, not the .onnx).
    /// </summary>
    internal static class Agent_InferenceLog
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>Call once per agent, from its first OnActionReceived.</summary>
        internal static void OnFirstAction(BehaviorParameters parameters)
        {
            if (parameters == null || parameters.Model == null || parameters.IsInHeuristicMode())
            {
                return;
            }

            if (Academy.IsInitialized && Academy.Instance.IsCommunicatorOn)
            {
                return;
            }

            if (!Reported.Add(parameters.BehaviorName))
            {
                return;
            }

            Debug.Log(Line(
                parameters.BehaviorName,
                parameters.Model.name,
                parameters.InferenceDevice.ToString(),
                parameters.BrainParameters.VectorObservationSize));
        }

        /// <summary>The line itself, separate so a test can pin its wording.</summary>
        internal static string Line(string brain, string model, string device, int vectorObservations)
        {
            return $"[PoFootball] {brain}: {model}.onnx inference running ({device}, {vectorObservations} vector obs)";
        }

        /// <summary>Cleared per play session, so entering play mode again reports again.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Reported.Clear();
        }
    }
}

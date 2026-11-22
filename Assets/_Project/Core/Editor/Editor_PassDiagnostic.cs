using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PoFootball.Agents;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEditor;
using UnityEngine;

namespace PoFootball.EditorTools
{
    /// <summary>
    /// Why does a pass call so rarely end in a throw? Samples the quarterback on every
    /// Editor frame of play mode and writes one row per scrimmage pass call to
    /// results/audit/pass-diag-&lt;stamp&gt;.csv: his depth when the call latched, the
    /// tick he crossed the line holding the ball, how many sampled frames the release
    /// was legal, how many of those the brain pressed it, and whether a pass left.
    ///
    /// Arm it (Tools/PoFootball/Audit/Pass Diagnostic) and play: it disarms itself on
    /// leaving play mode. Sampling is per frame, not per tick — at 8x a frame spans
    /// several ticks, so the counts are samples, not durations.
    /// </summary>
    [InitializeOnLoad]
    internal static class Editor_PassDiagnostic
    {
        private const string ARMED_KEY = "PoFootball.PassDiagnostic.Armed";
        private const int THROW_BRANCH = 1;
        private const int THROW_RELEASE = 1;

        private static readonly FieldInfo PlayField = typeof(Agent_FootballPlayer)
            .GetField("_play", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BallField = typeof(Agent_FootballPlayer)
            .GetField("_ball", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo QuarterbackField = typeof(Agent_FootballPlayer)
            .GetField("_hasQuarterbackActions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo CanReleaseMethod = typeof(Agent_FootballPlayer)
            .GetMethod("CanReleasePass", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly List<string> Rows = new List<string>();
        private static Row _current;
        private static Agent_FootballPlayer[] _quarterbacks;

        private sealed class Row
        {
            public Systems_PlayModel Play;
            public Agent_FootballPlayer Passer;
            public int LastTick;
            public Systems_PlayCall Call;
            public float DepthAtLatch = float.NaN;
            public int LatchTick = -1;
            public int CrossedTick = -1;
            public int LostBallTick = -1;
            public int LegalFrames;
            public int PressedWhileLegal;
            public int PressedWhileIllegal;
            public int ThrownTick = -1;
            public bool Completed;
            public float MaxDepth;
        }

        static Editor_PassDiagnostic()
        {
            EditorApplication.update += Sample;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/PoFootball/Audit/Pass Diagnostic (arm for next play session)")]
        private static void Arm()
        {
            SessionState.SetBool(ARMED_KEY, true);
            Debug.Log("[PoFootball] Pass diagnostic armed.");
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                Rows.Clear();
                _current = null;
                _quarterbacks = null;
            }

            if (change == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(ARMED_KEY, false))
            {
                Close();
                Write();
                SessionState.SetBool(ARMED_KEY, false);
            }
        }

        private static void Sample()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(ARMED_KEY, false))
            {
                return;
            }

            // The realism eval reloads the scene between games: drop destroyed agents.
            if (_quarterbacks != null && _quarterbacks.Length > 0 && _quarterbacks[0] == null)
            {
                _quarterbacks = null;
                _current = null;
            }

            if (_quarterbacks == null || _quarterbacks.Length == 0)
            {
                Agent_FootballPlayer[] all = Object.FindObjectsByType<Agent_FootballPlayer>();
                var found = new List<Agent_FootballPlayer>();
                for (int index = 0; index < all.Length; index++)
                {
                    if ((bool)QuarterbackField.GetValue(all[index]))
                    {
                        found.Add(all[index]);
                    }
                }

                _quarterbacks = found.ToArray();
                if (_quarterbacks.Length == 0)
                {
                    return;
                }
            }

            var play = (Systems_PlayModel)PlayField.GetValue(_quarterbacks[0]);
            if (play == null)
            {
                return;
            }

            if (_current != null && (play.PhysicsTick < _current.LastTick || play.Phase == Systems_PlayPhase.PreSnap))
            {
                Close();
            }

            if (play.Phase != Systems_PlayPhase.Live || play.Kind != Systems_PlayKind.Scrimmage)
            {
                if (_current != null)
                {
                    _current.Completed |= play.PassCompleted;
                    _current.ThrownTick = _current.ThrownTick < 0 && play.ForwardPassThrown ? _current.LastTick : _current.ThrownTick;
                }

                return;
            }

            if (_current == null)
            {
                Agent_FootballPlayer passer = null;
                for (int index = 0; index < _quarterbacks.Length; index++)
                {
                    if (_quarterbacks[index].IsCarrier)
                    {
                        passer = _quarterbacks[index];
                    }
                }

                if (passer == null)
                {
                    return;
                }

                _current = new Row { Play = play, Passer = passer };
            }

            Row row = _current;
            Agent_FootballPlayer qb = row.Passer;
            row.LastTick = play.PhysicsTick;
            float depth = play.LineOfScrimmageY - qb.Position.y;
            row.MaxDepth = Mathf.Max(row.MaxDepth, depth);

            if (play.CallIsLatched && row.LatchTick < 0)
            {
                row.Call = play.Call;
                row.LatchTick = play.PhysicsTick;
                row.DepthAtLatch = depth;
            }

            if (play.ForwardPassThrown && row.ThrownTick < 0)
            {
                row.ThrownTick = play.PhysicsTick;
            }

            row.Completed |= play.PassCompleted;

            if (qb.IsCarrier && depth < 0f && row.CrossedTick < 0)
            {
                row.CrossedTick = play.PhysicsTick;
            }

            if (!qb.IsCarrier && row.LostBallTick < 0 && !play.ForwardPassThrown)
            {
                row.LostBallTick = play.PhysicsTick;
            }

            bool pressed = qb.GetStoredActionBuffers().DiscreteActions.Length > THROW_BRANCH
                && qb.GetStoredActionBuffers().DiscreteActions[THROW_BRANCH] == THROW_RELEASE;
            bool legal = (bool)CanReleaseMethod.Invoke(qb, null);

            if (legal)
            {
                row.LegalFrames++;
                if (pressed)
                {
                    row.PressedWhileLegal++;
                }
            }
            else if (pressed && play.Call.IsPass())
            {
                row.PressedWhileIllegal++;
            }
        }

        private static void Close()
        {
            Row row = _current;
            _current = null;
            if (row == null || row.LatchTick < 0)
            {
                return;
            }

            Rows.Add(string.Join(",",
                row.Call, row.Call.IsPass() ? 1 : 0, row.LatchTick, row.DepthAtLatch.ToString("F2"),
                row.MaxDepth.ToString("F2"), row.CrossedTick, row.LostBallTick, row.LegalFrames,
                row.PressedWhileLegal, row.PressedWhileIllegal, row.ThrownTick, row.Completed ? 1 : 0,
                row.LastTick));
        }

        private static void Write()
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "results", "audit");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"pass-diag-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var text = new StringBuilder();
            text.AppendLine("call,isPass,latchTick,depthAtLatch,maxDepth,crossedTick,lostBallTick,legalFrames,pressedWhileLegal,pressedWhileIllegal,thrownTick,completed,lastTick");
            for (int index = 0; index < Rows.Count; index++)
            {
                text.AppendLine(Rows[index]);
            }

            File.WriteAllText(path, text.ToString());
            Debug.Log($"[PoFootball] Pass diagnostic: {Rows.Count} plays -> {path}");
        }
    }
}

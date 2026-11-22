using System.Diagnostics;
using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// What one physics tick's scripts cost, for the TAB overlay's "inference"
    /// line: from before every other FixedUpdate (this, at the earliest execution
    /// order) to after them all (Systems_TickTimerEnd, at the latest). In a game
    /// scene that span is the ML-Agents academy step — 22 players observing,
    /// deciding (model inference or the heuristic) and applying the action — and
    /// nothing else of any size; Physics2D runs after it and is not counted.
    ///
    /// A Stopwatch rather than a ProfilerRecorder because it reads the same in a
    /// release player, where profiler markers are not recorded.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    internal sealed class Systems_TickTimer : MonoBehaviour
    {
        /// <summary>Weight of the newest tick in the running mean (about a second of ticks).</summary>
        private const float SMOOTHING = 0.02f;

        private long _start;
        private bool _open;

        /// <summary>Running mean of the tick's script time, in milliseconds.</summary>
        internal float MeanMs { get; private set; }

        internal int Ticks { get; private set; }

        private void FixedUpdate()
        {
            _start = Stopwatch.GetTimestamp();
            _open = true;
        }

        /// <summary>Called by Systems_TickTimerEnd after every other FixedUpdate of the tick.</summary>
        internal void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            float ms = (Stopwatch.GetTimestamp() - _start) * 1000f / Stopwatch.Frequency;
            MeanMs = Ticks == 0 ? ms : Mathf.Lerp(MeanMs, ms, SMOOTHING);
            Ticks++;
        }
    }
}

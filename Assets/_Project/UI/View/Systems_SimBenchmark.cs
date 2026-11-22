using System.Globalization;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// The TAB overlay's "benchmark training speed" button: runs this game's own
    /// simulation — all 22 players observing, deciding and moving, the referee,
    /// Physics2D — as fast as this device can for a few real seconds, and counts
    /// the physics ticks it got through.
    ///
    /// WHAT IT IS AND IS NOT. It is the environment side of training: the same
    /// fixed 0.02 s tick a headless env steps, sped up through
    /// Systems_SimSpeedSystem.BeginBenchmark (the time scale only; fixed delta
    /// time is untouched). It is NOT a training run — no trainer, no gradient
    /// step — so the result is labelled as local simulation speed. The game
    /// visibly fast-forwards while it runs, then returns to the viewer's speed.
    ///
    /// Added to the status HUD's host by Systems_StatusHudBootstrap; injected in a
    /// game scene only. On the menu there is no simulation to time.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Systems_SimBenchmark : MonoBehaviour
    {
        private Systems_SimSpeedSystem _speed;
        private Systems_ShowcaseSettings _settings;

        private bool _running;
        private int _ticks;
        private float _realElapsed;
        private float _savedMaximumDeltaTime;

        /// <summary>The last result, in words; empty before the first run.</summary>
        internal string Result { get; private set; } = string.Empty;

        /// <summary>The last result's physics ticks per real second; 0 before the first.</summary>
        internal float TicksPerSecond { get; private set; }

        internal bool IsRunning => _running;

        internal bool HasSimulation => _speed != null;

        [Inject]
        public void Construct(Systems_SimSpeedSystem speed)
        {
            _speed = speed;
        }

        private void Awake()
        {
            _settings = Systems_ShowcaseSettings.Load();
        }

        private void OnDisable()
        {
            Stop(false);
        }

        /// <summary>Starts a run; false (with Result saying why) where there is nothing to time.</summary>
        internal bool Begin()
        {
            if (_running)
            {
                return false;
            }

            if (_speed == null)
            {
                Result = "Benchmark: start a game first (PLAY) — the menu has no simulation to time.";
                return false;
            }

            _running = true;
            _ticks = 0;
            _realElapsed = 0f;
            _savedMaximumDeltaTime = Time.maximumDeltaTime;

            // Let one frame carry up to a second of simulation, so the count is
            // bounded by this device's CPU rather than by Unity's catch-up cap.
            Time.maximumDeltaTime = 1f;
            _speed.BeginBenchmark(_settings.BenchmarkTimeScale);
            Result = "Benchmark running…";
            return true;
        }

        private void FixedUpdate()
        {
            if (_running)
            {
                _ticks++;
            }
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            _realElapsed += Time.unscaledDeltaTime;

            if (_realElapsed >= _settings.BenchmarkSeconds)
            {
                Stop(true);
            }
        }

        private void Stop(bool report)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            Time.maximumDeltaTime = _savedMaximumDeltaTime;

            if (_speed != null)
            {
                _speed.EndBenchmark();
            }

            if (!report)
            {
                return;
            }

            TicksPerSecond = _realElapsed > 0f ? _ticks / _realElapsed : 0f;
            int players = Systems_RoleTable.SQUAD_SIZE * 2;
            float decisions = TicksPerSecond / Systems_SimConstants.DECISION_PERIOD * players;

            Result = string.Format(
                CultureInfo.InvariantCulture,
                "Benchmark ({0:0.0} s, this device, 1 game, CPU): {1:N0} physics ticks/s = {2:N0} agent decisions/s "
                    + "({3} players). Local simulation speed, not a training run.",
                _realElapsed,
                TicksPerSecond,
                decisions,
                players);
            Systems_Log.Info("[PoFootball] " + Result);
        }
    }
}

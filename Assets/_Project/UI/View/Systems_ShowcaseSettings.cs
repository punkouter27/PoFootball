using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// The attract (demo) mode and the TAB overlay's benchmark, tuned in the
    /// Inspector: Assets/_Project/Data/Resources/PoFootballShowcase.asset.
    /// Read by Systems_AttractModeView and Systems_SimBenchmark; a missing asset
    /// falls back to the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "PoFootball/Showcase Settings", fileName = "PoFootballShowcase")]
    public sealed class Systems_ShowcaseSettings : ScriptableObject
    {
        public const string RESOURCE_PATH = "PoFootballShowcase";

        public const float DEFAULT_IDLE_SECONDS = 30f;
        public const float DEFAULT_CAMERA_DWELL_SECONDS = 6f;
        public const float DEFAULT_BENCHMARK_SECONDS = 3f;
        public const float DEFAULT_BENCHMARK_TIME_SCALE = 40f;

        [Tooltip("Real seconds with no key, click or touch before demo mode starts.")]
        [SerializeField] private float _idleSeconds = DEFAULT_IDLE_SECONDS;

        [Tooltip("Real seconds each camera mode is held while the demo cycles them.")]
        [SerializeField] private float _cameraDwellSeconds = DEFAULT_CAMERA_DWELL_SECONDS;

        [Tooltip("Real seconds the TAB overlay's benchmark runs the simulation for.")]
        [SerializeField] private float _benchmarkSeconds = DEFAULT_BENCHMARK_SECONDS;

        [Tooltip("Time scale the benchmark runs the simulation at (fixed delta time is untouched).")]
        [SerializeField] private float _benchmarkTimeScale = DEFAULT_BENCHMARK_TIME_SCALE;

        [Tooltip("Shown over the field while the demo runs.")]
        [SerializeField] private string _demoCaption = "DEMO — PRESS ANY KEY OR TAP TO PLAY";

        public float IdleSeconds => _idleSeconds;
        public float CameraDwellSeconds => _cameraDwellSeconds;
        public float BenchmarkSeconds => _benchmarkSeconds;
        public float BenchmarkTimeScale => _benchmarkTimeScale;
        public string DemoCaption => _demoCaption;

        private static Systems_ShowcaseSettings _fallback;

        /// <summary>The asset, or an in-memory one with the defaults if it is missing.</summary>
        public static Systems_ShowcaseSettings Load()
        {
            Systems_ShowcaseSettings settings = Resources.Load<Systems_ShowcaseSettings>(RESOURCE_PATH);

            if (settings != null)
            {
                return settings;
            }

            if (_fallback == null)
            {
                _fallback = CreateInstance<Systems_ShowcaseSettings>();
            }

            return _fallback;
        }
    }
}

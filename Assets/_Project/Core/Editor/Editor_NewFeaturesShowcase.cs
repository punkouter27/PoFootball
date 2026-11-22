using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MessagePipe;
using PoFootball.Models;
using PoFootball.Systems;
using PoFootball.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;

namespace PoFootball.EditorTools
{
    /// <summary>
    /// Runs SCN_GAME in the Editor Game view at 1080x1920 and captures still frames
    /// and frame bursts of the new presentation/simulation features for the
    /// SCREENSHOTS/new_features_report.html showcase:
    ///   - Cinemachine live/highlight virtual camera rig (BroadcastLiveVCam / BroadcastHighlightVCam)
    ///   - Shader Graph field surround (PoFootball_FieldSurround.shadergraph)
    ///   - Burst-compiled separation rings &amp; pursuit intercept lines (Systems_SeparationJob)
    ///   - Weather precipitation &amp; stadium lighting (Systems_WeatherView)
    ///   - DEBUG sheet presentation telemetry
    /// </summary>
    [InitializeOnLoad]
    internal static class Editor_NewFeaturesShowcase
    {
        private const string MENU_PATH = "Tools/PoFootball/UI/Capture New Features Showcase";
        private const string PENDING_KEY = "PoFootball.NewFeatures.Pending";
        private const string EXIT_KEY = "PoFootball.NewFeatures.ExitOnDone";
        private const string MENU_SCENE_PATH = "Assets/_Project/Scenes/SCN_MENU.unity";

        private const uint CAPTURE_WIDTH = 1080;
        private const uint CAPTURE_HEIGHT = 1920;
        private const int BURST_FRAMES = 22;
        private const float BURST_INTERVAL_SECONDS = 0.085f;

        private static bool _running;

        [Serializable]
        private sealed class ShowcaseMetadata
        {
            public string stamp;
            public string version;
            public uint width;
            public uint height;
            public string liveVcamName;
            public string highlightVcamName;
            public string surroundShaderName;
            public bool separationBurstAllocated;
            public bool weatherUsingVfxGraph;
            public float liveOrthoSize;
            public string liveShotKind;
            public List<string> stills = new List<string>();
            public List<string> bursts = new List<string>();
        }

        static Editor_NewFeaturesShowcase()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem(MENU_PATH)]
        public static void RunFromMenu()
        {
            Begin(exitOnDone: false);
        }

        public static void RunCapture()
        {
            Begin(exitOnDone: true);
        }

        private static void Begin(bool exitOnDone)
        {
            if (_running)
            {
                Debug.LogWarning("[PoFootball] New features showcase is already running.");
                return;
            }

            SessionState.SetBool(EXIT_KEY, exitOnDone);

            if (EditorApplication.isPlaying)
            {
                RunAsync().Forget();
                return;
            }

            SessionState.SetBool(PENDING_KEY, true);
            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(MENU_SCENE_PATH);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PENDING_KEY, false))
            {
                SessionState.SetBool(PENDING_KEY, false);
                EditorSceneManager.playModeStartScene = null;
                RunAsync().Forget();
                return;
            }

            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(EXIT_KEY, false))
            {
                SessionState.SetBool(EXIT_KEY, false);
                EditorApplication.Exit(0);
            }
        }

        private static async UniTaskVoid RunAsync()
        {
            _running = true;

            string root = Path.GetDirectoryName(Application.dataPath);
            string outputDir = Path.Combine(root, "SCREENSHOTS", "new_features");
            Directory.CreateDirectory(outputDir);

            bool oldSpaceControl = Systems_BroadcastSettings.SpaceControl;
            bool oldPursuit = Systems_BroadcastSettings.Pursuit;
            bool oldAutoDirector = Systems_BroadcastSettings.AutoDirector;
            bool oldFatigue = Systems_BroadcastSettings.FatigueRings;

            Systems_BroadcastSettings.SpaceControl = true;
            Systems_BroadcastSettings.Pursuit = true;
            Systems_BroadcastSettings.AutoDirector = true;
            Systems_BroadcastSettings.FatigueRings = true;

            PlayModeWindow.GetRenderingResolution(out uint oldWidth, out uint oldHeight);
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
            PlayModeWindow.SetCustomRenderingResolution(
                CAPTURE_WIDTH, CAPTURE_HEIGHT, "PoFootball Showcase 9x16");

            var metadata = new ShowcaseMetadata
            {
                stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                version = Application.version,
                width = CAPTURE_WIDTH,
                height = CAPTURE_HEIGHT,
            };

            try
            {
                Systems_SceneRouter.LoadGame();
                await UniTask.WaitUntil(() => SceneManager.GetActiveScene().name == Systems_SceneRouter.GAME_SCENE);
                await Seconds(1.2f);

                Systems_GameLifetimeScope scope =
                    UnityEngine.Object.FindAnyObjectByType<Systems_GameLifetimeScope>();
                Systems_PlayModel play = scope.Container.Resolve<Systems_PlayModel>();
                Systems_BallModel ball = scope.Container.Resolve<Systems_BallModel>();
                Systems_WeatherModel weather = scope.Container.Resolve<Systems_WeatherModel>();

                Systems_BroadcastCameraView camView =
                    UnityEngine.Object.FindAnyObjectByType<Systems_BroadcastCameraView>();
                Systems_FieldRenderer fieldRenderer =
                    UnityEngine.Object.FindAnyObjectByType<Systems_FieldRenderer>();
                Systems_SeparationView separationView =
                    UnityEngine.Object.FindAnyObjectByType<Systems_SeparationView>();
                Systems_WeatherView weatherView =
                    UnityEngine.Object.FindAnyObjectByType<Systems_WeatherView>();

                metadata.surroundShaderName = fieldRenderer != null ? fieldRenderer.SurroundShaderName : "none";
                metadata.separationBurstAllocated = separationView != null && separationView.IsBurstJobAllocated;
                metadata.weatherUsingVfxGraph = weatherView != null && weatherView.IsUsingVfxGraph;

                // 1. Live pass play: separation rings/gauges + Cinemachine BroadcastLiveVCam.
                await WaitForPlayCondition(() =>
                    play.Phase == Systems_PlayPhase.Live
                    && play.Call.IsPass()
                    && !ball.IsInFlight,
                    timeoutSeconds: 18f);

                Time.timeScale = 1f;
                await Seconds(0.25f);

                metadata.liveVcamName = camView != null ? camView.ActiveCinemachineName : "none";
                metadata.liveOrthoSize = Camera.main != null ? Camera.main.orthographicSize : 0f;
                metadata.liveShotKind = camView != null ? camView.CurrentShot.ToString() : "none";

                await CaptureStill(outputDir, "01_separation_cinemachine.png", metadata);
                await CaptureBurst(outputDir, "burst_dropback", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);

                // 2. Live run / open-field pursuit: Burst pursuit intercept lines + wide framing + surround.
                await WaitForPlayCondition(() =>
                    play.Phase == Systems_PlayPhase.Live
                    && ball.IsHeld
                    && (!play.Call.IsPass() || ball.Position.y > play.LineOfScrimmageY + 2f),
                    timeoutSeconds: 18f);

                Time.timeScale = 1f;
                await Seconds(0.2f);
                await CaptureStill(outputDir, "02_pursuit_surround.png", metadata);
                await CaptureBurst(outputDir, "burst_pursuit", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);

                // 3. Night rain weather showcase (Systems_WeatherView + stadium floodlights).
                weather.SetConditions(Systems_WeatherCondition.Rain, 10f, 26f, 315f, 0.92f, false, false);
                await Seconds(0.8f);

                await WaitForPlayCondition(() => play.Phase == Systems_PlayPhase.Live, timeoutSeconds: 15f);
                Time.timeScale = 1f;
                await Seconds(0.35f);
                await CaptureStill(outputDir, "03_weather_night_rain.png", metadata);
                await CaptureBurst(outputDir, "burst_weather", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);

                // 4. DEBUG sheet telemetry over live gameplay.
                if (PressButton("DEBUG"))
                {
                    await Seconds(0.5f);
                    await CaptureStill(outputDir, "04_debug_telemetry.png", metadata);
                    PressButton("DEBUG");
                    await Seconds(0.25f);
                }

                // 5. Post-game highlight reel: Cinemachine cross-camera blend to BroadcastHighlightVCam.
                scope.Container
                    .Resolve<IPublisher<Systems_GameOverMessage>>()
                    .Publish(new Systems_GameOverMessage(21, 14));
                Time.timeScale = 0f;
                await Seconds(1.0f);

                if (PressButton("HIGHLIGHTS"))
                {
                    await Seconds(0.6f);
                    metadata.highlightVcamName = camView != null ? camView.ActiveCinemachineName : "none";
                    await CaptureStill(outputDir, "05_highlight_vcam.png", metadata);
                    await CaptureBurst(outputDir, "burst_highlight", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);
                }

                File.WriteAllText(
                    Path.Combine(outputDir, "capture_metadata.json"),
                    JsonUtility.ToJson(metadata, true));

                Debug.Log($"[PoFootball] New features showcase captured to {outputDir}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PoFootball] New features showcase failed: " + ex);
            }
            finally
            {
                Time.timeScale = 1f;
                Systems_BroadcastSettings.SpaceControl = oldSpaceControl;
                Systems_BroadcastSettings.Pursuit = oldPursuit;
                Systems_BroadcastSettings.AutoDirector = oldAutoDirector;
                Systems_BroadcastSettings.FatigueRings = oldFatigue;

                if (oldWidth > 0 && oldHeight > 0)
                {
                    PlayModeWindow.SetCustomRenderingResolution(oldWidth, oldHeight, "Restored");
                }

                _running = false;
                EditorApplication.isPlaying = false;
            }
        }

        private static async UniTask WaitForPlayCondition(Func<bool> predicate, float timeoutSeconds)
        {
            Time.timeScale = 4f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (predicate())
                {
                    return;
                }

                await UniTask.Yield();
            }
        }

        private static async UniTask CaptureStill(string dir, string fileName, ShowcaseMetadata metadata)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, fileName));
            await Seconds(0.45f);
            metadata.stills.Add(fileName);
        }

        private static async UniTask CaptureBurst(
            string dir,
            string prefix,
            int frameCount,
            float intervalSeconds,
            ShowcaseMetadata metadata)
        {
            for (int index = 0; index < frameCount; index++)
            {
                string fileName = $"{prefix}_{index:D2}.png";
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, fileName));
                await Seconds(intervalSeconds);
            }

            await Seconds(0.5f);
            metadata.bursts.Add(prefix);
        }

        private static bool PressButton(string text)
        {
            UIDocument[] documents =
                UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);

            for (int index = 0; index < documents.Length; index++)
            {
                VisualElement root = documents[index].rootVisualElement;

                if (root == null)
                {
                    continue;
                }

                List<Button> buttons = root.Query<Button>().ToList();

                for (int button = 0; button < buttons.Count; button++)
                {
                    Button candidate = buttons[button];

                    if (candidate.text != text
                        || candidate.resolvedStyle.display == DisplayStyle.None
                        || candidate.resolvedStyle.visibility == Visibility.Hidden)
                    {
                        continue;
                    }

                    using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                    {
                        submit.target = candidate;
                        candidate.SendEvent(submit);
                    }

                    return true;
                }
            }

            return false;
        }

        private static UniTask Seconds(float seconds)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime);
        }
    }
}

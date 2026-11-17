using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MessagePipe;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// Opt-in runtime capture harness for the Windows standalone player, active
    /// ONLY when launched with <c>--showcase-capture=&lt;dir&gt;</c>. Captures still
    /// frames and rapid frame bursts across live gameplay for the new-features HTML
    /// report, then quits cleanly. Zero cost in normal play or training.
    /// </summary>
    internal sealed class Systems_ShowcaseCaptureRunner : MonoBehaviour
    {
        private const int CAPTURE_WIDTH = 540;
        private const int CAPTURE_HEIGHT = 960;
        private const int BURST_FRAMES = 22;
        private const float BURST_INTERVAL_SECONDS = 0.085f;

        [Serializable]
        private sealed class ShowcaseMetadata
        {
            public string stamp;
            public string version;
            public int width;
            public int height;
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

        private string _outputDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryInstall()
        {
            string dir = ReadCaptureDirectoryArg();

            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            if (FindAnyObjectByType<Systems_ShowcaseCaptureRunner>() != null)
            {
                return;
            }

            var host = new GameObject("ShowcaseCaptureRunner");
            DontDestroyOnLoad(host);
            var runner = host.AddComponent<Systems_ShowcaseCaptureRunner>();
            runner._outputDir = dir;
        }

        private static string ReadCaptureDirectoryArg()
        {
            const string prefix = "--showcase-capture=";
            string[] args = Environment.GetCommandLineArgs();

            for (int index = 0; index < args.Length; index++)
            {
                if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index].Substring(prefix.Length).Trim('"');
                }
            }

            return null;
        }

        private void Start()
        {
            RunAsync().Forget();
        }

        private async UniTaskVoid RunAsync()
        {
            Directory.CreateDirectory(_outputDir);
            Screen.SetResolution(CAPTURE_WIDTH, CAPTURE_HEIGHT, FullScreenMode.Windowed);

            bool oldSpaceControl = Systems_BroadcastSettings.SpaceControl;
            bool oldPursuit = Systems_BroadcastSettings.Pursuit;
            bool oldAutoDirector = Systems_BroadcastSettings.AutoDirector;
            bool oldFatigue = Systems_BroadcastSettings.FatigueRings;

            Systems_BroadcastSettings.SpaceControl = true;
            Systems_BroadcastSettings.Pursuit = true;
            Systems_BroadcastSettings.AutoDirector = true;
            Systems_BroadcastSettings.FatigueRings = true;

            var metadata = new ShowcaseMetadata
            {
                stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                version = Application.version,
                width = CAPTURE_WIDTH,
                height = CAPTURE_HEIGHT,
            };

            try
            {
                await Seconds(1.0f);

                if (SceneManager.GetActiveScene().name != Systems_SceneRouter.GAME_SCENE)
                {
                    Systems_SceneRouter.LoadGame();
                    await UniTask.WaitUntil(() =>
                        SceneManager.GetActiveScene().name == Systems_SceneRouter.GAME_SCENE);
                    await Seconds(1.2f);
                }

                Systems_GameLifetimeScope scope = FindAnyObjectByType<Systems_GameLifetimeScope>();
                Systems_PlayModel play = scope.Container.Resolve<Systems_PlayModel>();
                Systems_BallModel ball = scope.Container.Resolve<Systems_BallModel>();
                Systems_WeatherModel weather = scope.Container.Resolve<Systems_WeatherModel>();

                Systems_BroadcastCameraView camView = FindAnyObjectByType<Systems_BroadcastCameraView>();
                Systems_FieldRenderer fieldRenderer = FindAnyObjectByType<Systems_FieldRenderer>();
                Systems_SeparationView separationView = FindAnyObjectByType<Systems_SeparationView>();
                Systems_WeatherView weatherView = FindAnyObjectByType<Systems_WeatherView>();

                metadata.surroundShaderName = fieldRenderer != null ? fieldRenderer.SurroundShaderName : "none";
                metadata.separationBurstAllocated = separationView != null && separationView.IsBurstJobAllocated;
                metadata.weatherUsingVfxGraph = weatherView != null && weatherView.IsUsingVfxGraph;

                // 1. Live pass play: Burst separation rings + Cinemachine BroadcastLiveVCam.
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

                await CaptureStill("01_separation_cinemachine.png", metadata);
                await CaptureBurst("burst_dropback", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);

                // 2. Live run / open-field pursuit: Burst pursuit lines + wide framing + ShaderGraph surround.
                await WaitForPlayCondition(() =>
                    play.Phase == Systems_PlayPhase.Live
                    && ball.IsHeld
                    && (!play.Call.IsPass() || ball.Position.y > play.LineOfScrimmageY + 2f),
                    timeoutSeconds: 18f);

                Time.timeScale = 1f;
                await Seconds(0.2f);
                await CaptureStill("02_pursuit_surround.png", metadata);
                await CaptureBurst("burst_pursuit", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);

                // 3. Night rain weather showcase (Systems_WeatherView + stadium floodlights).
                weather.SetConditions(Systems_WeatherCondition.Rain, 10f, 26f, 315f, 0.92f, false, false);
                await Seconds(0.8f);

                await WaitForPlayCondition(() => play.Phase == Systems_PlayPhase.Live, timeoutSeconds: 15f);
                Time.timeScale = 1f;
                await Seconds(0.35f);
                await CaptureStill("03_weather_night_rain.png", metadata);
                await CaptureBurst("burst_weather", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);

                // 4. Live DEBUG sheet telemetry.
                if (PressButton("DEBUG"))
                {
                    await Seconds(0.5f);
                    await CaptureStill("04_debug_telemetry.png", metadata);
                    PressButton("DEBUG");
                    await Seconds(0.25f);
                }

                // 5. Post-game highlight reel: Cinemachine blend to BroadcastHighlightVCam.
                scope.Container
                    .Resolve<IPublisher<Systems_GameOverMessage>>()
                    .Publish(new Systems_GameOverMessage(21, 14));
                Time.timeScale = 0f;
                await Seconds(1.0f);

                if (PressButton("HIGHLIGHTS"))
                {
                    await Seconds(0.6f);
                    metadata.highlightVcamName = camView != null ? camView.ActiveCinemachineName : "none";
                    await CaptureStill("05_highlight_vcam.png", metadata);
                    await CaptureBurst("burst_highlight", BURST_FRAMES, BURST_INTERVAL_SECONDS, metadata);
                }

                File.WriteAllText(
                    Path.Combine(_outputDir, "capture_metadata.json"),
                    JsonUtility.ToJson(metadata, true));

                Debug.Log($"[PoFootball] Showcase capture completed in {_outputDir}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PoFootball] Showcase capture failed: " + ex);
            }
            finally
            {
                Time.timeScale = 1f;
                Systems_BroadcastSettings.SpaceControl = oldSpaceControl;
                Systems_BroadcastSettings.Pursuit = oldPursuit;
                Systems_BroadcastSettings.AutoDirector = oldAutoDirector;
                Systems_BroadcastSettings.FatigueRings = oldFatigue;
                Application.Quit();
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

        private async UniTask CaptureStill(string fileName, ShowcaseMetadata metadata)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_outputDir, fileName));
            await Seconds(0.45f);
            metadata.stills.Add(fileName);
        }

        private async UniTask CaptureBurst(
            string prefix,
            int frameCount,
            float intervalSeconds,
            ShowcaseMetadata metadata)
        {
            for (int index = 0; index < frameCount; index++)
            {
                string fileName = $"{prefix}_{index:D2}.png";
                ScreenCapture.CaptureScreenshot(Path.Combine(_outputDir, fileName));
                await Seconds(intervalSeconds);
            }

            await Seconds(0.5f);
            metadata.bursts.Add(prefix);
        }

        private static bool PressButton(string text)
        {
            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);

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

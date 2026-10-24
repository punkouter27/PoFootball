using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MessagePipe;
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
    /// Plays the whole player-facing loop once and photographs every screen at
    /// every portrait ratio a phone ships with, auditing each capture's text with
    /// Systems_UiFitAudit — the same check the DEBUG sheet runs on a handset.
    ///
    ///     menu, menu + SETTINGS, live game, live game + DEBUG,
    ///     final whistle, highlights
    ///         x 16:9, 18:9, 19.5:9, 20:9, 21:9
    ///
    /// WHY A TOOL AND NOT ONLY THE TEST. Systems_AspectSweepTests fails on a
    /// finding; it cannot show anyone what a screen looks like. The UI rules ask
    /// for a before-and-after report on every UI change, and a report is only as
    /// good as the captures being the same shots on the same ratios each time —
    /// which nobody achieves by hand in the Game view.
    ///
    /// Writes SCREENSHOTS/ui_&lt;label&gt;/&lt;state&gt;_&lt;ratio&gt;.png and an audit.json
    /// beside them. The label is EditorPrefs "PoFootball.UiCaptureLabel", so a
    /// script can name a run ("before", "after") and then press the menu item.
    ///
    /// RESOLUTION THROUGH PlayModeWindow, the public API, not the GameViewSizes
    /// reflection most snippets use: it survives Editor upgrades and it puts the
    /// Game view back on the Game view if a Device Simulator tab was in front —
    /// which matters, because Simulator and Game view together report a hybrid
    /// Screen (see Systems_ScreenView's safe-area notes).
    ///
    /// The previous rendering resolution and the play-mode start scene are put
    /// back when the sweep ends, whether it finished or threw.
    /// </summary>
    [InitializeOnLoad]
    internal static class Editor_UiCaptureSweep
    {
        private const string MENU_PATH = "Tools/PoFootball/UI/Capture Screens (5 ratios)";
        private const string LABEL_PREF = "PoFootball.UiCaptureLabel";
        private const string PENDING_KEY = "PoFootball.UiCaptureSweep.Pending";
        private const string MENU_SCENE_PATH = "Assets/Scenes/SCN_MENU.unity";

        /// <summary>Real seconds a live game runs before it is photographed, so there is a play on the field and a few in the reel.</summary>
        private const float LIVE_GAME_SECONDS = 14f;

        /// <summary>Time scale while the game is left to run. Editor-only, like Editor_SimSpeed; put back to 1 before any capture.</summary>
        private const float FAST_FORWARD = 6f;

        /// <summary>Real seconds after a resolution change before the shot: the panel re-lays out and the safe-area inset is re-applied a frame later.</summary>
        private const float SETTLE_SECONDS = 0.6f;

        /// <summary>ScreenCapture writes at the end of a later frame; this is how long to give it.</summary>
        private const float WRITE_SECONDS = 0.4f;

        private static readonly Ratio[] Ratios =
        {
            new Ratio("16x9", 1080, 1920),
            new Ratio("18x9", 1080, 2160),
            new Ratio("19.5x9", 1080, 2340),
            new Ratio("20x9", 1080, 2400),
            new Ratio("21x9", 1080, 2520),
        };

        private static bool _running;

        private readonly struct Ratio
        {
            public readonly string Name;
            public readonly uint Width;
            public readonly uint Height;

            public Ratio(string name, uint width, uint height)
            {
                Name = name;
                Width = width;
                Height = height;
            }
        }

        [Serializable]
        private sealed class Capture
        {
            public string state;
            public string ratio;
            public string file;
            public int textChecked;
            public int cutOff;
            public string cutOffExample;
            public int overflowing;
            public string overflowExample;
            public int scrolling;
            public int outOfReach;
            public string outOfReachExample;
            public List<Region> regions = new List<Region>();
        }

        /// <summary>Where a named element was on the capture, in its pixels — what the report draws its boxes from.</summary>
        [Serializable]
        private sealed class Region
        {
            public string name;
            public float x;
            public float y;
            public float width;
            public float height;
        }

        /// <summary>The elements a before/after report annotates, by the names the views give them.</summary>
        private static readonly string[] RegionNames =
        {
            "ScoreRow", "FinalSheet", "FinalTabs", "FinalActions", "ScoringPane",
            "SettingsTabs", "DebugSheet", "PhysicsToggle",
        };

        [Serializable]
        private sealed class Report
        {
            public string label;
            public string stamp;
            public string version;
            public List<Capture> captures = new List<Capture>();
        }

        static Editor_UiCaptureSweep()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem(MENU_PATH)]
        private static void Start()
        {
            if (_running)
            {
                Debug.LogWarning("[PoFootball] UI capture sweep is already running.");
                return;
            }

            if (EditorApplication.isPlaying)
            {
                RunAsync().Forget();
                return;
            }

            // From the menu, whatever scene the Editor has open, and without opening
            // SCN_MENU in the Editor — a dirty scene would raise a save dialog, and a
            // modal dialog freezes every automation bridge with no error.
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
            }
        }

        private static async UniTaskVoid RunAsync()
        {
            _running = true;

            string label = EditorPrefs.GetString(LABEL_PREF, string.Empty);

            if (string.IsNullOrEmpty(label))
            {
                label = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            }

            string directory = Path.Combine(
                Path.GetDirectoryName(Application.dataPath), "SCREENSHOTS", "ui_" + label);
            Directory.CreateDirectory(directory);

            Report report = new Report
            {
                label = label,
                stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                version = Application.version,
            };

            PlayModeWindow.GetRenderingResolution(out uint oldWidth, out uint oldHeight);
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);

            try
            {
                Systems_SceneRouter.LoadMenu();
                await WaitForScene(Systems_SceneRouter.MENU_SCENE);
                await Shoot(directory, "1_menu", report);

                Press("SETTINGS");
                await Seconds(0.5f);
                await Shoot(directory, "2_menu_settings", report);
                Press("DONE");

                Systems_SceneRouter.LoadGame();
                await WaitForScene(Systems_SceneRouter.GAME_SCENE);

                Time.timeScale = FAST_FORWARD;
                await Seconds(LIVE_GAME_SECONDS);
                Time.timeScale = 1f;

                await Shoot(directory, "3_game_live", report);

                if (Press("DEBUG"))
                {
                    await Seconds(0.6f);
                    await Shoot(directory, "4_game_debug", report);

                    // The physics view, with the sheet closed so the field shows.
                    bool physics = Press("PHYSICS OFF");
                    Press("DEBUG");

                    if (physics)
                    {
                        await Seconds(0.4f);
                        await Shoot(directory, "4b_game_physics", report);
                        Press("DEBUG");
                        Press("PHYSICS ON");
                        Press("DEBUG");
                    }
                }

                Systems_GameLifetimeScope scope =
                    UnityEngine.Object.FindAnyObjectByType<Systems_GameLifetimeScope>();

                scope.Container
                    .Resolve<IPublisher<Systems_GameOverMessage>>()
                    .Publish(new Systems_GameOverMessage(21, 14));

                // A PUBLISHED whistle does not stop the director the way a played
                // one does, so the next play would resolve under the result sheet
                // and put its banner over it. Frozen instead; the sheet, the reel
                // and every fade run on unscaled time and do not notice.
                Time.timeScale = 0f;

                await Seconds(1.2f);
                await Shoot(directory, "5_game_final", report);

                if (Press("SCORING"))
                {
                    await Seconds(0.4f);
                    await Shoot(directory, "5b_game_final_scoring", report);
                    Press("BOX SCORE");
                }

                if (Press("HIGHLIGHTS"))
                {
                    await Seconds(1.5f);
                    await Shoot(directory, "6_game_highlights", report);
                }

                File.WriteAllText(
                    Path.Combine(directory, "audit.json"), JsonUtility.ToJson(report, true));

                Debug.Log(
                    $"[PoFootball] UI CAPTURE SWEEP DONE: {report.captures.Count} captures in {directory}");
            }
            catch (Exception exception)
            {
                Debug.LogError("[PoFootball] UI CAPTURE SWEEP FAILED: " + exception);
            }
            finally
            {
                Time.timeScale = 1f;

                if (oldWidth > 0 && oldHeight > 0)
                {
                    PlayModeWindow.SetCustomRenderingResolution(oldWidth, oldHeight, "Restored");
                }

                _running = false;
            }
        }

        private static async UniTask Shoot(string directory, string state, Report report)
        {
            for (int index = 0; index < Ratios.Length; index++)
            {
                Ratio ratio = Ratios[index];

                PlayModeWindow.SetCustomRenderingResolution(
                    ratio.Width, ratio.Height, "PoFootball " + ratio.Name);

                await Seconds(SETTLE_SECONDS);

                Systems_UiFitAudit.Rescan();
                Systems_UiFitAudit.Result fit = Systems_UiFitAudit.Run();

                string file = state + "_" + ratio.Name + ".png";
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, file));

                // The same frame as the shot: the DEBUG sheet grows and shrinks as
                // its findings change, and bounds read after the write wait drew
                // its box 50 units off the sheet.
                List<Region> regions = CollectRegions(ratio);

                await Seconds(WRITE_SECONDS);

                report.captures.Add(new Capture
                {
                    state = state,
                    ratio = ratio.Name,
                    file = file,
                    textChecked = fit.TextChecked,
                    cutOff = fit.CutOff,
                    cutOffExample = fit.CutOffExample,
                    overflowing = fit.Overflowing,
                    overflowExample = fit.OverflowExample,
                    scrolling = fit.Scrolling,
                    outOfReach = fit.OutOfReach,
                    outOfReachExample = fit.OutOfReachExample,
                    regions = regions,
                });
            }
        }

        /// <summary>
        /// Every visible named element in <see cref="RegionNames"/>, converted from
        /// panel units to the capture's pixels. Hidden ones are left out, so a box
        /// in the report is always round something on the screen.
        /// </summary>
        private static List<Region> CollectRegions(Ratio ratio)
        {
            List<Region> regions = new List<Region>();
            UIDocument[] documents =
                UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);

            for (int index = 0; index < documents.Length; index++)
            {
                VisualElement root = documents[index].rootVisualElement;

                if (root == null || root.panel == null)
                {
                    continue;
                }

                float scale = ratio.Width / root.panel.visualTree.worldBound.width;

                for (int name = 0; name < RegionNames.Length; name++)
                {
                    VisualElement element = root.Q(RegionNames[name]);

                    if (element == null || !IsShowing(element))
                    {
                        continue;
                    }

                    Rect bounds = element.worldBound;
                    regions.Add(new Region
                    {
                        name = RegionNames[name],
                        x = bounds.x * scale,
                        y = bounds.y * scale,
                        width = bounds.width * scale,
                        height = bounds.height * scale,
                    });
                }
            }

            return regions;
        }

        private static bool IsShowing(VisualElement element)
        {
            for (VisualElement scan = element; scan != null; scan = scan.hierarchy.parent)
            {
                IResolvedStyle style = scan.resolvedStyle;

                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden
                    || style.opacity <= 0.01f)
                {
                    return false;
                }
            }

            return element.worldBound.width > 0f && element.worldBound.height > 0f;
        }

        /// <summary>
        /// Presses the first visible button with this exact text on any document in
        /// the scene, the way the PlayMode tests do: a pooled NavigationSubmitEvent,
        /// which runs the button's clickable without faking a pointer.
        /// </summary>
        private static bool Press(string text)
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

            Debug.LogWarning($"[PoFootball] UI capture sweep: no visible '{text}' button.");
            return false;
        }

        private static async UniTask WaitForScene(string sceneName)
        {
            await UniTask.WaitUntil(() => SceneManager.GetActiveScene().name == sceneName);
            await Seconds(1.5f);
        }

        private static UniTask Seconds(float seconds)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime);
        }
    }
}

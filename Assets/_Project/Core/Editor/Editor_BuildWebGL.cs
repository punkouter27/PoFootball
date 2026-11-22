using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoFootball.EditorTools
{
    /// <summary>
    /// The game in a browser: SCN_MENU and SCN_GAME as a WebGL player, served from
    /// this machine (2026-10-24). Same scenes as the Android builds
    /// (Editor_BuildAndroidAAB.SHIP_SCENES), so the menu, the CPU game and the QB
    /// challenge all come with it — and tap-to-run / double-tap-to-pass is a mouse
    /// click there exactly as on a phone (Systems_HumanControlModel.Tap).
    /// Reports through the "WEBGL BUILD RESULT:" line in the editor log.
    ///
    /// GZIP WITH THE DECOMPRESSION FALLBACK. It is served from an Azure Static Web
    /// App (resource group PoFootball) and from `python -m http.server` locally,
    /// and neither sends the Content-Encoding header a compressed Unity build
    /// otherwise needs; with the fallback the loader unpacks the files itself.
    /// Gzip rather than Brotli because that unpacking runs in JavaScript, where
    /// gzip is several times faster to decode.
    ///
    /// ANY ASPECT. 540 x 960 is only the default size; the page
    /// (Assets/WebGLTemplates/PoFootball) sizes the canvas to fill the window
    /// between its header and footer, 16:9 or 9:16, and Systems_ScreenView scales
    /// the UI panel on height in a landscape window.
    ///
    /// No trained brain runs here any more than on the phone: until a revision 17
    /// run is promoted every player is a Heuristic, which needs no inference.
    /// </summary>
    internal static class Editor_BuildWebGL
    {
        private const string OUTPUT_PATH = "Builds/WebGL";

        private const int CANVAS_WIDTH = 540;
        private const int CANVAS_HEIGHT = 960;

        private const string WEBGL_TEMPLATE = "PROJECT:PoFootball";

        [MenuItem("Tools/PoFootball/Build WebGL (browser)")]
        private static void BuildFromMenu()
        {
            Build();
        }

        /// <summary>
        /// PUBLIC SO -executeMethod CAN REACH IT:
        ///
        ///     Unity.exe -batchmode -quit -nographics -projectPath &lt;root&gt; ^
        ///       -executeMethod PoFootball.EditorTools.Editor_BuildWebGL.Build
        /// </summary>
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("WEBGL BUILD RESULT: Aborted — exit Play mode first.");
                return;
            }

            var scenes = Editor_BuildAndroidAAB.ResolveShipScenes();
            if (scenes == null)
            {
                return;
            }

            // The MCP bridge's NuGet closure stays out of the player, as for
            // Android: pinned Editor-only, and its runtime assembly's define gate
            // shut for this target. WebGL is not the target the Editor sits on, so
            // the define change queues no recompile in front of BuildPlayer (the
            // failure Editor_BuildMenu.Build documents for Standalone).
            Editor_NuGetPluginGuard.PinBeforeBuild();
            Editor_NuGetPluginGuard.StripMcpDefines(NamedBuildTarget.WebGL);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.defaultWebScreenWidth = CANVAS_WIDTH;
            PlayerSettings.defaultWebScreenHeight = CANVAS_HEIGHT;

            // Assets/WebGLTemplates/PoFootball: header, canvas filling the window,
            // footer. Unity's default page scrolled both ways in any window under
            // ~1010 px tall and cut off the status bar's top strip.
            PlayerSettings.WebGL.template = WEBGL_TEMPLATE;

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = OUTPUT_PATH,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            Debug.Log($"WEBGL BUILD START: v{PlayerSettings.bundleVersion} scenes={scenes.Count}");

            string prevEmscriptenArgs = PlayerSettings.WebGL.emscriptenArgs;
            if (string.IsNullOrEmpty(prevEmscriptenArgs) || !prevEmscriptenArgs.Contains("--bind"))
                PlayerSettings.WebGL.emscriptenArgs = string.IsNullOrWhiteSpace(prevEmscriptenArgs)
                    ? "--bind"
                    : prevEmscriptenArgs + " --bind";
            BuildSummary summary;
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                summary = report.summary;
            }
            finally
            {
                PlayerSettings.WebGL.emscriptenArgs = prevEmscriptenArgs;
            }

            bool ok = summary.result == BuildResult.Succeeded;
            string msg = $"WEBGL BUILD RESULT: {summary.result} | errors={summary.totalErrors} | " +
                         $"size={summary.totalSize / (1024 * 1024)}MB | " +
                         $"time={summary.totalTime.TotalMinutes:F1}min | {OUTPUT_PATH}";
            if (ok) Debug.Log(msg);
            else Debug.LogError(msg);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}

using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PoFootball.Tests
{
    /// <summary>
    /// Every player-facing screen, on every portrait ratio a phone ships with,
    /// through Systems_UiFitAudit — the check the DEBUG sheet runs on a handset,
    /// run here on all of them at once:
    ///
    ///     menu, menu + SETTINGS, live game, final whistle, final + SCORING
    ///         x 16:9, 18:9, 19.5:9, 20:9, 21:9
    ///
    /// A finding is text cut off by an edge, a single line wider than its box, a
    /// ScrollView with more in it than it shows, or a control in the top of the
    /// screen a thumb does not reach. CLAUDE.md section 3 asks for all four to be
    /// zero; until now the only place that was checked was the device in
    /// someone's hand, with the sheet open, one ratio at a time.
    ///
    /// AND ONCE WITH A NOTCH. The Game view has no cutout, so the 20:9 pass is run
    /// again with a simulated top inset and home indicator
    /// (Systems_ScreenView.SimulatedInsetPixels), and the status bar has to start
    /// below it.
    ///
    /// Editor only: the ratio is set through PlayModeWindow, and a player has no
    /// Game view to resize. Ignored in batch mode, which has no status HUD and no
    /// rendering resolution to speak of.
    /// </summary>
    public sealed class Systems_AspectSweepTests
    {
        private static readonly Vector2Int[] Ratios =
        {
            new Vector2Int(1080, 1920),
            new Vector2Int(1080, 2160),
            new Vector2Int(1080, 2340),
            new Vector2Int(1080, 2400),
            new Vector2Int(1080, 2520),
        };

        /// <summary>Real seconds for a resolution change to reach the layout: the panel re-lays out, the safe area re-applies a frame later.</summary>
        private const float SETTLE_SECONDS = 0.5f;

#if UNITY_EDITOR
        private uint _oldWidth;
        private uint _oldHeight;

        [SetUp]
        public void RememberResolution()
        {
            UnityEditor.PlayModeWindow.GetRenderingResolution(out _oldWidth, out _oldHeight);
        }

        [TearDown]
        public void RestoreResolution()
        {
            Views.Systems_ScreenView.SimulatedInsetPixels = Vector2.zero;
            Time.timeScale = 1f;

            if (_oldWidth > 0 && _oldHeight > 0)
            {
                UnityEditor.PlayModeWindow.SetCustomRenderingResolution(_oldWidth, _oldHeight, "Restored");
            }
        }
#endif

        [UnityTest]
        public IEnumerator EveryScreen_FitsEveryRatio_WithNoFinding()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                Assert.Ignore("Batch mode has no Game view to resize and no status HUD.");
            }

            StringBuilder failures = new StringBuilder();

            for (int index = 0; index < Ratios.Length; index++)
            {
                Vector2Int ratio = Ratios[index];
                string name = ratio.x + "x" + ratio.y;

                UnityEditor.PlayModeWindow.SetCustomRenderingResolution(
                    (uint)ratio.x, (uint)ratio.y, "Sweep " + name);

                yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.MENU_SCENE);
                yield return new WaitForSecondsRealtime(SETTLE_SECONDS);
                Audit(name + " menu", failures);

                Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(
                    Systems_UiTestKit.RootOf<Views.Systems_MenuView>(), "SETTINGS"));
                yield return new WaitForSecondsRealtime(SETTLE_SECONDS);
                Audit(name + " settings", failures);

                yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);
                yield return new WaitForSecondsRealtime(SETTLE_SECONDS);
                Audit(name + " live game", failures);

                Systems_UiTestKit.BlowFinalWhistle();
                yield return new WaitForSecondsRealtime(SETTLE_SECONDS);
                Audit(name + " final", failures);

                Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(
                    Systems_UiTestKit.RootOf<Views.Systems_HudView>(), "SCORING"));
                yield return new WaitForSecondsRealtime(SETTLE_SECONDS);
                Audit(name + " final scoring", failures);

                Time.timeScale = 1f;
            }

            Assert.That(failures.Length, Is.Zero, "fit findings:\n" + failures);
#else
            Assert.Ignore("Editor only: the ratio is set through PlayModeWindow.");
            yield break;
#endif
        }

        /// <summary>
        /// A 20:9 phone with a 140-pixel camera cutout and a 60-pixel gesture bar:
        /// the status bar must start under the cutout and the menu must still fit.
        /// </summary>
        [UnityTest]
        public IEnumerator Notch_PushesTheStatusBarBelowIt_AndTheMenuStillFits()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                Assert.Ignore("Batch mode has no Game view to resize and no status HUD.");
            }

            const float TOP_PIXELS = 140f;
            const float BOTTOM_PIXELS = 60f;

            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1080, 2400, "Sweep notch");
            Views.Systems_ScreenView.SimulatedInsetPixels = new Vector2(TOP_PIXELS, BOTTOM_PIXELS);

            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.MENU_SCENE);
            yield return new WaitForSecondsRealtime(SETTLE_SECONDS);

            VisualElement statusRoot = Systems_UiTestKit.RootOf<Views.Systems_StatusHudView>();
            Button menuChip = Systems_UiTestKit.FindButton(statusRoot, "MENU");
            Assert.That(menuChip, Is.Not.Null, "no MENU chip");

            // Pixels to panel units: the panel is scaled to the screen's width.
            float unitsPerPixel = statusRoot.worldBound.width / Screen.width;
            float insetUnits = TOP_PIXELS * unitsPerPixel;

            Assert.That(
                menuChip.worldBound.yMin, Is.GreaterThanOrEqualTo(insetUnits - 1f),
                "the MENU chip is under the simulated cutout");

            Button play = Systems_UiTestKit.FindButton(
                Systems_UiTestKit.RootOf<Views.Systems_MenuView>(), "PLAY");
            Assert.That(
                play.worldBound.yMax,
                Is.LessThanOrEqualTo(statusRoot.worldBound.yMax - (BOTTOM_PIXELS * unitsPerPixel)),
                "PLAY runs into the simulated gesture bar");

            StringBuilder failures = new StringBuilder();
            Audit("notch menu", failures);
            Assert.That(failures.Length, Is.Zero, "fit findings:\n" + failures);
#else
            Assert.Ignore("Editor only: the ratio is set through PlayModeWindow.");
            yield break;
#endif
        }

        private static void Audit(string where, StringBuilder failures)
        {
            Views.Systems_UiFitAudit.Rescan();
            Views.Systems_UiFitAudit.Result fit = Views.Systems_UiFitAudit.Run();

            Assert.That(fit.TextChecked, Is.GreaterThan(0), where + ": the audit saw no text at all");

            if (fit.CutOff + fit.Overflowing + fit.Scrolling + fit.OutOfReach == 0)
            {
                return;
            }

            failures.Append(where)
                .Append(": cut off ").Append(fit.CutOff).Append(" \"").Append(fit.CutOffExample)
                .Append("\", overflowing ").Append(fit.Overflowing).Append(" \"").Append(fit.OverflowExample)
                .Append("\", scrolling ").Append(fit.Scrolling).Append(' ').Append(fit.ScrollingScreen)
                .Append(", out of reach ").Append(fit.OutOfReach).Append(" \"").Append(fit.OutOfReachExample)
                .Append("\"\n");
        }
    }
}

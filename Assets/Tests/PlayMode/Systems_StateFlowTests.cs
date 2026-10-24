using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PoFootball.Tests
{
    /// <summary>
    /// The transitions Systems_ScreenBuildTests does not drive: the double taps and
    /// mid-load taps that make dead ends, SAME GAME, the HIGHLIGHTS tab and back,
    /// and every route Android Back takes.
    ///
    /// Back is driven through Systems_StatusHudView.RouteBack, the method the
    /// Escape binding calls — the binding itself is one line in Systems_InputView
    /// and the routing is everything that can be wrong.
    /// </summary>
    public sealed class Systems_StateFlowTests
    {
        [TearDown]
        public void ResetTime()
        {
            Time.timeScale = 1f;
        }

        /// <summary>
        /// Two taps on REMATCH in one frame: one load, one new HUD, and the router
        /// free again afterwards — not a second load queued behind the first, and
        /// not a guard left stuck that kills every later button.
        /// </summary>
        [UnityTest]
        public IEnumerator Rematch_TappedTwice_LoadsOneGameAndLeavesTheRouterFree()
        {
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);

            Views.Systems_HudView firstHud = Object.FindAnyObjectByType<Views.Systems_HudView>();
            Systems_UiTestKit.BlowFinalWhistle();
            yield return null;
            yield return null;

            int gameLoads = 0;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> counter = (scene, mode) =>
            {
                if (scene.name == Views.Systems_SceneRouter.GAME_SCENE)
                {
                    gameLoads++;
                }
            };

            SceneManager.sceneLoaded += counter;

            try
            {
                Button rematch = Systems_UiTestKit.FindButton(
                    Systems_UiTestKit.RootOf<Views.Systems_HudView>(), "REMATCH");
                Systems_UiTestKit.Press(rematch);
                Systems_UiTestKit.Press(rematch);

                yield return new WaitUntil(
                    () => firstHud == null && Object.FindAnyObjectByType<Views.Systems_HudView>() != null);
                yield return new WaitForSecondsRealtime(1f);

                Assert.That(gameLoads, Is.EqualTo(1), "a double tap on REMATCH loaded the game twice");
                Assert.That(
                    Object.FindObjectsByType<Views.Systems_HudView>(FindObjectsInactive.Exclude).Length,
                    Is.EqualTo(1), "two HUDs after a double tap");
            }
            finally
            {
                SceneManager.sceneLoaded -= counter;
            }

            Views.Systems_SceneRouter.LoadMenu();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.MENU_SCENE);
        }

        /// <summary>
        /// MENU pressed while a game is still loading is swallowed, not queued: the
        /// game arrives and stays, and the router takes the next request.
        /// </summary>
        [UnityTest]
        public IEnumerator Menu_DuringAGameLoad_IsIgnoredNotQueued()
        {
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.MENU_SCENE);

            Views.Systems_SceneRouter.LoadGame();
            Views.Systems_SceneRouter.LoadMenu();

            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.GAME_SCENE);
            yield return new WaitForSecondsRealtime(1f);

            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(Views.Systems_SceneRouter.GAME_SCENE),
                "a MENU tap during the load was queued and fired after it");

            Views.Systems_SceneRouter.LoadMenu();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.MENU_SCENE);

            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(Views.Systems_SceneRouter.MENU_SCENE),
                "the router stayed locked after an ignored tap");
        }

        /// <summary>SAME GAME loads the seed the game was played on, not a fresh one.</summary>
        [UnityTest]
        public IEnumerator SameGame_ReplaysTheSeedJustPlayed()
        {
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);

            uint seed = Systems.Systems_EpisodeSeed.Value;
            Views.Systems_HudView firstHud = Object.FindAnyObjectByType<Views.Systems_HudView>();

            Systems_UiTestKit.BlowFinalWhistle();
            yield return null;
            yield return null;

            Button same = Systems_UiTestKit.RootOf<Views.Systems_HudView>().Q<Button>("ReplaySeed");
            Systems_UiTestKit.Press(same);

            yield return new WaitUntil(
                () => firstHud == null && Object.FindAnyObjectByType<Views.Systems_HudView>() != null);
            yield return null;

            Assert.That(Systems.Systems_EpisodeSeed.Value, Is.EqualTo(seed), "SAME GAME played a different seed");
        }

        /// <summary>
        /// The result sheet's tabs: SCORING swaps the pane without moving REMATCH,
        /// and HIGHLIGHTS hands the screen to the reel — the overlay goes down —
        /// and Back brings it straight back.
        /// </summary>
        [UnityTest]
        public IEnumerator FinalTabs_SwapPanesInPlace_AndHighlightsComeBackOnBack()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("No status HUD, and no replay view, in batch mode.");
            }

            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);

            // Long enough at 8x for a play or two to be worth a highlight.
            Time.timeScale = 8f;
            yield return new WaitForSecondsRealtime(10f);

            Systems_UiTestKit.BlowFinalWhistle();
            yield return null;
            yield return null;

            VisualElement root = Systems_UiTestKit.RootOf<Views.Systems_HudView>();
            Button rematch = Systems_UiTestKit.FindButton(root, "REMATCH");
            float rematchY = rematch.worldBound.y;

            Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(root, "SCORING"));
            yield return null;

            Assert.That(
                root.Q("ScoringPane").resolvedStyle.visibility, Is.EqualTo(Visibility.Visible),
                "SCORING did not show its pane");
            Assert.That(rematch.worldBound.y, Is.EqualTo(rematchY).Within(1f), "REMATCH moved when the tab changed");

            Button highlights = Systems_UiTestKit.FindButton(root, "HIGHLIGHTS");

            if (highlights.resolvedStyle.display == DisplayStyle.None)
            {
                Assert.Inconclusive("No play in ten seconds at 8x was long enough to be a highlight.");
            }

            VisualElement overlay = root.Q("FinalScore");
            Systems_UiTestKit.Press(highlights);
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.That(overlay.resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden),
                "the result sheet stayed up over the highlight");

            Systems_UiTestKit.StatusHud().RouteBack();
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.That(overlay.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible),
                "Back did not close the highlight and bring the result back");

            // Nothing left open: Back leaves a finished game at once.
            Systems_UiTestKit.StatusHud().RouteBack();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.MENU_SCENE);
        }

        /// <summary>Back in a game in progress asks first, the way the MENU chip does: one arms QUIT?, a second leaves.</summary>
        [UnityTest]
        public IEnumerator Back_InALiveGame_ArmsThenLeaves()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("No status HUD in batch mode.");
            }

            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);

            Views.Systems_StatusHudView hud = Systems_UiTestKit.StatusHud();
            hud.RouteBack();
            yield return null;

            Assert.That(
                Systems_UiTestKit.FindButton(Systems_UiTestKit.RootOf<Views.Systems_StatusHudView>(), "QUIT?"),
                Is.Not.Null, "the first Back did not arm the way out");
            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(Views.Systems_SceneRouter.GAME_SCENE),
                "one Back threw a game away");

            hud.RouteBack();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.MENU_SCENE);
        }

        /// <summary>
        /// On the menu, Back closes the settings sheet first, and only with nothing
        /// open arms EXIT? on the corner chip.
        /// </summary>
        [UnityTest]
        public IEnumerator Back_OnTheMenu_ClosesSheetsThenArmsExit()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("No status HUD in batch mode.");
            }

            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.MENU_SCENE);

            VisualElement menuRoot = Systems_UiTestKit.RootOf<Views.Systems_MenuView>();
            Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(menuRoot, "SETTINGS"));
            yield return null;

            Views.Systems_StatusHudView hud = Systems_UiTestKit.StatusHud();
            VisualElement statusRoot = Systems_UiTestKit.RootOf<Views.Systems_StatusHudView>();

            hud.RouteBack();
            yield return new WaitForSecondsRealtime(0.4f);

            Assert.That(
                menuRoot.Q("SettingsSheet").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden),
                "Back did not close SETTINGS");
            Assert.That(Systems_UiTestKit.FindButton(statusRoot, "EXIT?"), Is.Null,
                "Back armed EXIT? while a sheet was still open");

            hud.RouteBack();
            yield return null;

            Assert.That(Systems_UiTestKit.FindButton(statusRoot, "EXIT?"), Is.Not.Null,
                "Back with nothing open did not arm EXIT?");
        }
    }
}

using System.Collections;
using NUnit.Framework;
using PoFootball.Agents;
using PoFootball.Rewards;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PoFootball.Tests
{
    /// <summary>
    /// The Po showcase rules, run rather than read: every control penalty is
    /// actually charged in a live game, the TAB overlay shows FPS / episode reward /
    /// inference / curriculum stage and its benchmark times the simulation, and
    /// demo mode starts after 30 s idle and ends on input.
    ///
    /// The status HUD is spawned by hand (Systems_StatusHudBootstrap.SpawnInto)
    /// because the bootstrap deliberately spawns none in batch mode.
    /// </summary>
    public sealed class Systems_ShowcasePlayTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Views.Systems_AttractModeView.ResetDemoGame();
            Views.Systems_AttractModeView.Suppressed = true;
        }

        [UnityTest]
        public IEnumerator EveryControlPenaltyTerm_IsChargedInALiveGame()
        {
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);

            Time.timeScale = 8f;
            float deadline = Time.realtimeSinceStartup + 30f;
            Reward_Penalty.Terms totals = default;

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(1f);
                totals = SumPenalties();

                if (totals.Effort < 0f && totals.AngularVelocity < 0f
                    && totals.ActionJitter < 0f && totals.Slide < 0f)
                {
                    break;
                }
            }

            Time.timeScale = 1f;

            Assert.That(totals.Effort, Is.LessThan(0f), "torque/effort penalty never charged");
            Assert.That(totals.AngularVelocity, Is.LessThan(0f), "angular velocity penalty never charged");
            Assert.That(totals.ActionJitter, Is.LessThan(0f), "action jitter penalty never charged");
            Assert.That(totals.Slide, Is.LessThan(0f), "slide (foot sliding) penalty never charged");

            Models.Systems_SimHealthModel health = Object.FindAnyObjectByType<Systems.Systems_GameLifetimeScope>()
                .Container.Resolve(typeof(Models.Systems_SimHealthModel)) as Models.Systems_SimHealthModel;
            Assert.That(health.PenaltyEffort, Is.LessThan(0.0), "the overlay's penalty sums saw nothing");

            Debug.Log(
                $"[PoFootball] penalties charged: torque {totals.Effort:0.000} spin {totals.AngularVelocity:0.000} "
                + $"jitter {totals.ActionJitter:0.000} slide {totals.Slide:0.000}");
        }

        [UnityTest]
        public IEnumerator TabOverlay_ShowsTheReadout_AndBenchmarksTheSimulation()
        {
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.GAME_SCENE);
            Views.Systems_StatusHudView hud =
                Views.Systems_StatusHudBootstrap.SpawnInto(SceneManager.GetActiveScene());
            yield return null;
            yield return null;

            // Long enough at 8x for a few plays to finish and report their reward.
            Time.timeScale = 8f;
            yield return new WaitForSecondsRealtime(8f);
            Time.timeScale = 1f;

            hud.ToggleDebugSheet();
            Assert.That(hud.IsSheetOpen, Is.True, "TAB did not open the sheet");
            yield return new WaitForSecondsRealtime(0.6f);

            string readout = hud.RlReadoutText;
            Debug.Log("[PoFootball] TAB readout:\n" + readout);
            StringAssert.Contains("FPS", readout);
            StringAssert.Contains("Episode reward", readout);
            StringAssert.DoesNotContain("no play finished yet", readout);
            StringAssert.Contains("ms agent step", readout);
            StringAssert.Contains("Curriculum", readout);

            Views.Systems_SimBenchmark benchmark = Object.FindAnyObjectByType<Views.Systems_SimBenchmark>();
            hud.PressBenchmark();
            Assert.That(benchmark.IsRunning, Is.True, "the button did not start the benchmark");

            float deadline = Time.realtimeSinceStartup + 20f;
            while (benchmark.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            // The sheet rewrites its text every quarter second.
            yield return new WaitForSecondsRealtime(0.6f);

            string bench = hud.BenchmarkText;
            Debug.Log("[PoFootball] " + bench);
            StringAssert.Contains("physics ticks/s", bench);
            StringAssert.Contains("not a training run", bench);

            Assert.That(benchmark.TicksPerSecond, Is.GreaterThan(50f), "the benchmark ran slower than real time");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the benchmark did not put the viewer's speed back");
        }

        [UnityTest]
        public IEnumerator DemoMode_StartsAfterThirtySecondsIdle_AndInputEndsIt()
        {
            Views.Systems_AttractModeView.Suppressed = false;
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.MENU_SCENE);
            Views.Systems_StatusHudBootstrap.SpawnInto(SceneManager.GetActiveScene());
            yield return null;

            Views.Systems_AttractModeView menuView = Object.FindAnyObjectByType<Views.Systems_AttractModeView>();
            Assert.That(menuView, Is.Not.Null, "no attract view on the menu");
            Assert.That(menuView.Settings.IdleSeconds, Is.EqualTo(30f), "idle threshold is not 30 s");

            float idleFrom = Time.realtimeSinceStartup;
            float deadline = idleFrom + 40f;

            while (SceneManager.GetActiveScene().name != Views.Systems_SceneRouter.GAME_SCENE
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            float waited = Time.realtimeSinceStartup - idleFrom;
            Debug.Log($"[PoFootball] demo game loaded after {waited:0.0} s idle");
            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(Views.Systems_SceneRouter.GAME_SCENE),
                "no demo game after 40 s idle");
            Assert.That(waited, Is.GreaterThanOrEqualTo(29.5f), "demo started before 30 s idle");
            Assert.That(Views.Systems_AttractModeView.DemoGame, Is.True, "the game was not flagged as a demo");

            yield return null;
            yield return null;
            Views.Systems_StatusHudBootstrap.SpawnInto(SceneManager.GetActiveScene());
            yield return null;

            Views.Systems_AttractModeView gameView = Object.FindAnyObjectByType<Views.Systems_AttractModeView>();
            Views.Systems_BroadcastCameraView camera = Object.FindAnyObjectByType<Views.Systems_BroadcastCameraView>();
            Assert.That(gameView.IsActive, Is.True, "the demo game did not start in demo mode");

            yield return new WaitForSecondsRealtime(gameView.Settings.CameraDwellSeconds + 0.5f);
            Assert.That(
                camera.CameraMode, Is.Not.EqualTo(Views.Systems_CameraMode.Broadcast),
                "the demo did not cycle the camera");

            gameView.NotifyInput();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.MENU_SCENE);
            Assert.That(Views.Systems_AttractModeView.DemoGame, Is.False, "input did not end the demo");
        }

        private static Reward_Penalty.Terms SumPenalties()
        {
            Agent_FootballPlayer[] players = Object.FindObjectsByType<Agent_FootballPlayer>(FindObjectsInactive.Exclude);
            float effort = 0f;
            float spin = 0f;
            float jitter = 0f;
            float slide = 0f;

            for (int index = 0; index < players.Length; index++)
            {
                Reward_Penalty.Terms terms = players[index].PenaltyTotals;
                effort += terms.Effort;
                spin += terms.AngularVelocity;
                jitter += terms.ActionJitter;
                slide += terms.Slide;
            }

            return new Reward_Penalty.Terms(effort, spin, jitter, slide);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PoFootball.Tests
{
    /// <summary>
    /// The whole loop a player drives — menu, SETTINGS, a game at speed, the
    /// whistle, SCORING, REMATCH, back to the menu — with the console listened to
    /// throughout. Any error, exception or assert fails it, and so does any of the
    /// warnings that mean the layout is wrong rather than merely noisy.
    ///
    /// WHY A TEST FOR THE CONSOLE. The Unity test runner already fails a test on
    /// an unexpected LogError, but only inside the test that logged it, and only
    /// errors. A NullReferenceException in a view's Update on the third scene load,
    /// or the HUD's own "the ribbon is drawn behind the scoreboard" warning, is
    /// exactly what shows up only after a full loop and is read by nobody.
    ///
    /// WARNINGS ARE FILTERED, NOT ALL FAILED. Two are expected on this build and
    /// say so themselves: the brain table refused on a contract mismatch (every
    /// player is a heuristic until a revision 17 run is promoted, CLAUDE.md), and
    /// Editor-only bridge chatter. The list below is the warnings that are a fault.
    /// </summary>
    public sealed class Systems_ConsoleHealthTests
    {
        /// <summary>Fragments of a warning that means the UI or the loop is broken.</summary>
        private static readonly string[] FaultyWarnings =
        {
            "The score row is",
            "Layout update is struggling",
            "could not be loaded",
            "was never injected",
            "no PanelSettings",
            "has no root visual element",
        };

        /// <summary>
        /// Errors that are the automation bridge's, not the game's: the Unity CLI
        /// polling test status while play mode holds the main thread.
        /// </summary>
        private static readonly string[] ToolingErrors =
        {
            "Failed to handle /api/exec request",
        };

        private readonly List<string> _faults = new List<string>();

        [SetUp]
        public void Listen()
        {
            _faults.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void StopListening()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator FullLoop_LogsNoErrorAndNoLayoutWarning()
        {
            yield return Systems_UiTestKit.LoadAndSettle(Views.Systems_SceneRouter.MENU_SCENE);

            Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(
                Systems_UiTestKit.RootOf<Views.Systems_MenuView>(), "SETTINGS"));
            yield return new WaitForSecondsRealtime(0.3f);
            Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(
                Systems_UiTestKit.RootOf<Views.Systems_MenuView>(), "DONE"));

            Views.Systems_SceneRouter.LoadGame();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.GAME_SCENE);

            Time.timeScale = 8f;
            yield return new WaitForSecondsRealtime(6f);

            Views.Systems_HudView firstHud = Object.FindAnyObjectByType<Views.Systems_HudView>();
            Systems_UiTestKit.BlowFinalWhistle();
            yield return new WaitForSecondsRealtime(0.5f);

            Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(
                Systems_UiTestKit.RootOf<Views.Systems_HudView>(), "SCORING"));
            yield return new WaitForSecondsRealtime(0.3f);

            Time.timeScale = 1f;
            Systems_UiTestKit.Press(Systems_UiTestKit.FindButton(
                Systems_UiTestKit.RootOf<Views.Systems_HudView>(), "REMATCH"));

            yield return new WaitUntil(
                () => firstHud == null && Object.FindAnyObjectByType<Views.Systems_HudView>() != null);
            yield return new WaitForSecondsRealtime(2f);

            Views.Systems_SceneRouter.LoadMenu();
            yield return Systems_UiTestKit.WaitForScene(Views.Systems_SceneRouter.MENU_SCENE);
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.That(_faults, Is.Empty, "console faults over the loop:\n" + string.Join("\n", _faults));
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                for (int index = 0; index < ToolingErrors.Length; index++)
                {
                    if (condition.Contains(ToolingErrors[index]))
                    {
                        return;
                    }
                }

                _faults.Add(type + ": " + condition);
                return;
            }

            if (type != LogType.Warning)
            {
                return;
            }

            for (int index = 0; index < FaultyWarnings.Length; index++)
            {
                if (condition.Contains(FaultyWarnings[index]))
                {
                    _faults.Add("Warning: " + condition);
                    return;
                }
            }
        }
    }
}

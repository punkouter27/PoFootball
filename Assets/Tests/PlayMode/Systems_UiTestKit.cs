using System.Collections;
using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;

namespace PoFootball.Tests
{
    /// <summary>
    /// What the state-flow, aspect-sweep and console-health tests share: find a
    /// screen's document, press a button the way a thumb would, wait for a scene,
    /// and blow the final whistle without playing ten minutes to it.
    ///
    /// Systems_ScreenBuildTests keeps its own private copies of the first two; it
    /// predates this class and moving them would be churn in a file that passes.
    /// </summary>
    internal static class Systems_UiTestKit
    {
        public static UIDocument DocumentFor<TScreen>()
            where TScreen : Views.Systems_ScreenView
        {
            TScreen screen = Object.FindAnyObjectByType<TScreen>();
            Assert.That(screen, Is.Not.Null, $"no {typeof(TScreen).Name} in the loaded scene");
            return screen.GetComponent<UIDocument>();
        }

        public static VisualElement RootOf<TScreen>()
            where TScreen : Views.Systems_ScreenView
        {
            return DocumentFor<TScreen>().rootVisualElement;
        }

        /// <summary>The first button with this exact text in any visible part of the tree, or null.</summary>
        public static Button FindButton(VisualElement root, string text)
        {
            List<Button> buttons = root.Query<Button>().ToList();

            for (int index = 0; index < buttons.Count; index++)
            {
                if (buttons[index].text == text)
                {
                    return buttons[index];
                }
            }

            return null;
        }

        /// <summary>
        /// A pooled NavigationSubmitEvent: runs the button's clickable exactly as a
        /// tap does, without faking pointer coordinates.
        /// </summary>
        public static void Press(Button button)
        {
            Assert.That(button, Is.Not.Null, "pressed a button that does not exist");

            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        public static IEnumerator LoadAndSettle(string sceneName)
        {
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        public static IEnumerator WaitForScene(string sceneName)
        {
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == sceneName);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// The final whistle, PUBLISHED, then time frozen: a published whistle does
        /// not stop the director the way a played one does, and the next play would
        /// resolve under the result sheet. Everything post-game runs on unscaled
        /// time. The caller puts Time.timeScale back.
        /// </summary>
        public static void BlowFinalWhistle()
        {
            Systems.Systems_GameLifetimeScope scope =
                Object.FindAnyObjectByType<Systems.Systems_GameLifetimeScope>();

            scope.Container
                .Resolve<IPublisher<Systems.Systems_GameOverMessage>>()
                .Publish(new Systems.Systems_GameOverMessage(21, 14));

            Time.timeScale = 0f;
        }

        public static Views.Systems_StatusHudView StatusHud()
        {
            Views.Systems_StatusHudView hud = Object.FindAnyObjectByType<Views.Systems_StatusHudView>();
            Assert.That(hud, Is.Not.Null, "no status HUD — is this batch mode?");
            return hud;
        }
    }
}

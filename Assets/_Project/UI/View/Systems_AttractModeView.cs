using PoFootball.Models;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoFootball.Views
{
    /// <summary>
    /// Attract (demo) mode: after Systems_ShowcaseSettings.IdleSeconds (30 s) with
    /// no key, click or touch, the game shows itself off until someone touches it.
    ///
    ///     on the menu   a CPU game starts as a DEMO game
    ///     in a game     the camera cycles BROADCAST -> FOLLOW -> ORBIT every
    ///                   CameraDwellSeconds, under a "press any key" caption
    ///
    /// Any input ends it: a demo game goes back to the menu (and so does one that
    /// reaches the final whistle), a game the viewer started keeps playing with
    /// the broadcast camera back. The players are whatever runs the CPU game —
    /// the promoted brain table when one loads, the scripted heuristic until then
    /// (CLAUDE.md: no brain loads in contract revision 18).
    ///
    /// Added beside the status HUD by Systems_StatusHudBootstrap, so it exists on
    /// every player-facing screen and in no training scene or batch-mode run.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Systems_AttractModeView : MonoBehaviour
    {
        /// <summary>
        /// Whether the game now loading or running was started by the demo. Static
        /// for the reason Systems_GameVariantRequest is: it has to cross the scene
        /// load from the menu to the game.
        /// </summary>
        internal static bool DemoGame { get; private set; }

        /// <summary>
        /// Holds the idle timer at zero. The PlayMode suite sets it for every test
        /// but the demo's own (Systems_ShowcaseSuppression): in an Editor run the
        /// HUD and this view are spawned on every menu load, and a suite that sits
        /// on the menu for 30 s would otherwise have a demo game loaded under it.
        /// </summary>
        internal static bool Suppressed { get; set; }

        private Systems_ShowcaseSettings _settings;
        private Systems_StatusHudView _statusHud;
        private Systems_BroadcastCameraView _camera;

        private float _idleSeconds;
        private float _dwell;
        private bool _active;
        private bool _onMenu;

        internal bool IsActive => _active;

        internal float IdleSeconds => _idleSeconds;

        internal Systems_ShowcaseSettings Settings => _settings;

        internal void Bind(Systems_StatusHudView statusHud)
        {
            _statusHud = statusHud;
        }

        private void Awake()
        {
            _settings = Systems_ShowcaseSettings.Load();
        }

        private void Start()
        {
            _onMenu = string.Equals(
                gameObject.scene.name, Systems_SceneRouter.MENU_SCENE, System.StringComparison.Ordinal);

            // A game the demo started is a demo from its first frame.
            if (!_onMenu && DemoGame)
            {
                Activate();
            }
        }

        private void OnDisable()
        {
            if (_active && _camera != null)
            {
                _camera.SetCameraMode(Systems_CameraMode.Broadcast);
            }
        }

        private void Update()
        {
            if (AnyInputThisFrame())
            {
                NotifyInput();
                return;
            }

            float delta = Time.unscaledDeltaTime;

            if (!_active)
            {
                _idleSeconds = Suppressed ? 0f : _idleSeconds + delta;

                if (_idleSeconds >= _settings.IdleSeconds)
                {
                    StartDemo();
                }

                return;
            }

            // A demo game that reached its final whistle goes back to the menu,
            // whose own idle timer starts the next one.
            if (DemoGame && Systems_SceneRouter.HasFinishedGame)
            {
                Systems_SceneRouter.TakeSummary();
                Systems_SceneRouter.LoadMenu();
                return;
            }

            _dwell += delta;

            if (_dwell >= _settings.CameraDwellSeconds)
            {
                _dwell = 0f;
                CycleCamera();
            }
        }

        /// <summary>A key, click or touch: the viewer is back. Internal so a test can stand in for one.</summary>
        internal void NotifyInput()
        {
            _idleSeconds = 0f;

            if (!_active)
            {
                return;
            }

            Deactivate();

            if (DemoGame)
            {
                DemoGame = false;
                Systems_SceneRouter.LoadMenu();
            }
        }

        private void StartDemo()
        {
            Systems_Log.Info(
                $"[PoFootball] DEMO on after {_idleSeconds:0.0} s idle ({gameObject.scene.name}).");

            if (_onMenu)
            {
                // Once: the load replaces this scene, and the game's own copy of
                // this view takes the demo from there.
                _active = true;
                DemoGame = true;
                Systems_SceneRouter.LoadGame();
                return;
            }

            Activate();
        }

        private void Activate()
        {
            _active = true;
            _dwell = 0f;
            _camera = FindAnyObjectByType<Systems_BroadcastCameraView>();

            if (_statusHud != null)
            {
                _statusHud.ShowDemoCaption(_settings.DemoCaption);
            }
        }

        private void Deactivate()
        {
            _active = false;

            if (_camera != null)
            {
                _camera.SetCameraMode(Systems_CameraMode.Broadcast);
            }

            if (_statusHud != null)
            {
                _statusHud.ShowDemoCaption(null);
            }
        }

        private void CycleCamera()
        {
            if (_camera == null)
            {
                _camera = FindAnyObjectByType<Systems_BroadcastCameraView>();

                if (_camera == null)
                {
                    return;
                }
            }

            // BROADCAST -> FOLLOW -> ORBIT; FREE is a viewer's pan, not a shot.
            Systems_CameraMode next = _camera.CycleCameraMode();

            if (next == Systems_CameraMode.Free)
            {
                _camera.SetCameraMode(Systems_CameraMode.Broadcast);
            }
        }

        private static bool AnyInputThisFrame()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                return true;
            }

            Mouse mouse = Mouse.current;

            if (mouse != null
                && (mouse.leftButton.wasPressedThisFrame
                    || mouse.rightButton.wasPressedThisFrame
                    || mouse.delta.ReadValue().sqrMagnitude > 1f
                    || mouse.scroll.ReadValue().sqrMagnitude > 0f))
            {
                return true;
            }

            Touchscreen touch = Touchscreen.current;

            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        /// <summary>For a test: the static flag a demo load carries, cleared between tests.</summary>
        internal static void ResetDemoGame()
        {
            DemoGame = false;
        }
    }
}

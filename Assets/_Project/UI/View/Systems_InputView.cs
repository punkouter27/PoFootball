using UnityEngine;
using UnityEngine.InputSystem;

namespace PoFootball.Views
{
    /// <summary>
    /// The keys and gestures that are not a tap on something: TAB or a
    /// three-finger tap opens and closes the DEBUG sheet, and Android Back (Escape
    /// on a keyboard) closes whatever is on top, then leaves.
    ///
    /// A thin adapter and nothing else, per .claude/rules/architecture.md — it owns
    /// the actions, enables them in OnEnable, disables them in OnDisable, and
    /// forwards the press. Whether a sheet may be shown at all, and what Back
    /// closes, are the status HUD's decisions (Systems_StatusHudView
    /// .DiagnosticsAllowed and RouteBack), not this class's.
    ///
    /// THE ACTIONS ARE BUILT IN CODE, NOT FROM AN .inputactions ASSET. Three
    /// bindings do not justify an asset, a generated class and the import step
    /// that keeps the two in step; the project builds its screens from C# for the
    /// same reason (CLAUDE.md section 3). When there is a second action map this
    /// is where the asset goes.
    ///
    /// ANDROID BACK IS THE ESCAPE KEY. The Input System reports the system back
    /// button and the back gesture as Keyboard.escapeKey on Android, so one
    /// binding serves the phone, a desktop build and the Editor. Before this
    /// nothing handled it, and on a phone with gesture navigation a swipe from
    /// the edge did nothing at all — no sheet closed, no game was left.
    ///
    /// THE GESTURE IS A THIRD FINGER, so it cannot be a pinch. The field surface
    /// tracks two pointers (Systems_HudView.MAX_POINTERS) and a third touch is the
    /// one thing no other control in the game listens for; it opens the sheet
    /// without reaching for the DEBUG chip in the corner mid-play.
    ///
    /// Taps, drags and pinches on the field are UI Toolkit pointer events on
    /// Systems_HudView's field surface, because the panel already knows which of
    /// them landed on a button and an InputAction does not.
    ///
    /// Spawned beside the status HUD by <see cref="Systems_StatusHudBootstrap"/>,
    /// so there is exactly one per player-facing scene and none in training.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Systems_InputView : MonoBehaviour
    {
        private const string DEBUG_TOGGLE_BINDING = "<Keyboard>/tab";
        private const string DEBUG_GESTURE_BINDING = "<Touchscreen>/touch2/press";
        private const string BACK_BINDING = "<Keyboard>/escape";

        private InputAction _toggleDebug;
        private InputAction _back;
        private Systems_StatusHudView _statusHud;
        private Systems_BroadcastCameraView _cameraView;

        private void Awake()
        {
            _toggleDebug = new InputAction(
                nameof(_toggleDebug), InputActionType.Button, DEBUG_TOGGLE_BINDING);
            _toggleDebug.AddBinding(DEBUG_GESTURE_BINDING);

            _back = new InputAction(nameof(_back), InputActionType.Button, BACK_BINDING);
        }

        private void OnEnable()
        {
            _toggleDebug.performed += OnToggleDebug;
            _toggleDebug.Enable();

            _back.performed += OnBack;
            _back.Enable();
        }

        private void OnDisable()
        {
            _toggleDebug.performed -= OnToggleDebug;
            _toggleDebug.Disable();

            _back.performed -= OnBack;
            _back.Disable();
        }

        private void OnDestroy()
        {
            _toggleDebug.Dispose();
            _back.Dispose();
        }

        internal void Bind(Systems_StatusHudView statusHud)
        {
            _statusHud = statusHud;
        }

        private void OnToggleDebug(InputAction.CallbackContext context)
        {
            if (_statusHud != null)
            {
                _statusHud.ToggleDebugSheet();
            }
        }

        private void OnBack(InputAction.CallbackContext context)
        {
            if (_statusHud != null)
            {
                _statusHud.RouteBack();
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (_cameraView == null)
            {
                _cameraView = FindAnyObjectByType<Systems_BroadcastCameraView>();
            }

            Models.Systems_SimHealthModel health = _statusHud != null ? _statusHud.Health : null;

            if (kb.f1Key.wasPressedThisFrame && health != null)
            {
                health.AppMode = (Models.Systems_AppMode)(((int)health.AppMode + 1) % 3);
            }

            if (kb.f2Key.wasPressedThisFrame && _cameraView != null)
            {
                _cameraView.CycleCameraMode();
            }

            if (kb.f3Key.wasPressedThisFrame && _statusHud != null)
            {
                _statusHud.ToggleDebugSheet();
            }

            if (kb.f4Key.wasPressedThisFrame && health != null)
            {
                health.UseFallbackPolicy = !health.UseFallbackPolicy;
            }

            if (kb.spaceKey.wasPressedThisFrame)
            {
                Time.timeScale = Time.timeScale > 0f ? 0f : 1f;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                Time.timeScale = 1f;
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (active.IsValid())
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(active.name);
                }
            }

            if (_cameraView != null && _cameraView.CameraMode == Systems_CameraMode.Free)
            {
                float step = 12f * Mathf.Max(Time.unscaledDeltaTime, 0.005f);
                Vector2 delta = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) delta.y += step;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) delta.y -= step;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) delta.x += step;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) delta.x -= step;
                if (delta != Vector2.zero)
                {
                    _cameraView.AddFreePan(delta);
                }
            }
        }
    }
}

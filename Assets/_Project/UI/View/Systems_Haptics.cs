using System;
using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// A short buzz whose strength follows the hit, on Android. Nothing anywhere
    /// else.
    ///
    /// THE PLATFORM API, NOT A PACKAGE. The minimum SDK is 26, which is exactly
    /// where VibrationEffect.createOneShot(ms, amplitude) arrived, so the one call
    /// worth making needs no plugin: a third-party haptics package would be a
    /// dependency to keep current for a single method.
    ///
    /// EXPLICIT JNI SIGNATURES. AndroidJavaObject.Call builds a signature from
    /// the C# argument types, and the object returned by createOneShot is a
    /// VibrationEffect$OneShot — so a Call("vibrate", effect) asks for
    /// vibrate(OneShot), which does not exist. The method ids here are looked up
    /// with the declared signatures instead.
    ///
    /// Handheld.Vibrate is referenced as the fallback for a device whose vibrator
    /// will not take an amplitude, and its presence is also what makes the Android
    /// build add the VIBRATE permission to the generated manifest; this project
    /// has no hand-written one.
    ///
    /// Any failure turns haptics off for the session. A buzz is never worth an
    /// exception per tackle.
    /// </summary>
    public sealed class Systems_Haptics : IDisposable
    {
        private const int MIN_MILLISECONDS = 18;
        private const int MAX_MILLISECONDS = 70;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _vibrator;
        private AndroidJavaClass _effectClass;
        private IntPtr _vibrateMethod;
        private IntPtr _createOneShotMethod;
#endif
        private bool _failed;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android only, for the reason Systems_SpeechOutput._started gives.
        private bool _initialised;
#endif

        /// <param name="strength">0..1: how hard the hit was.</param>
        public void Pulse(float strength)
        {
            if (_failed || !Systems_BroadcastSettings.Haptics)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!_initialised)
                {
                    Initialise();
                }

                if (_vibrator == null)
                {
                    Handheld.Vibrate();
                    return;
                }

                float clamped = Mathf.Clamp01(strength);
                long milliseconds = (long)Mathf.Lerp(MIN_MILLISECONDS, MAX_MILLISECONDS, clamped);
                int amplitude = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(60f, 255f, clamped)), 1, 255);

                jvalue[] oneShotArgs = new jvalue[2];
                oneShotArgs[0].j = milliseconds;
                oneShotArgs[1].i = amplitude;

                IntPtr effect = AndroidJNI.CallStaticObjectMethod(
                    _effectClass.GetRawClass(), _createOneShotMethod, oneShotArgs);

                jvalue[] vibrateArgs = new jvalue[1];
                vibrateArgs[0].l = effect;
                AndroidJNI.CallVoidMethod(_vibrator.GetRawObject(), _vibrateMethod, vibrateArgs);
                AndroidJNI.DeleteLocalRef(effect);
            }
            catch (Exception exception)
            {
                _failed = true;
                Debug.LogWarning("[PoFootball] Haptics are off for this session: " + exception.Message);
            }
#else
            // Desktop and the Editor have nothing to buzz.
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void Initialise()
        {
            _initialised = true;

            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (_vibrator == null || !_vibrator.Call<bool>("hasAmplitudeControl"))
            {
                _vibrator?.Dispose();
                _vibrator = null;
                return;
            }

            _effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            _createOneShotMethod = AndroidJNI.GetStaticMethodID(
                _effectClass.GetRawClass(), "createOneShot", "(JI)Landroid/os/VibrationEffect;");

            using (AndroidJavaClass vibratorClass = new AndroidJavaClass("android.os.Vibrator"))
            {
                _vibrateMethod = AndroidJNI.GetMethodID(
                    vibratorClass.GetRawClass(), "vibrate", "(Landroid/os/VibrationEffect;)V");
            }
        }
#endif

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _vibrator?.Dispose();
            _effectClass?.Dispose();
            _vibrator = null;
            _effectClass = null;
#endif
        }
    }
}

using System;
using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// Reads the play-by-play aloud through Android's own text-to-speech engine.
    /// Silent everywhere else.
    ///
    /// THE DEVICE'S VOICE, NOT A SERVICE. android.speech.tts.TextToSpeech ships
    /// on every handset this game targets, works offline, and costs nothing per
    /// line; a cloud voice would be a network request per play — which the
    /// privacy policy says this app does not make — and a key in the APK.
    ///
    /// QUEUE_FLUSH, NOT QUEUE_ADD. At 2x or 4x playback the lines arrive faster
    /// than a voice can say them, and a queue would be narrating a play from a
    /// minute ago. Each new line cuts the last one off: the voice always describes
    /// the field in front of the viewer.
    ///
    /// EXPLICIT JNI SIGNATURE for speak(CharSequence, int, Bundle, String), for
    /// the reason Systems_Haptics gives: a C# string marshals as a String, and a
    /// lookup built from argument types would ask for speak(String, ...).
    ///
    /// The engine initialises asynchronously; lines that arrive before it is
    /// ready are dropped rather than queued, for the same reason as the flush.
    /// Any failure turns speech off for the session.
    /// </summary>
    public sealed class Systems_SpeechOutput : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int QUEUE_FLUSH = 0;
        private const int SUCCESS = 0;

        private AndroidJavaObject _engine;
        private IntPtr _speakMethod;
        private volatile bool _ready;
        private int _utterance;

        private sealed class InitListener : AndroidJavaProxy
        {
            private readonly Systems_SpeechOutput _owner;

            public InitListener(Systems_SpeechOutput owner)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _owner = owner;
            }

            // Called by name from Java, on a Java thread.
            public void onInit(int status)
            {
                _owner._ready = status == SUCCESS;
            }
        }
#endif

        private bool _failed;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android only: elsewhere nothing reads it, and an assignment the compiler
        // can see is never read is a CS0414 in every Editor console.
        private bool _started;
#endif

        public void Speak(string line)
        {
            if (_failed || string.IsNullOrEmpty(line) || !Systems_BroadcastSettings.Speech)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!_started)
                {
                    Start();
                    return;
                }

                if (!_ready || _engine == null)
                {
                    return;
                }

                IntPtr text = AndroidJNI.NewStringUTF(line);
                IntPtr id = AndroidJNI.NewStringUTF("pof" + (++_utterance));

                jvalue[] args = new jvalue[4];
                args[0].l = text;
                args[1].i = QUEUE_FLUSH;
                args[2].l = IntPtr.Zero;
                args[3].l = id;

                AndroidJNI.CallIntMethod(_engine.GetRawObject(), _speakMethod, args);
                AndroidJNI.DeleteLocalRef(text);
                AndroidJNI.DeleteLocalRef(id);
            }
            catch (Exception exception)
            {
                _failed = true;
                Debug.LogWarning("[PoFootball] Speech is off for this session: " + exception.Message);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void Start()
        {
            _started = true;

            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _engine = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech", activity, new InitListener(this));
            }

            using (AndroidJavaClass engineClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
            {
                _speakMethod = AndroidJNI.GetMethodID(
                    engineClass.GetRawClass(),
                    "speak",
                    "(Ljava/lang/CharSequence;ILandroid/os/Bundle;Ljava/lang/String;)I");
            }
        }
#endif

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_engine != null)
            {
                try
                {
                    _engine.Call("shutdown");
                }
                catch (Exception)
                {
                    // Shutting down a voice that never started is not worth a log line.
                }

                _engine.Dispose();
                _engine = null;
            }
#endif
        }
    }
}

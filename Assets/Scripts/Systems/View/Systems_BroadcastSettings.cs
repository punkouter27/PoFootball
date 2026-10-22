using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// Which broadcast graphics the viewer wants on screen: the space-control
    /// overlay, the win-probability ribbon, the call sheet, the commentary ticker
    /// and its voice, live weather, fatigue rings, hit haptics and the
    /// auto-director.
    ///
    /// PRESENTATION ONLY, EVERY ONE OF THEM. Nothing here reaches the simulation,
    /// so a viewer can switch any of it off mid-game without the game they are
    /// watching changing by a single tick — which is also why none of it needs a
    /// contract revision. Each view still asks Systems_PresentationBudget first;
    /// these settings only decide what a view that is ALLOWED to build shows.
    ///
    /// PlayerPrefs, static, loaded lazily and saved on every write, for the
    /// reasons Systems_AudioSettings and Systems_GameSettings give: a handful of
    /// flags, and one changed immediately before the app is killed must survive.
    /// Read every frame by some views, so the getters are a bool load after the
    /// first call and never touch PlayerPrefs.
    /// </summary>
    public static class Systems_BroadcastSettings
    {
        private const string SPACE_CONTROL_KEY = "PoFootball.Broadcast.SpaceControl";
        private const string WIN_PROBABILITY_KEY = "PoFootball.Broadcast.WinProbability";
        private const string CALL_SHEET_KEY = "PoFootball.Broadcast.CallSheet";
        private const string COMMENTARY_KEY = "PoFootball.Broadcast.Commentary";
        private const string SPEECH_KEY = "PoFootball.Broadcast.Speech";
        private const string LIVE_WEATHER_KEY = "PoFootball.Broadcast.LiveWeather";
        private const string HAPTICS_KEY = "PoFootball.Broadcast.Haptics";
        private const string AUTO_DIRECTOR_KEY = "PoFootball.Broadcast.AutoDirector";
        private const string FATIGUE_RINGS_KEY = "PoFootball.Broadcast.FatigueRings";

        private static bool _loaded;
        private static bool _spaceControl;
        private static bool _winProbability;
        private static bool _callSheet;
        private static bool _commentary;
        private static bool _speech;
        private static bool _liveWeather;
        private static bool _haptics;
        private static bool _autoDirector;
        private static bool _fatigueRings;

        /// <summary>The time-to-reach heatmap on the turf and the separation tags.</summary>
        public static bool SpaceControl
        {
            get { Load(); return _spaceControl; }
            set { Load(); _spaceControl = value; Save(SPACE_CONTROL_KEY, value); }
        }

        /// <summary>The win-probability ribbon under the scoreboard.</summary>
        public static bool WinProbability
        {
            get { Load(); return _winProbability; }
            set { Load(); _winProbability = value; Save(WIN_PROBABILITY_KEY, value); }
        }

        /// <summary>The coordinators' call sheet card before and after the snap.</summary>
        public static bool CallSheet
        {
            get { Load(); return _callSheet; }
            set { Load(); _callSheet = value; Save(CALL_SHEET_KEY, value); }
        }

        /// <summary>The play-by-play ticker.</summary>
        public static bool Commentary
        {
            get { Load(); return _commentary; }
            set { Load(); _commentary = value; Save(COMMENTARY_KEY, value); }
        }

        /// <summary>
        /// The ticker read aloud by the device's text-to-speech. Off by default: a
        /// voice nobody asked for is the fastest way to get a game muted.
        /// </summary>
        public static bool Speech
        {
            get { Load(); return _speech; }
            set { Load(); _speech = value; Save(SPEECH_KEY, value); }
        }

        /// <summary>
        /// Whether the weather is fetched for a real stadium city at kickoff. Off,
        /// the sky is drawn from the game seed and nothing leaves the device.
        ///
        /// ON BY DEFAULT SINCE privacy-policy.html (22 October 2026) DESCRIBES IT:
        /// one request per game to open-meteo.com, carrying a fixed city's
        /// coordinates. It was off while the policy still said the app made no
        /// network requests. The Play data-safety form has to say the same before a
        /// build with this default ships.
        /// </summary>
        public static bool LiveWeather
        {
            get { Load(); return _liveWeather; }
            set { Load(); _liveWeather = value; Save(LIVE_WEATHER_KEY, value); }
        }

        /// <summary>The fatigue ring under every tiring player.</summary>
        public static bool FatigueRings
        {
            get { Load(); return _fatigueRings; }
            set { Load(); _fatigueRings = value; Save(FATIGUE_RINGS_KEY, value); }
        }

        /// <summary>A vibration on a big hit, Android only.</summary>
        public static bool Haptics
        {
            get { Load(); return _haptics; }
            set { Load(); _haptics = value; Save(HAPTICS_KEY, value); }
        }

        /// <summary>The broadcast camera choosing its own shot for the situation.</summary>
        public static bool AutoDirector
        {
            get { Load(); return _autoDirector; }
            set { Load(); _autoDirector = value; Save(AUTO_DIRECTOR_KEY, value); }
        }

        private static void Save(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _spaceControl = PlayerPrefs.GetInt(SPACE_CONTROL_KEY, 1) != 0;
            _winProbability = PlayerPrefs.GetInt(WIN_PROBABILITY_KEY, 1) != 0;
            _callSheet = PlayerPrefs.GetInt(CALL_SHEET_KEY, 1) != 0;
            _commentary = PlayerPrefs.GetInt(COMMENTARY_KEY, 1) != 0;
            _speech = PlayerPrefs.GetInt(SPEECH_KEY, 0) != 0;
            _liveWeather = PlayerPrefs.GetInt(LIVE_WEATHER_KEY, 1) != 0;
            _haptics = PlayerPrefs.GetInt(HAPTICS_KEY, 1) != 0;
            _autoDirector = PlayerPrefs.GetInt(AUTO_DIRECTOR_KEY, 1) != 0;
            _fatigueRings = PlayerPrefs.GetInt(FATIGUE_RINGS_KEY, 1) != 0;
        }

        /// <summary>See Systems_GameSettings.ResetState.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _loaded = false;
        }
    }
}

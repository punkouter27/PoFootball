namespace PoFootball.Models
{
    /// <summary>
    /// The conditions the game is being played in: which city, how cold, what is
    /// falling, which way the wind blows, and whether it is night. Written once a
    /// game by Systems_WeatherSystem; read by Systems_WeatherView, the stadium
    /// rig and the HUD.
    ///
    /// NOTHING ON THE FIELD READS IT, AND THAT IS THE WHOLE DESIGN. Every brain
    /// was fitted against one set of dynamics, so weather here is something the
    /// viewer sees and the players do not feel. A wet ball would be a contract
    /// revision, observed and randomised in training like the roster's traits.
    ///
    /// <see cref="Revision"/> instead of a subscription. The model changes at most
    /// twice a game — the city at kickoff and the conditions a few seconds later —
    /// and every reader already has an Update; comparing one int a frame is
    /// cheaper than a message broker for an event that happens twice.
    ///
    /// <see cref="DisplayLine"/> is built when the conditions are written, never
    /// when they are read, so a HUD that shows it every frame allocates nothing.
    /// </summary>
    public sealed class Systems_WeatherModel
    {
        private static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Upper-case city name, empty until the game has picked one.</summary>
        public string City { get; private set; } = string.Empty;

        /// <summary>
        /// False until the conditions are known — up to the live feed's timeout
        /// after kickoff. A reader must draw nothing rather than a clear sky that
        /// turns into a blizzard four seconds in.
        /// </summary>
        public bool IsReady { get; private set; }

        public Systems_WeatherCondition Condition { get; private set; } = Systems_WeatherCondition.Clear;

        public float TemperatureCelsius { get; private set; }

        /// <summary>Kilometres an hour, ten metres up, as the forecast reports it.</summary>
        public float WindSpeedKmh { get; private set; }

        /// <summary>
        /// Degrees clockwise from north that the wind blows FROM — the
        /// meteorological convention, so 315 is a north-westerly blowing toward the
        /// south-east. The view turns it around; the HUD prints it as given.
        /// </summary>
        public float WindFromDegrees { get; private set; }

        /// <summary>0 to 1: drizzle to downpour, flurries to a whiteout.</summary>
        public float Intensity { get; private set; }

        /// <summary>False for a night game, which the rig plays darker and bluer.</summary>
        public bool IsDay { get; private set; } = true;

        /// <summary>True when the conditions came from the live forecast rather than the seed.</summary>
        public bool IsLive { get; private set; }

        /// <summary>
        /// "DENVER · 3°C · SNOW · WIND 18 NW". Empty until <see cref="IsReady"/>.
        /// </summary>
        public string DisplayLine { get; private set; } = string.Empty;

        /// <summary>Moves on every write. Compare against the last value seen.</summary>
        public int Revision { get; private set; }

        // --- Mutators. Systems_WeatherSystem only. ---------------------------

        public void SetCity(string city)
        {
            City = city ?? string.Empty;
            IsReady = false;
            DisplayLine = string.Empty;
            Revision++;
        }

        public void SetConditions(
            Systems_WeatherCondition condition,
            float temperatureCelsius,
            float windSpeedKmh,
            float windFromDegrees,
            float intensity,
            bool isDay,
            bool isLive)
        {
            Condition = condition;
            TemperatureCelsius = temperatureCelsius;
            WindSpeedKmh = windSpeedKmh < 0f ? 0f : windSpeedKmh;
            WindFromDegrees = ((windFromDegrees % 360f) + 360f) % 360f;
            Intensity = intensity < 0f ? 0f : (intensity > 1f ? 1f : intensity);
            IsDay = isDay;
            IsLive = isLive;
            IsReady = true;
            DisplayLine = BuildDisplayLine();
            Revision++;
        }

        private string BuildDisplayLine()
        {
            int temperature = (int)System.Math.Round(TemperatureCelsius);
            int wind = (int)System.Math.Round(WindSpeedKmh);

            string sky = !IsDay && Condition == Systems_WeatherCondition.Clear
                ? "CLEAR NIGHT"
                : ConditionName(Condition);

            // A calm day has no direction worth printing, and "WIND 0 N" reads as
            // a fault rather than as still air.
            string windPart = wind < 3
                ? "CALM"
                : "WIND " + wind + " " + CompassPoint(WindFromDegrees);

            return City + " · " + temperature + "°C · " + sky + " · " + windPart;
        }

        private static string ConditionName(Systems_WeatherCondition condition)
        {
            switch (condition)
            {
                case Systems_WeatherCondition.Cloudy:
                    return "CLOUDY";
                case Systems_WeatherCondition.Rain:
                    return "RAIN";
                case Systems_WeatherCondition.Snow:
                    return "SNOW";
                case Systems_WeatherCondition.Fog:
                    return "FOG";
                default:
                    return "CLEAR";
            }
        }

        private static string CompassPoint(float degrees)
        {
            int index = (int)System.Math.Round(degrees / 45f) % CompassPoints.Length;
            return CompassPoints[index];
        }
    }
}

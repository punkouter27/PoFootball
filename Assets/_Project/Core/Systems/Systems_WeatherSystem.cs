using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using PoFootball.Models;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Networking;

namespace PoFootball.Systems
{
    /// <summary>
    /// Decides where the game is being played and what the sky is doing there, and
    /// writes it into <see cref="Systems_WeatherModel"/> once.
    ///
    /// THE CITY COMES FROM THE GAME SEED, SO A FIXED-SEED GAME IS ALWAYS IN THE
    /// SAME PLACE. "Watch that game again" (Systems_EpisodeSeed) should not move
    /// it from a snowy northern city to a southern dome. The draw is its own
    /// Unity.Mathematics.Random, hashed off the seed with a salt, so it consumes
    /// nothing from the spot, formation or skill streams — adding weather could
    /// not change a single play.
    ///
    /// THE CONDITIONS COME FROM THE LIVE FORECAST WHEN THE VIEWER ALLOWS IT, AND
    /// FROM THE SAME SEED WHEN THEY DO NOT. Open-Meteo is free, needs no key, and
    /// is asked for the current conditions at the chosen city's coordinates and
    /// nothing else. If it is switched off, slow, offline or returns something
    /// unreadable, the seeded draw stands in, so the game always has a sky. The
    /// one consequence of the live path is that a replayed seed can come back to
    /// different weather on a different day; the football is identical either way,
    /// because nothing on the field reads this.
    ///
    /// IT NEVER HOLDS UP KICKOFF. <see cref="Begin"/> returns at once; the request
    /// runs on UniTask behind a four-second timeout, and the model reports
    /// IsReady false until it lands, so the view draws nothing rather than a clear
    /// sky that turns into a downpour a few seconds in.
    ///
    /// WHY A VIEW STARTS IT. The setting that allows the network lives in
    /// PoFootball.Views (Systems_BroadcastSettings), which this assembly cannot
    /// see, and "is anyone watching" is Systems_PresentationBudget's question, which
    /// Systems_WeatherView already asks before it builds anything. So the view
    /// calls Begin with the answer, and a training run or a headless env — which
    /// has no weather view — never reaches the network at all.
    /// </summary>
    public sealed class Systems_WeatherSystem : IDisposable
    {
        private const string ENDPOINT =
            "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}"
            + "&current=temperature_2m,precipitation,rain,snowfall,weather_code,"
            + "wind_speed_10m,wind_direction_10m,is_day&wind_speed_unit=kmh";

        /// <summary>
        /// Seconds. Long enough for a phone on a poor connection, short enough that
        /// the weather is settled before the opening kickoff has come down.
        /// </summary>
        private const int REQUEST_TIMEOUT_SECONDS = 4;

        /// <summary>Keeps the city draw off every other stream seeded from the same value.</summary>
        private const uint CITY_SALT = 0x5EA7_7E12u;

        /// <summary>Precipitation, mm an hour, that reads as a downpour.</summary>
        private const float HEAVY_RAIN_MM = 6f;

        /// <summary>Snowfall, cm an hour, that reads as a whiteout.</summary>
        private const float HEAVY_SNOW_CM = 2f;

        /// <summary>Weakest rain or snow the view draws, so a trace still reads as weather.</summary>
        private const float MIN_INTENSITY = 0.3f;

        /// <summary>
        /// Generic city names and coordinates, with a typical game-day temperature
        /// for the seeded fallback. Cities, never teams: no club names, colours or
        /// marks anywhere in this project.
        /// </summary>
        private static readonly string[] CityNames =
        {
            "DENVER", "CHICAGO", "SEATTLE", "MIAMI", "BOSTON", "BUFFALO",
            "MINNEAPOLIS", "PHOENIX", "NEW ORLEANS", "KANSAS CITY", "PITTSBURGH",
            "SAN FRANCISCO",
        };

        private static readonly float[] CityLatitudes =
        {
            39.7392f, 41.8781f, 47.6062f, 25.7617f, 42.3601f, 42.8864f,
            44.9778f, 33.4484f, 29.9511f, 39.0997f, 40.4406f, 37.7749f,
        };

        private static readonly float[] CityLongitudes =
        {
            -104.9903f, -87.6298f, -122.3321f, -80.1918f, -71.0589f, -78.8784f,
            -93.2650f, -112.0740f, -90.0715f, -94.5786f, -79.9959f, -122.4194f,
        };

        private static readonly float[] CityTypicalCelsius =
        {
            6f, 5f, 9f, 25f, 6f, 3f, 1f, 22f, 19f, 8f, 6f, 14f,
        };

        private readonly Systems_WeatherModel _model;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private bool _begun;

        public Systems_WeatherSystem(Systems_WeatherModel model)
        {
            _model = model;
        }

        /// <summary>
        /// Picks the city and starts resolving the conditions. Once a game: a
        /// second call is ignored, so a view rebuilt mid-game cannot re-roll the sky.
        /// </summary>
        public void Begin(bool allowNetwork)
        {
            if (_begun || _model == null)
            {
                return;
            }

            _begun = true;

            Unity.Mathematics.Random random = SeededRandom(Systems_EpisodeSeed.Value);
            int city = random.NextInt(0, CityNames.Length);

            _model.SetCity(CityNames[city]);

            if (!allowNetwork)
            {
                ApplySeeded(ref random, city);
                return;
            }

            ResolveLiveAsync(city, random, _cancellation.Token).Forget();
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }

        private static Unity.Mathematics.Random SeededRandom(uint seed)
        {
            // Never zero: Unity.Mathematics.Random rejects it.
            return new Unity.Mathematics.Random(math.hash(new uint2(seed, CITY_SALT)) | 1u);
        }

        /// <summary>
        /// Mostly clear, sometimes cloudy, sometimes wet — and snow only where the
        /// drawn temperature is at freezing, so Miami never gets a blizzard from
        /// the fallback.
        /// </summary>
        private void ApplySeeded(ref Unity.Mathematics.Random random, int city)
        {
            float temperature = CityTypicalCelsius[city] + random.NextFloat(-8f, 8f);
            float roll = random.NextFloat();

            Systems_WeatherCondition condition;

            if (roll < 0.55f)
            {
                condition = Systems_WeatherCondition.Clear;
            }
            else if (roll < 0.72f)
            {
                condition = Systems_WeatherCondition.Cloudy;
            }
            else if (roll < 0.93f)
            {
                condition = temperature <= 1f
                    ? Systems_WeatherCondition.Snow
                    : Systems_WeatherCondition.Rain;
            }
            else
            {
                condition = Systems_WeatherCondition.Fog;
            }

            // Squared, so most games are calm and a gale is rare.
            float gust = random.NextFloat();
            float windSpeed = gust * gust * 38f;
            float windFrom = random.NextFloat(0f, 360f);
            float intensity = random.NextFloat(0.35f, 1f);
            bool isDay = random.NextFloat() < 0.6f;

            _model.SetConditions(
                condition, temperature, windSpeed, windFrom, intensity, isDay, isLive: false);
        }

        /// <summary>
        /// One request, one answer, then done. Every failure — timeout, no
        /// network, a 5xx, a body that does not parse — lands in the seeded draw;
        /// a cancelled game (the scene unloading) lands nowhere.
        /// </summary>
        private async UniTaskVoid ResolveLiveAsync(
            int city, Unity.Mathematics.Random random, CancellationToken token)
        {
            OpenMeteoCurrent current = null;

            try
            {
                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    ENDPOINT,
                    CityLatitudes[city].ToString("F4", CultureInfo.InvariantCulture),
                    CityLongitudes[city].ToString("F4", CultureInfo.InvariantCulture));

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = REQUEST_TIMEOUT_SECONDS;

                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();

                    try
                    {
                        await UniTask.WaitUntil(
                            () => operation.isDone, cancellationToken: token);
                    }
                    catch (OperationCanceledException)
                    {
                        request.Abort();
                        throw;
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        current = Parse(request.downloadHandler.text);
                    }
                    else
                    {
                        Systems_Log.Info(
                            $"[PoFootball] Weather: live forecast unavailable ({request.error}); "
                            + "using the seeded sky.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Systems_Log.Info(
                    $"[PoFootball] Weather: live forecast failed ({exception.Message}); "
                    + "using the seeded sky.");
                current = null;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (current == null)
            {
                ApplySeeded(ref random, city);
                return;
            }

            ApplyLive(current);
        }

        private static OpenMeteoCurrent Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            OpenMeteoResponse response = JsonUtility.FromJson<OpenMeteoResponse>(json);

            // The sentinels below survive only when the field was absent, which is
            // how a body that is valid JSON but not a forecast is told apart.
            if (response == null || response.current == null
                || response.current.weather_code < 0 || response.current.is_day < 0)
            {
                return null;
            }

            return response.current;
        }

        /// <summary>
        /// WMO codes reduced to what can be drawn. Snow is tested before rain
        /// because sleet reports both and reads as snow from above; the code is the
        /// fallback when the amounts are zero but the sky is still doing something.
        /// </summary>
        private void ApplyLive(OpenMeteoCurrent current)
        {
            int code = current.weather_code;
            Systems_WeatherCondition condition;
            float intensity;

            if (current.snowfall > 0f || (code >= 71 && code <= 77) || code == 85 || code == 86)
            {
                condition = Systems_WeatherCondition.Snow;
                intensity = Mathf.Clamp(current.snowfall / HEAVY_SNOW_CM, MIN_INTENSITY, 1f);
            }
            else if (current.rain > 0f || current.precipitation > 0f
                || (code >= 51 && code <= 67) || (code >= 80 && code <= 82) || code >= 95)
            {
                condition = Systems_WeatherCondition.Rain;
                intensity = code >= 95
                    ? 1f
                    : Mathf.Clamp(
                        Mathf.Max(current.rain, current.precipitation) / HEAVY_RAIN_MM,
                        MIN_INTENSITY,
                        1f);
            }
            else if (code == 45 || code == 48)
            {
                condition = Systems_WeatherCondition.Fog;
                intensity = 0.7f;
            }
            else if (code == 2 || code == 3)
            {
                condition = Systems_WeatherCondition.Cloudy;
                intensity = 0f;
            }
            else
            {
                condition = Systems_WeatherCondition.Clear;
                intensity = 0f;
            }

            _model.SetConditions(
                condition,
                current.temperature_2m,
                current.wind_speed_10m,
                current.wind_direction_10m,
                intensity,
                current.is_day != 0,
                isLive: true);
        }

        // The two shapes below are Open-Meteo's JSON, and JsonUtility maps by field
        // name — so these are public snake_case fields, the one place in the
        // project that breaks the naming rule, because the wire format chose it.

        [Serializable]
        private sealed class OpenMeteoResponse
        {
            public OpenMeteoCurrent current;
        }

        [Serializable]
        private sealed class OpenMeteoCurrent
        {
            public float temperature_2m;
            public float precipitation;
            public float rain;
            public float snowfall;
            public int weather_code = -1;
            public float wind_speed_10m;
            public float wind_direction_10m;
            public int is_day = -1;
        }
    }
}

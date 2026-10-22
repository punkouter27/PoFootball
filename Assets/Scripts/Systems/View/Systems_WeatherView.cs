using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// The sky over the game: rain or snow falling across the broadcast shot, a
    /// haze for fog and for the air in a downpour, and snow settling on the turf
    /// as the game goes on. The light rig's share of the weather — darker, cooler,
    /// night — is Systems_StadiumRigView's, read off the same model.
    ///
    /// NONE OF THIS IS ON THE FIELD. The particles are drawn over the camera's
    /// view, the haze is a quad that follows the camera and the dusting is a quad
    /// over the turf; there is no collider, no force and nothing a brain observes.
    /// A wet ball would be a contract revision; this is a picture of one.
    ///
    /// IT STARTS THE WEATHER, AS WELL AS DRAWING IT. Systems_WeatherSystem cannot
    /// read Systems_BroadcastSettings (that lives in this assembly) and should not
    /// touch the network in a training run (which has no view to ask). So this
    /// view, once Systems_PresentationBudget has said yes, hands the system the
    /// viewer's live-weather setting and the system resolves the rest.
    ///
    /// THE COSTS. One ParticleSystem, configured once the conditions are known
    /// and capped at <see cref="MAX_PARTICLES"/> — about three hundred live at
    /// the default zoom, which is the budget a phone's fill rate can afford over
    /// the whole screen. One haze quad and one dusting quad, both on the shared
    /// particle material, both off in clear weather. The emitter follows the
    /// camera in LateUpdate, after Systems_BroadcastCameraView (order -40) has
    /// placed it, and the emission rate is only rewritten when the zoom has
    /// changed enough to matter. Nothing allocates per frame.
    ///
    /// UNSCALED TIME THROUGHOUT. Weather is not game time: at Sim Speed 8x the
    /// play is eight times faster and the rain is not.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_WeatherView : MonoBehaviour, Systems_IInjectableView
    {
        /// <summary>Over the bodies (1-2), the intent overlay (3), the ball (5) and the hit sparks (6).</summary>
        private const int PRECIPITATION_SORTING_ORDER = 7;

        /// <summary>Over the precipitation: fog sits between the viewer and everything.</summary>
        private const int HAZE_SORTING_ORDER = 8;

        /// <summary>With the turf scuffs and the drive trail, under every body.</summary>
        private const int DUSTING_SORTING_ORDER = 1;

        /// <summary>
        /// Behind the plane of play, so a dusting renderer that shares a sorting
        /// order with a player is drawn first — the same trick
        /// Systems_DriveTrailView uses.
        /// </summary>
        private const float DUSTING_DEPTH = 0.06f;

        private const int MAX_PARTICLES = 360;

        /// <summary>Haze and emitter box beyond the visible frame, so a fast pan never shows an edge.</summary>
        private const float FRAME_OVERSCAN = 1.3f;

        /// <summary>Relative change in camera size before the box and rate are rewritten.</summary>
        private const float RESIZE_THRESHOLD = 0.05f;

        // Rain: short pale streaks, falling down the screen and blown sideways.
        private const float RAIN_LIFETIME = 0.45f;
        private const float RAIN_WIDTH = 0.05f;
        private const float RAIN_FALL_SPEED = 16f;
        private const float RAIN_STREAK_SCALE = 0.06f;

        /// <summary>New streaks per square metre of frame per second at full intensity.</summary>
        private const float RAIN_DENSITY = 0.24f;

        private static readonly Color RainColor = new Color(0.82f, 0.88f, 0.98f, 0.38f);

        // Snow: slow, fluttering flakes.
        private const float SNOW_LIFETIME = 4.5f;
        private const float SNOW_SIZE_MIN = 0.12f;
        private const float SNOW_SIZE_MAX = 0.24f;
        private const float SNOW_FALL_SPEED = 1.4f;
        private const float SNOW_DENSITY = 0.032f;

        private static readonly Color SnowColor = new Color(1f, 1f, 1f, 0.78f);

        /// <summary>
        /// Metres a second of screen drift per metre a second of wind. Under one:
        /// the forecast's wind is ten metres up and the picture is a long lens.
        /// </summary>
        private const float RAIN_WIND_DRIFT = 0.8f;
        private const float SNOW_WIND_DRIFT = 0.35f;

        /// <summary>
        /// Alpha the snow on the turf builds to, and the game seconds it takes.
        /// Low on purpose: the yard lines are how a viewer reads the down, and a
        /// dusting that hid them would be weather at the game's expense.
        /// </summary>
        private const float DUSTING_MAX_ALPHA = 0.11f;
        private const float DUSTING_BUILD_SECONDS = 150f;

        private static readonly Color DustingColor = new Color(0.94f, 0.96f, 1f, 1f);

        private Systems_PresentationBudget _budget;
        private Systems_WeatherModel _weather;
        private Systems_WeatherSystem _weatherSystem;

        private Camera _camera;
        private Material _material;

        /// <summary>The precipitation's own instance of the particle material, with a soft dot.</summary>
        private Material _flakeMaterial;

        private Texture2D _flakeTexture;

        /// <summary>Side of the generated flake texture, pixels.</summary>
        private const int FLAKE_TEXTURE_SIZE = 32;

        private ParticleSystem _precipitation;
        private Transform _precipitationTransform;

        private Mesh _hazeMesh;
        private MeshRenderer _hazeRenderer;
        private Transform _hazeTransform;
        private readonly Color32[] _hazeColors = new Color32[5];

        private Mesh _dustingMesh;
        private MeshRenderer _dustingRenderer;
        private readonly Color32[] _dustingColors = new Color32[4];
        private float _dustingAlpha;
        private float _dustingShownAlpha = -1f;

        private int _weatherRevision = -1;
        private Systems_WeatherCondition _condition = Systems_WeatherCondition.Clear;
        private float _density;
        private float _lastCameraSize = -1f;
        private float _lastCameraAspect = -1f;

        [Inject]
        public void Construct(
            Systems_PresentationBudget budget,
            Systems_WeatherModel weather,
            Systems_WeatherSystem weatherSystem)
        {
            _budget = budget;
            _weather = weather;
            _weatherSystem = weatherSystem;
        }

        private void Start()
        {
            if (_budget == null || !_budget.EffectsEnabled
                || _weather == null || _weatherSystem == null)
            {
                enabled = false;
                return;
            }

            _camera = Camera.main;

            if (_camera == null
                || !Systems_ParticleMaterial.TryLoad(
                    nameof(Systems_WeatherView), "No weather is drawn.", out _material))
            {
                // The model still gets its conditions, so the HUD's weather line
                // is right even on a device with no particle material.
                _weatherSystem.Begin(Systems_BroadcastSettings.LiveWeather);
                enabled = false;
                return;
            }

            BuildPrecipitation();
            BuildHaze();
            BuildDusting();

            _weatherSystem.Begin(Systems_BroadcastSettings.LiveWeather);
        }

        /// <summary>
        /// The shared particle material carries no texture, so every particle drawn
        /// with it is a square — which a capture showed snow to be, a field of
        /// grey pixels. The haze and dusting meshes want exactly that flat fill, so
        /// only the precipitation gets an instance, with a radial falloff drawn once
        /// here; stretched along the fall it is a rain streak, billboarded it is a
        /// flake. Owned and destroyed by this view, as Systems_BallTrailView does
        /// with its own instance.
        /// </summary>
        private Material BuildFlakeMaterial()
        {
            _flakeTexture = new Texture2D(FLAKE_TEXTURE_SIZE, FLAKE_TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                name = "PoFootball Weather Flake",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float centre = (FLAKE_TEXTURE_SIZE - 1) * 0.5f;
            Color32[] pixels = new Color32[FLAKE_TEXTURE_SIZE * FLAKE_TEXTURE_SIZE];

            for (int y = 0; y < FLAKE_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < FLAKE_TEXTURE_SIZE; x++)
                {
                    float distance = new Vector2(x - centre, y - centre).magnitude / centre;
                    float alpha = Mathf.Clamp01(1f - distance);
                    byte value = (byte)Mathf.RoundToInt(255f * alpha * alpha);
                    pixels[(y * FLAKE_TEXTURE_SIZE) + x] = new Color32(255, 255, 255, value);
                }
            }

            _flakeTexture.SetPixels32(pixels);
            _flakeTexture.Apply(false, true);

            _flakeMaterial = new Material(_material) { name = "PoFootball Weather Flake" };
            _flakeMaterial.mainTexture = _flakeTexture;
            return _flakeMaterial;
        }

        private void OnDestroy()
        {
            if (_flakeMaterial != null)
            {
                Destroy(_flakeMaterial);
            }

            if (_flakeTexture != null)
            {
                Destroy(_flakeTexture);
            }

            if (_hazeMesh != null)
            {
                Destroy(_hazeMesh);
            }

            if (_dustingMesh != null)
            {
                Destroy(_dustingMesh);
            }
        }

        private void Update()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            if (_weather.Revision != _weatherRevision)
            {
                _weatherRevision = _weather.Revision;

                if (_weather.IsReady)
                {
                    Configure();
                }
            }

            TickDusting();
        }

        private void LateUpdate()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            if (_condition == Systems_WeatherCondition.Clear
                || _condition == Systems_WeatherCondition.Cloudy)
            {
                return;
            }

            Vector3 cameraPosition = _camera.transform.position;
            Vector3 framePosition = new Vector3(cameraPosition.x, cameraPosition.y, 0f);

            _precipitationTransform.position = framePosition;
            _hazeTransform.position = framePosition;

            float size = _camera.orthographicSize;
            float aspect = _camera.aspect;

            if (Mathf.Abs(size - _lastCameraSize) > _lastCameraSize * RESIZE_THRESHOLD
                || !Mathf.Approximately(aspect, _lastCameraAspect))
            {
                Resize(size, aspect);
            }
        }

        // --- Building -------------------------------------------------------

        /// <summary>
        /// The one ParticleSystem, built stopped and empty. What it emits is set by
        /// <see cref="Configure"/> when the conditions arrive; until then it costs
        /// nothing, because a stopped system does not simulate.
        /// </summary>
        private void BuildPrecipitation()
        {
            GameObject host = new GameObject("WeatherPrecipitation");
            host.transform.SetParent(transform, false);
            _precipitationTransform = host.transform;

            _precipitation = host.AddComponent<ParticleSystem>();
            _precipitation.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _precipitation.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles = MAX_PARTICLES;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            // Zero until Resize knows how big the frame is. A new system's default
            // is ten a second from a one-metre box at the origin.
            ParticleSystem.EmissionModule emission = _precipitation.emission;
            emission.rateOverTimeMultiplier = 0f;

            ParticleSystem.ShapeModule shape = _precipitation.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1f, 1f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = _precipitation.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            // Fade in and out over each particle's life, so nothing pops into or
            // out of existence in the middle of the frame.
            ParticleSystem.ColorOverLifetimeModule fade = _precipitation.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(BuildFadeGradient());

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = BuildFlakeMaterial();
            renderer.sortingOrder = PRECIPITATION_SORTING_ORDER;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
        }

        /// <summary>
        /// A five-vertex fan: thicker at the four corners than in the middle, so
        /// fog closes in from the edges of the frame and the play in the centre
        /// stays the clearest thing on screen.
        /// </summary>
        private void BuildHaze()
        {
            _hazeMesh = new Mesh { name = "PoFootball Weather Haze" };
            _hazeMesh.MarkDynamic();
            _hazeMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                Vector3.zero,
            };
            _hazeMesh.triangles = new[] { 4, 1, 0, 4, 2, 1, 4, 3, 2, 4, 0, 3 };
            _hazeMesh.colors32 = _hazeColors;

            _hazeRenderer = BuildQuadRenderer("WeatherHaze", _hazeMesh, HAZE_SORTING_ORDER);
            _hazeTransform = _hazeRenderer.transform;
        }

        /// <summary>The turf's snow cover: one quad over the whole field, end line to end line.</summary>
        private void BuildDusting()
        {
            float halfWidth = Systems_FieldModel.HALF_WIDTH;
            float halfLength = Systems_FieldModel.TOTAL_LENGTH * 0.5f;

            _dustingMesh = new Mesh { name = "PoFootball Weather Dusting" };
            _dustingMesh.vertices = new[]
            {
                new Vector3(-halfWidth, -halfLength, 0f),
                new Vector3(halfWidth, -halfLength, 0f),
                new Vector3(halfWidth, halfLength, 0f),
                new Vector3(-halfWidth, halfLength, 0f),
            };
            _dustingMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _dustingMesh.colors32 = _dustingColors;

            _dustingRenderer = BuildQuadRenderer("WeatherDusting", _dustingMesh, DUSTING_SORTING_ORDER);
            _dustingRenderer.transform.position = new Vector3(0f, 0f, DUSTING_DEPTH);
        }

        private MeshRenderer BuildQuadRenderer(string objectName, Mesh mesh, int sortingOrder)
        {
            GameObject host = new GameObject(objectName);
            host.transform.SetParent(transform, false);

            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = host.AddComponent<MeshRenderer>();

            // sharedMaterial, never material (.claude/rules/performance.md). Every
            // colour arrives through the vertex stream.
            meshRenderer.sharedMaterial = _material;
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.enabled = false;
            return meshRenderer;
        }

        private static Gradient BuildFadeGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        // --- Conditions ------------------------------------------------------

        /// <summary>
        /// Sets everything up for the conditions in the model. Runs once a game in
        /// practice, so the configuration below allocates and nobody minds.
        /// </summary>
        private void Configure()
        {
            _condition = _weather.Condition;

            float intensity = Mathf.Lerp(0.45f, 1f, _weather.Intensity);
            Vector2 windDrift = WindVector(_weather.WindFromDegrees) * (_weather.WindSpeedKmh / 3.6f);

            _precipitation.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystemRenderer renderer = _precipitation.GetComponent<ParticleSystemRenderer>();
            ParticleSystem.MainModule main = _precipitation.main;
            ParticleSystem.VelocityOverLifetimeModule velocity = _precipitation.velocityOverLifetime;
            ParticleSystem.NoiseModule noise = _precipitation.noise;

            _density = 0f;

            switch (_condition)
            {
                case Systems_WeatherCondition.Rain:
                {
                    main.startLifetime = RAIN_LIFETIME;
                    main.startSize = RAIN_WIDTH;
                    main.startColor = RainColor;

                    Vector2 drift = windDrift * RAIN_WIND_DRIFT;
                    velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
                    velocity.y = new ParticleSystem.MinMaxCurve(drift.y - RAIN_FALL_SPEED);
                    velocity.z = new ParticleSystem.MinMaxCurve(0f);

                    noise.enabled = false;

                    // Stretched along the velocity, which is what makes a dot a
                    // streak and a streak read as rain rather than as sleet.
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = RAIN_STREAK_SCALE;
                    renderer.lengthScale = 1f;

                    _density = RAIN_DENSITY * intensity;
                    SetHaze(new Color(0.7f, 0.76f, 0.84f, 1f), 0.16f * intensity, 0.06f * intensity);
                    break;
                }
                case Systems_WeatherCondition.Snow:
                {
                    main.startLifetime = SNOW_LIFETIME;
                    main.startSize = new ParticleSystem.MinMaxCurve(SNOW_SIZE_MIN, SNOW_SIZE_MAX);
                    main.startColor = SnowColor;

                    Vector2 drift = windDrift * SNOW_WIND_DRIFT;
                    velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
                    velocity.y = new ParticleSystem.MinMaxCurve(drift.y - SNOW_FALL_SPEED);
                    velocity.z = new ParticleSystem.MinMaxCurve(0f);

                    // The flutter. Cheap at this particle count, and without it
                    // snow falls in perfectly straight parallel lines like rain
                    // in slow motion.
                    noise.enabled = true;
                    noise.strength = 0.6f;
                    noise.frequency = 0.4f;
                    noise.scrollSpeed = 0.3f;
                    noise.quality = ParticleSystemNoiseQuality.Low;

                    renderer.renderMode = ParticleSystemRenderMode.Billboard;

                    _density = SNOW_DENSITY * intensity;
                    SetHaze(new Color(0.92f, 0.94f, 0.98f, 1f), 0.14f * intensity, 0.05f * intensity);
                    break;
                }
                case Systems_WeatherCondition.Fog:
                    noise.enabled = false;
                    SetHaze(new Color(0.78f, 0.8f, 0.83f, 1f), 0.42f * intensity, 0.2f * intensity);
                    break;
                default:
                    noise.enabled = false;
                    _hazeRenderer.enabled = false;
                    break;
            }

            // Force LateUpdate to size the box and set the rate on its first pass.
            _lastCameraSize = -1f;
            _lastCameraAspect = -1f;

            if (_density > 0f)
            {
                _precipitation.Play();
            }
        }

        /// <summary>
        /// Rewrites the emitter box, the haze size and the emission rate for the
        /// frame the camera is showing now, so the density on screen is the same
        /// zoomed in on the pile as pulled out wide.
        /// </summary>
        private void Resize(float orthographicSize, float aspect)
        {
            _lastCameraSize = orthographicSize;
            _lastCameraAspect = aspect;

            float height = 2f * orthographicSize * FRAME_OVERSCAN;
            float width = height * aspect;

            _hazeTransform.localScale = new Vector3(width, height, 1f);

            if (_density <= 0f)
            {
                return;
            }

            ParticleSystem.ShapeModule shape = _precipitation.shape;
            shape.scale = new Vector3(width, height, 0f);

            ParticleSystem.EmissionModule emission = _precipitation.emission;
            emission.enabled = true;
            emission.rateOverTimeMultiplier = _density * width * height;
        }

        private void SetHaze(Color color, float edgeAlpha, float centreAlpha)
        {
            Color32 edge = WithAlpha(color, edgeAlpha);

            _hazeColors[0] = edge;
            _hazeColors[1] = edge;
            _hazeColors[2] = edge;
            _hazeColors[3] = edge;
            _hazeColors[4] = WithAlpha(color, centreAlpha);
            _hazeMesh.colors32 = _hazeColors;
            _hazeRenderer.enabled = edgeAlpha > 0f;
        }

        /// <summary>
        /// Snow settles while it falls and stays when it stops — game time, so a
        /// long drive in a blizzard leaves more on the ground than a quick one.
        /// The mesh is rewritten only when the alpha has moved a visible step.
        /// </summary>
        private void TickDusting()
        {
            if (_condition != Systems_WeatherCondition.Snow)
            {
                return;
            }

            float ceiling = DUSTING_MAX_ALPHA * Mathf.Lerp(0.5f, 1f, _weather.Intensity);

            _dustingAlpha = Mathf.MoveTowards(
                _dustingAlpha, ceiling, ceiling * Time.deltaTime / DUSTING_BUILD_SECONDS);

            if (Mathf.Abs(_dustingAlpha - _dustingShownAlpha) < 0.004f)
            {
                return;
            }

            _dustingShownAlpha = _dustingAlpha;

            Color32 color = WithAlpha(DustingColor, _dustingAlpha);

            for (int index = 0; index < _dustingColors.Length; index++)
            {
                _dustingColors[index] = color;
            }

            _dustingMesh.colors32 = _dustingColors;
            _dustingRenderer.enabled = _dustingAlpha > 0f;
        }

        /// <summary>
        /// The direction the wind blows TOWARD, on screen. The model holds the
        /// direction it blows FROM, clockwise from north; the field's +Y is taken
        /// as north, which is a convention and not a fact about any stadium.
        /// </summary>
        private static Vector2 WindVector(float fromDegrees)
        {
            float towardRadians = (fromDegrees + 180f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(towardRadians), Mathf.Cos(towardRadians));
        }

        private static Color32 WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }
}

using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// Moves the ball sprite to wherever the ball actually is.
    ///
    /// Before this existed the sprite sat wherever the scene author left it and
    /// never moved, so the yellow dot on screen had nothing to do with possession.
    /// It was also a live Rigidbody2D, making it a loose obstacle that 22 agents
    /// could shove around; the physics components are removed from the prefab.
    ///
    /// Reads the model directly in LateUpdate rather than subscribing, because the
    /// ball moves every tick and a per-change callback would allocate.
    ///
    /// THE ARC IS DRAWN, NOT SIMULATED. Systems_BallModel.Height is a presentation
    /// value (see that class), and this is the only thing that reads it. The sprite
    /// is offset up the screen by the height and a shadow is drawn at the ball's
    /// true plane position, so the gap between the two is what sells "the ball went
    /// over him". Without the shadow a lifted sprite just reads as a ball that
    /// drifted north, because a top-down view has no other depth cue.
    ///
    /// AND THE BALL COMES TOWARD THE CAMERA (2026-10-23). A full-speed pass peaks
    /// at about 3.3 m (PASS_LOFT_RATIO), so the lift alone was two metres of
    /// screen against a fifty-metre field and nobody could tell a lob from a
    /// bullet. Two more cues now ride on it: the ball grows as it climbs, as a
    /// nearer object does, and a faint tether runs from the shadow up to it, so
    /// the height reads as a length on the screen rather than an offset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_BallView : MonoBehaviour, Systems_IInjectableView
    {
        /// <summary>
        /// In front of every player (offense 1, defense 2) and the ball trail (4).
        /// See <see cref="ApplyBallSkin"/> — the scene had this at 0, behind
        /// everyone.
        /// </summary>
        private const int BALL_SORTING_ORDER = 5;

        /// <summary>
        /// Below the players so a ball passing over a defender casts its shadow
        /// across him rather than sitting on him. Was expressed relative to the
        /// ball's own order, which broke the moment that order moved to the front.
        /// </summary>
        private const int SHADOW_SORTING_ORDER = 0;

        private const int BALL_TEXTURE_WIDTH = 40;
        private const int BALL_TEXTURE_HEIGHT = 64;

        /// <summary>
        /// Lower than the texture height, so the sprite is longer than one world
        /// unit before the transform's own 0.36 scale. At a true 1:1 the ball came
        /// out about six screen pixels at broadcast framing — the brown was there
        /// but nothing could be read on it, and against the cream carrier glow it
        /// vanished into the shape it was sitting on.
        /// </summary>
        private const float BALL_PIXELS_PER_UNIT = 42f;

        [SerializeField] private float _zOffset = -1f;

        /// <summary>
        /// Screen-space metres the sprite rises per metre of real height. Below 1
        /// because a straight-up offset in a top-down view reads as distance
        /// upfield, and an honest 1:1 lift makes a deep ball look like it teleported
        /// ten yards downfield before coming back.
        /// </summary>
        [SerializeField] private float _heightToScreen = 0.6f;

        /// <summary>How much the shadow shrinks at the top of the arc.</summary>
        [SerializeField] private float _shadowMinScale = 0.45f;

        /// <summary>Height in metres at which the shadow reaches its smallest.</summary>
        [SerializeField] private float _shadowFadeHeight = 4f;

        /// <summary>How much bigger the ball is drawn per metre of height.</summary>
        private const float SCALE_PER_METRE = 0.35f;

        /// <summary>The largest it is drawn: a punt at its peak, not a beach ball.</summary>
        private const float MAX_HEIGHT_SCALE = 2.2f;

        /// <summary>Below this the tether is not drawn — a ball at the hip is not "in the air".</summary>
        private const float TETHER_MIN_HEIGHT = 0.15f;

        /// <summary>Width of the tether, in world units.</summary>
        private const float TETHER_WIDTH = 0.14f;

        /// <summary>
        /// Screen lift per metre for a PASS: an honest 1:1 now that the shadow and
        /// the tether join the ball to the spot it is over. A pass peaks at about
        /// 3.3 m, and at _heightToScreen's 0.6 the whole climb was two metres of
        /// screen. A kick keeps _heightToScreen: it goes twenty metres up.
        /// </summary>
        private const float PASS_HEIGHT_TO_SCREEN = 1f;

        /// <summary>Between the players (1, 2) and the trail (4): over a defender it passes above.</summary>
        private const int TETHER_SORTING_ORDER = 3;

        private Vector3 _baseScale;
        private Transform _tetherTransform;
        private SpriteRenderer _tetherRenderer;

        private Systems_BallModel _ball;
        private Transform _transform;
        private SpriteRenderer _renderer;

        private Transform _shadowTransform;
        private SpriteRenderer _shadowRenderer;
        private Vector3 _shadowBaseScale;

        [Inject]
        public void Construct(Systems_BallModel ball)
        {
            _ball = ball;
        }

        private void Awake()
        {
            _transform = transform;
            _renderer = GetComponent<SpriteRenderer>();

            ApplyBallSkin();
            BuildShadow();
            BuildTether();
            _baseScale = _transform.localScale;
        }

        /// <summary>
        /// A one-unit white sprite stretched between the shadow and the ball. Built
        /// from code and parented beside the ball for the same reasons as the shadow.
        /// </summary>
        private void BuildTether()
        {
            if (_renderer == null)
            {
                return;
            }

            Texture2D white = Texture2D.whiteTexture;
            Sprite sprite = Sprite.Create(
                white, new Rect(0f, 0f, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            sprite.name = "PoFootball_BallTether";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            GameObject tether = new GameObject("BallTether");
            _tetherTransform = tether.transform;
            _tetherTransform.SetParent(_transform.parent, false);

            _tetherRenderer = tether.AddComponent<SpriteRenderer>();
            _tetherRenderer.sprite = sprite;
            _tetherRenderer.sortingLayerID = _renderer.sortingLayerID;
            _tetherRenderer.sortingOrder = TETHER_SORTING_ORDER;
            _tetherRenderer.color = new Color(1f, 1f, 1f, 0.6f);
            _tetherRenderer.enabled = false;
        }

        /// <summary>
        /// Paints the ball a football: brown, with the two white stripes and the
        /// laces, and puts it in front of every player.
        ///
        /// SORTING WAS THE REAL BUG. The scene left this renderer on order 0 while
        /// Systems_RoleShapeApplier puts offense on 1 and defense on 2, so the ball
        /// was BEHIND all twenty-two players — a pass crossing a defender
        /// disappeared into him. It now sits above the trail (4) as well, so the
        /// ball is always the front-most thing on the field, which is what a viewer
        /// is actually tracking.
        ///
        /// The texture is generated rather than imported because this project has
        /// no sprite pipeline for gameplay art — the players are built-in shapes
        /// tinted at runtime and the audio is synthesised the same way. A 64x40
        /// texture costs nothing and keeps the ball a code artifact like everything
        /// else on the field.
        /// </summary>
        private void ApplyBallSkin()
        {
            if (_renderer == null)
            {
                return;
            }

            _renderer.sprite = BuildFootballSprite();

            // Tint back to white: the sprite carries its own colour now, and any
            // leftover tint from the scene would multiply against it.
            _renderer.color = Color.white;
            _renderer.sortingOrder = BALL_SORTING_ORDER;
        }

        /// <summary>
        /// A football: brown prolate body, a white stripe near each point, and the
        /// laces down the middle.
        ///
        /// The long axis runs along Y because the field runs along Y — a ball drawn
        /// lengthwise across a portrait field reads as a pill rather than a
        /// football. Pixels-per-unit is the texture height, so the sprite is exactly
        /// one world unit long before the transform's own 0.36 scale, which keeps it
        /// the size the round sprite used to be.
        /// </summary>
        private static Sprite BuildFootballSprite()
        {
            const float LEATHER_R = 0.44f;
            const float LEATHER_G = 0.24f;
            const float LEATHER_B = 0.11f;

            Texture2D texture = new Texture2D(
                BALL_TEXTURE_WIDTH, BALL_TEXTURE_HEIGHT, TextureFormat.RGBA32, false)
            {
                name = "PoFootball_Ball",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Color leather = new Color(LEATHER_R, LEATHER_G, LEATHER_B, 1f);
            Color rim = new Color(LEATHER_R * 0.55f, LEATHER_G * 0.55f, LEATHER_B * 0.55f, 1f);
            Color clear = new Color(0f, 0f, 0f, 0f);

            float halfWidth = BALL_TEXTURE_WIDTH * 0.5f;
            float halfHeight = BALL_TEXTURE_HEIGHT * 0.5f;
            Color[] pixels = new Color[BALL_TEXTURE_WIDTH * BALL_TEXTURE_HEIGHT];

            for (int y = 0; y < BALL_TEXTURE_HEIGHT; y++)
            {
                for (int x = 0; x < BALL_TEXTURE_WIDTH; x++)
                {
                    // Normalised to the ellipse, so 1.0 is exactly the silhouette.
                    float nx = (x + 0.5f - halfWidth) / halfWidth;
                    float ny = (y + 0.5f - halfHeight) / halfHeight;
                    float distance = Mathf.Sqrt((nx * nx) + (ny * ny));

                    if (distance > 1f)
                    {
                        pixels[(y * BALL_TEXTURE_WIDTH) + x] = clear;
                        continue;
                    }

                    Color colour = distance > 0.86f ? rim : leather;

                    // The two white stripes, set in from each point.
                    float absY = Mathf.Abs(ny);

                    if (absY > 0.58f && absY < 0.70f)
                    {
                        colour = Color.white;
                    }

                    // Laces: a spine down the middle with four crossbars over it.
                    bool onSpine = Mathf.Abs(nx) < 0.07f && absY < 0.34f;
                    bool onCrossbar = Mathf.Abs(nx) < 0.24f
                        && (Mathf.Abs(absY - 0.04f) < 0.035f
                            || Mathf.Abs(absY - 0.19f) < 0.035f);

                    if (onSpine || onCrossbar)
                    {
                        colour = Color.white;
                    }

                    // Feather only the outermost ring, so the silhouette is smooth
                    // at the size this is actually drawn without blurring the laces.
                    colour.a = Mathf.Clamp01((1f - distance) * BALL_TEXTURE_HEIGHT * 0.25f);
                    pixels[(y * BALL_TEXTURE_WIDTH) + x] = colour;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, BALL_TEXTURE_WIDTH, BALL_TEXTURE_HEIGHT),
                new Vector2(0.5f, 0.5f),
                BALL_PIXELS_PER_UNIT);

            sprite.name = "PoFootball_Ball";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>
        /// The shadow is created here rather than authored into the scene for the
        /// same reason Systems_RoleShapeApplier paints from code: a second object
        /// somebody has to remember to keep in step with the ball is a thing that
        /// silently stops matching it. It is not a child of this transform — a child
        /// would inherit the height offset and rise with the ball, which is the one
        /// thing it must not do.
        /// </summary>
        private void BuildShadow()
        {
            if (_renderer == null)
            {
                return;
            }

            GameObject shadow = new GameObject("BallShadow");
            _shadowTransform = shadow.transform;
            _shadowTransform.SetParent(_transform.parent, false);

            _shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            _shadowRenderer.sprite = _renderer.sprite;
            _shadowRenderer.sortingLayerID = _renderer.sortingLayerID;

            // Under the players, so a ball passing over a defender shows its shadow
            // crossing him rather than sitting on top of him. An absolute order, not
            // one relative to the ball's — the ball moved to the front, and "three
            // behind the ball" would have dragged the shadow up there with it.
            _shadowRenderer.sortingOrder = SHADOW_SORTING_ORDER;
            _shadowRenderer.color = new Color(0f, 0f, 0f, 0.35f);

            _shadowBaseScale = _transform.localScale * 0.8f;
            _shadowTransform.localScale = _shadowBaseScale;
            _shadowRenderer.enabled = false;
        }

        private void LateUpdate()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            if (_ball == null)
            {
                return;
            }

            Vector2 position = _ball.Position;
            float height = _ball.Height;

            _transform.position = new Vector3(
                position.x, position.y + (height * HeightToScreen()), _zOffset);

            // ALWAYS DRAWN, INCLUDING WHEN HELD. This used to hide the sprite
            // unless the ball was in flight, on the grounds that the white carrier
            // highlight already said who had it. It did not: the highlight says
            // which SHAPE is the carrier, and a viewer looking for the ball found a
            // white square with no ball anywhere near it. Where the ball physically
            // is, is the single most important thing on the field, so it is now
            // visible on every frame of every play and rides on top of whoever is
            // carrying it.
            // A kick is in the air too (revision 14), and its height is real.
            bool inFlight = _ball.IsAirborne;

            if (_renderer != null)
            {
                _renderer.enabled = true;
            }

            UpdateShadow(position, height, inFlight);
            UpdateHeightCues(position, height, inFlight);
        }

        private float HeightToScreen()
        {
            return _ball.IsKicked ? _heightToScreen : PASS_HEIGHT_TO_SCREEN;
        }

        /// <summary>The ball's size and the tether, both from the height alone.</summary>
        private void UpdateHeightCues(Vector2 position, float height, bool inFlight)
        {
            float lift = inFlight ? Mathf.Max(0f, height) : 0f;

            _transform.localScale = _baseScale
                * Mathf.Min(MAX_HEIGHT_SCALE, 1f + (lift * SCALE_PER_METRE));

            if (_tetherRenderer == null)
            {
                return;
            }

            bool showTether = lift > TETHER_MIN_HEIGHT;
            _tetherRenderer.enabled = showTether;

            if (!showTether)
            {
                return;
            }

            float length = lift * HeightToScreen();
            _tetherTransform.position = new Vector3(
                position.x, position.y + (length * 0.5f), _zOffset + 0.25f);
            _tetherTransform.localScale = new Vector3(TETHER_WIDTH, length, 1f);
        }

        /// <summary>
        /// The shadow stays on the turf at the ball's real plane position and
        /// shrinks as the ball climbs. That separation is the depth cue; the scale
        /// change is what stops a high ball and a low one looking identical.
        /// </summary>
        private void UpdateShadow(Vector2 position, float height, bool inFlight)
        {
            if (_shadowTransform == null)
            {
                return;
            }

            _shadowRenderer.enabled = inFlight;

            if (!inFlight)
            {
                return;
            }

            _shadowTransform.position = new Vector3(position.x, position.y, _zOffset + 0.5f);

            float climb = _shadowFadeHeight <= 0f
                ? 1f
                : Mathf.Clamp01(height / _shadowFadeHeight);

            _shadowTransform.localScale =
                _shadowBaseScale * Mathf.Lerp(1f, _shadowMinScale, climb);

            // Sharp and dark near the turf, faint at the top: a second reading of
            // the same height for a viewer who is watching the shadow, not the ball.
            _shadowRenderer.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.5f, 0.18f, climb));
        }

        private void OnDestroy()
        {
            // Built here, so destroyed here — it is not a child of this transform
            // and would otherwise outlive the ball it belongs to.
            if (_shadowTransform != null)
            {
                Destroy(_shadowTransform.gameObject);
            }

            if (_tetherTransform != null)
            {
                Destroy(_tetherTransform.gameObject);
            }
        }
    }
}

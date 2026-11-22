using PoFootball.Models;
using PoFootball.Systems;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// Who owns the grass: a soft wash of team colour over every patch of field
    /// one side can reach before the other, while the ball is live.
    ///
    /// WHAT IT SHOWS THAT NOTHING ELSE DOES. Every other mark on the field is about
    /// a player or the ball. Football is about the SPACE between them — the seam a
    /// receiver is running into, the cutback lane a linebacker has overrun, the
    /// pocket closing — and a viewer forty metres up sees twenty-two shapes and
    /// has to infer all of it. This draws it. A lane opening in front of the
    /// carrier is offense colour spreading ahead of him; a defense rallying to the
    /// ball is its colour closing over it.
    ///
    /// SUBTLE ON PURPOSE. The turf, the yard lines, the drive trail and the wear
    /// all have to read through it, so the strongest cell is about a third opaque
    /// and anything nobody can reach soon is not drawn at all (see
    /// Systems_SpaceControlJob for why). It fades in at the snap and out at the
    /// whistle, and the dead-ball hold shows the last map fading rather than a cut.
    ///
    /// TEN TIMES A SECOND, NOT EVERY FRAME. Players move a few tens of centimetres
    /// in a tenth of a second, which against one-metre cells is under a texel; a
    /// per-frame map would cost six times the work to show the same picture. The
    /// job is scheduled in Update and completed in LateUpdate, so the Burst workers
    /// run while the rest of the frame's scripts do.
    ///
    /// ONE QUAD, ONE DRAW CALL, ONE TEXTURE UPLOAD PER REFRESH. The texture is
    /// small — one texel a metre, about 50 x 110 — and bilinear filtering turns
    /// that into smooth gradients at the broadcast camera's height. The
    /// NativeArrays are allocated once at Start and disposed with the view, so a
    /// steady-state refresh allocates nothing.
    ///
    /// COLOURED BY TEAM, READ FROM THE BODIES. Offense is whichever team has the
    /// ball, and Systems_RoleShapeApplier repaints the bodies when that changes,
    /// so the colours are read off two of them at the snap — the reasoning
    /// Systems_IntentOverlayView.CollectBodies gives for reading the renderer
    /// rather than keeping a side-to-colour table of its own.
    ///
    /// Gated on Systems_PresentationBudget like every other decoration, and on
    /// Systems_BroadcastSettings.SpaceControl every frame, so the viewer can turn
    /// it off mid-play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_SpaceControlView : MonoBehaviour, Systems_IInjectableView
    {
        /// <summary>
        /// On the ground with the turf wear, under every player (offense is on 1).
        /// </summary>
        private const int SORTING_ORDER = 0;

        /// <summary>
        /// In front of Systems_TurfScuffView's wear layer at -0.01, which shares
        /// the sorting order: same-order sprites are drawn back to front, and this
        /// wash belongs over the worn earth, not under it.
        /// </summary>
        private const float DEPTH = -0.02f;

        /// <summary>One texel a metre: about a stride, and finer than the model's own accuracy.</summary>
        private const float CELL_METRES = 1f;

        private const float REFRESH_SECONDS = 0.1f;

        /// <summary>
        /// How long a moving body keeps going the way it was going. A quarter of a
        /// second is about a plant-and-cut for a skill player; longer and a corner
        /// backpedalling is credited with ground behind him he cannot turn to.
        /// </summary>
        private const float REACTION_SECONDS = 0.3f;

        /// <summary>A one-second lead is full colour; half a second is a fair fight.</summary>
        private const float SATURATE_SECONDS = 1f;

        private const float NEAR_SECONDS = 1.5f;
        private const float FAR_SECONDS = 4.5f;

        /// <summary>
        /// Strongest cell's opacity. 0.34 was the first guess, and a 1080x1920
        /// capture (SCREENSHOTS/broadcast_after_3.png, 2026-10-22) showed it washing
        /// the whole field teal and amber — the two jerseys blended into green turf
        /// — so the field stopped reading as grass. Halved, the turf leads and the
        /// tint says whose space it is.
        /// </summary>
        private const float MAX_ALPHA = 0.17f;

        /// <summary>How far a team colour is lifted toward white; see Systems_DriveTrailView.</summary>
        private const float COLOR_LIFT = 0.25f;

        private const float FADE_IN_RATE = 6f;
        private const float FADE_OUT_RATE = 2.5f;

        private Systems_PlayerRegistry _registry;
        private Systems_PlayModel _play;
        private Systems_Roster _roster;
        private Systems_PresentationBudget _budget;

        private SpriteRenderer[] _bodies;

        private Texture2D _texture;
        private Sprite _sprite;
        private SpriteRenderer _renderer;

        private int _width;
        private int _height;

        private NativeArray<float4> _players;
        private NativeArray<float> _topSpeeds;
        private NativeArray<int> _sides;
        private NativeArray<Color32> _pixels;

        private JobHandle _job;
        private bool _jobPending;
        private bool _allocated;

        private float _refreshRemaining;
        private float _shownAlpha;
        private int _coloredEpisode = -1;
        private float3 _offenseColor = new float3(1f, 1f, 1f);
        private float3 _defenseColor = new float3(1f, 1f, 1f);

        [Inject]
        public void Construct(
            Systems_PlayerRegistry registry,
            Systems_PlayModel play,
            Systems_Roster roster,
            Systems_PresentationBudget budget)
        {
            _registry = registry;
            _play = play;
            _roster = roster;
            _budget = budget;
        }

        /// <summary>
        /// Start rather than Awake: injection has not run by Awake, and the body
        /// colours are assigned by Systems_RoleShapeApplier at execution order -100.
        /// </summary>
        private void Start()
        {
            if (_budget == null || !_budget.EffectsEnabled
                || _registry == null || _play == null)
            {
                enabled = false;
                return;
            }

            CollectBodies();
            BuildLayer();
        }

        private void OnDestroy()
        {
            if (_jobPending)
            {
                _job.Complete();
                _jobPending = false;
            }

            if (_allocated)
            {
                _players.Dispose();
                _topSpeeds.Dispose();
                _sides.Dispose();
                _pixels.Dispose();
                _allocated = false;
            }

            if (_sprite != null)
            {
                Destroy(_sprite);
            }

            if (_texture != null)
            {
                Destroy(_texture);
            }
        }

        private void BuildLayer()
        {
            _width = Mathf.CeilToInt(Systems_FieldModel.FIELD_WIDTH / CELL_METRES);
            _height = Mathf.CeilToInt(Systems_FieldModel.TOTAL_LENGTH / CELL_METRES);

            int capacity = Systems_PlayerRegistry.CAPACITY;

            _players = new NativeArray<float4>(capacity, Allocator.Persistent);
            _topSpeeds = new NativeArray<float>(capacity, Allocator.Persistent);
            _sides = new NativeArray<int>(capacity, Allocator.Persistent);
            _pixels = new NativeArray<Color32>(_width * _height, Allocator.Persistent);
            _allocated = true;

            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SpaceControl",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            _texture.SetPixelData(_pixels, 0);
            _texture.Apply(false);

            _sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, _width, _height),
                new Vector2(0.5f, 0.5f),
                1f / CELL_METRES);

            GameObject host = new GameObject("SpaceControl");
            host.transform.SetParent(transform, false);
            host.transform.position = new Vector3(0f, 0f, DEPTH);

            // Whole texels against a field that is not a whole number of metres:
            // squeezed onto it exactly, as the wear layer is.
            host.transform.localScale = new Vector3(
                Systems_FieldModel.FIELD_WIDTH / (_width * CELL_METRES),
                Systems_FieldModel.TOTAL_LENGTH / (_height * CELL_METRES),
                1f);

            _renderer = host.AddComponent<SpriteRenderer>();
            _renderer.sprite = _sprite;
            _renderer.sortingOrder = SORTING_ORDER;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.color = new Color(1f, 1f, 1f, 0f);
            _renderer.enabled = false;
        }

        private void Update()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            bool live = _play.Phase == Systems_PlayPhase.Live;
            bool wanted = live && Systems_BroadcastSettings.SpaceControl;

            float rate = wanted ? FADE_IN_RATE : FADE_OUT_RATE;
            _shownAlpha = Mathf.MoveTowards(_shownAlpha, wanted ? 1f : 0f, rate * Time.deltaTime);

            _renderer.enabled = _shownAlpha > 0.01f;
            _renderer.color = new Color(1f, 1f, 1f, _shownAlpha);

            if (!wanted)
            {
                // The last map stays in the texture and fades with the renderer.
                // The next snap refreshes it before it is visible again.
                _refreshRemaining = 0f;
                return;
            }

            if (_coloredEpisode != _play.EpisodeIndex)
            {
                _coloredEpisode = _play.EpisodeIndex;
                CaptureTeamColors();
            }

            _refreshRemaining -= Time.deltaTime;

            if (_refreshRemaining > 0f || _jobPending)
            {
                return;
            }

            _refreshRemaining = REFRESH_SECONDS;
            Schedule();
        }

        private void LateUpdate()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            if (!_jobPending)
            {
                return;
            }

            _job.Complete();
            _jobPending = false;

            _texture.SetPixelData(_pixels, 0);
            _texture.Apply(false);
        }

        private void Schedule()
        {
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                if (handle == null)
                {
                    _sides[slotIndex] = Systems_SpaceControlJob.ABSENT;
                    continue;
                }

                Vector2 position = handle.Position;
                Vector2 velocity = handle.Velocity;

                _players[slotIndex] = new float4(position.x, position.y, velocity.x, velocity.y);
                _topSpeeds[slotIndex] = TopSpeedOf(slotIndex, handle);
                _sides[slotIndex] = handle.Side == Systems_TeamSide.Offense ? 0 : 1;
            }

            float2 cellSize = new float2(
                Systems_FieldModel.FIELD_WIDTH / _width,
                Systems_FieldModel.TOTAL_LENGTH / _height);

            Systems_SpaceControlJob job = new Systems_SpaceControlJob
            {
                Players = _players,
                TopSpeeds = _topSpeeds,
                Sides = _sides,
                Width = _width,
                Origin = new float2(
                    -Systems_FieldModel.HALF_WIDTH + (cellSize.x * 0.5f),
                    (-Systems_FieldModel.TOTAL_LENGTH * 0.5f) + (cellSize.y * 0.5f)),
                CellSize = cellSize,
                ReactionSeconds = REACTION_SECONDS,
                SaturateSeconds = SATURATE_SECONDS,
                NearSeconds = NEAR_SECONDS,
                FarSeconds = FAR_SECONDS,
                MaxAlpha = MAX_ALPHA,
                OffenseColor = _offenseColor,
                DefenseColor = _defenseColor,
                Pixels = _pixels
            };

            _job = job.Schedule(_pixels.Length, _width);
            _jobPending = true;
        }

        private float TopSpeedOf(int slotIndex, Systems_IPlayerHandle handle)
        {
            Systems_PlayerTraits traits = _roster == null ? default : _roster.TraitsOf(slotIndex);
            return Systems_RoleTable.TopSpeedOf(handle.Role, traits);
        }

        /// <summary>
        /// One body from each side that is not carrying the ball — the carrier is
        /// painted white on top of his team colour (Agent_FootballPlayer).
        /// </summary>
        private void CaptureTeamColors()
        {
            bool offenseFound = false;
            bool defenseFound = false;
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                if (handle == null || handle.IsCarrier
                    || handle.Id < 0 || handle.Id >= _bodies.Length)
                {
                    continue;
                }

                SpriteRenderer body = _bodies[handle.Id];

                if (body == null)
                {
                    continue;
                }

                if (handle.Side == Systems_TeamSide.Offense && !offenseFound)
                {
                    _offenseColor = Lift(body.color);
                    offenseFound = true;
                }
                else if (handle.Side == Systems_TeamSide.Defense && !defenseFound)
                {
                    _defenseColor = Lift(body.color);
                    defenseFound = true;
                }

                if (offenseFound && defenseFound)
                {
                    return;
                }
            }
        }

        private static float3 Lift(Color color)
        {
            Color lifted = Color.Lerp(color, Color.white, COLOR_LIFT);
            return new float3(lifted.r, lifted.g, lifted.b);
        }

        /// <summary>
        /// One scan for the twenty-two sprite renderers, indexed by the handle's
        /// own Id — Systems_IntentOverlayView.CollectBodies, for the same reason.
        /// </summary>
        private void CollectBodies()
        {
            int capacity = Systems_PlayerRegistry.CAPACITY;
            _bodies = new SpriteRenderer[capacity];

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include);

            for (int index = 0; index < behaviours.Length; index++)
            {
                if (!(behaviours[index] is Systems_IPlayerHandle handle))
                {
                    continue;
                }

                if (handle.Id < 0 || handle.Id >= capacity)
                {
                    continue;
                }

                if (behaviours[index].TryGetComponent(out SpriteRenderer body))
                {
                    _bodies[handle.Id] = body;
                }
            }
        }
    }
}

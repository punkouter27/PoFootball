using System.Collections.Generic;
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
    /// The two numbers a broadcast's tracking graphics are built on: how open
    /// each receiver is while the pass is still in the quarterback's hand or in
    /// the air, and where the nearest pursuers will meet the carrier once
    /// somebody is running with it.
    ///
    /// SEPARATION, ON PASS PLAYS, BEFORE THE CATCH. Each eligible receiver gets a
    /// ring graded by the distance to the nearest defender — green open, amber
    /// contested, red covered — and a gauge arc round it that fills with that
    /// distance up to six yards. The thresholds are the simulation's own, not
    /// television's: "covered" is inside Systems_SimConstants.PASS_BREAKUP_RADIUS,
    /// the radius inside which a defender can actually get a hand on the ball, so
    /// a red ring means the referee's dice are already in play. The rings go the
    /// moment the ball is caught, because after that the question is no longer
    /// who is open but who can catch the man with it.
    ///
    /// PURSUIT, WHILE A CARRIER RUNS. A line from each of the three defenders who
    /// can reach the carrier soonest to the point they would meet him: the
    /// constant-velocity intercept of a man running at the carrier's current
    /// velocity by a man running at his own trait-scaled top speed. It is the
    /// angle a coach is asking a defender to take, and it makes the difference
    /// between pursuit and chasing visible — a defender with no line is one who
    /// cannot get there at all. Ranked by TIME to intercept, not distance, which
    /// is the point: the nearest defender is often the one already beaten.
    ///
    /// THE SAME CONSTRUCTION AS Systems_IntentOverlayView. One mesh, per-vertex
    /// colour, the shared additive particle material, Lists cleared rather than
    /// reallocated — one draw call and nothing allocated per frame. Everything is
    /// eased per player, because both the separation and the intercept point are
    /// derived from velocities that jitter tick to tick and an unsmoothed gauge
    /// flickers.
    ///
    /// NOTHING HERE WRITES TO THE SIMULATION. Gated on Systems_PresentationBudget,
    /// and on Systems_BroadcastSettings.SpaceControl every frame — it is the same
    /// family of tracking graphics as the space-control wash, under one switch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_SeparationView : MonoBehaviour, Systems_IInjectableView
    {
        /// <summary>With the intent overlay: over the bodies, under the ball trail.</summary>
        private const int SORTING_ORDER = 3;

        /// <summary>
        /// Separation at which a receiver is open, three yards. Between this and
        /// the breakup radius is contested.
        /// </summary>
        private const float OPEN_METRES = 3f * Systems_FieldModel.YARD;

        /// <summary>Separation that fills the gauge, six yards.</summary>
        private const float GAUGE_FULL_METRES = 6f * Systems_FieldModel.YARD;

        private const float RING_RADIUS = 0.95f;
        private const float RING_WIDTH = 0.08f;
        private const float GAUGE_WIDTH = 0.2f;
        private const int RING_SEGMENTS = 28;

        /// <summary>Lines start at the body's edge, as the intent arrows do.</summary>
        private const float BODY_RADIUS = 0.62f;

        private const int PURSUERS = 3;

        /// <summary>
        /// Intercepts further off than this are not drawn: the carrier is either
        /// gone or will have been brought down by someone else long before.
        /// </summary>
        private const float MAX_INTERCEPT_SECONDS = 3.5f;

        private const float PURSUIT_WIDTH_AT_DEFENDER = 0.16f;
        private const float PURSUIT_WIDTH_AT_POINT = 0.06f;
        private const float MARK_HALF_LENGTH = 0.32f;
        private const float MARK_WIDTH = 0.08f;

        /// <summary>Strength of the first, second and third pursuer's line.</summary>
        private static readonly float[] RankAlpha = { 0.85f, 0.6f, 0.42f };

        private const float FADE_RATE = 7f;

        /// <summary>How fast a drawn gauge or intercept point chases its target, per second.</summary>
        private const float CHASE_RATE = 10f;

        private const float COLOR_LIFT = 0.3f;

        /// <summary>See Systems_IntentOverlayView.FieldBounds.</summary>
        private static readonly Bounds FieldBounds =
            new Bounds(Vector3.zero, new Vector3(240f, 240f, 1f));

        private Systems_PlayerRegistry _registry;
        private Systems_PlayModel _play;
        private Systems_BallModel _ball;
        private Systems_Roster _roster;
        private Systems_PresentationBudget _budget;

        private Mesh _mesh;
        private MeshRenderer _meshRenderer;

        private readonly List<Vector3> _vertices = new List<Vector3>(2048);
        private readonly List<Color32> _colors = new List<Color32>(2048);
        private readonly List<int> _indices = new List<int>(3072);

        private SpriteRenderer[] _bodies;

        private float[] _ringAlpha;
        private float[] _shownSeparation;

        private float[] _pursuitAlpha;
        private float[] _pursuitRankAlpha;
        private Vector2[] _shownIntercept;
        private Vector2[] _targetIntercept;
        private bool[] _pursuitChosen;

        private readonly int[] _bestSlots = new int[PURSUERS];
        private readonly float[] _bestTimes = new float[PURSUERS];

        private NativeArray<float4> _jobPlayers;
        private NativeArray<int> _jobSides;
        private NativeArray<float> _jobTopSpeeds;
        private NativeArray<float> _jobDistances;
        private NativeArray<float> _jobIntercepts;
        private bool _jobAllocated;

        internal bool IsBurstJobAllocated => _jobAllocated;

        [Inject]
        public void Construct(
            Systems_PlayerRegistry registry,
            Systems_PlayModel play,
            Systems_BallModel ball,
            Systems_Roster roster,
            Systems_PresentationBudget budget)
        {
            _registry = registry;
            _play = play;
            _ball = ball;
            _roster = roster;
            _budget = budget;
        }

        /// <summary>Start rather than Awake, for the reason Systems_IntentOverlayView gives.</summary>
        private void Start()
        {
            if (_budget == null || !_budget.EffectsEnabled
                || _registry == null || _play == null || _ball == null)
            {
                enabled = false;
                return;
            }

            if (!Systems_ParticleMaterial.TryLoad(
                    nameof(Systems_SeparationView), "No separation or pursuit is drawn.",
                    out Material source))
            {
                enabled = false;
                return;
            }

            int capacity = Systems_PlayerRegistry.CAPACITY;

            _ringAlpha = new float[capacity];
            _shownSeparation = new float[capacity];
            _pursuitAlpha = new float[capacity];
            _pursuitRankAlpha = new float[capacity];
            _shownIntercept = new Vector2[capacity];
            _targetIntercept = new Vector2[capacity];
            _pursuitChosen = new bool[capacity];

            _jobPlayers = new NativeArray<float4>(capacity, Allocator.Persistent);
            _jobSides = new NativeArray<int>(capacity, Allocator.Persistent);
            _jobTopSpeeds = new NativeArray<float>(capacity, Allocator.Persistent);
            _jobDistances = new NativeArray<float>(capacity, Allocator.Persistent);
            _jobIntercepts = new NativeArray<float>(capacity, Allocator.Persistent);
            _jobAllocated = true;

            CollectBodies();
            BuildMesh(source);
        }

        private void OnDestroy()
        {
            if (_jobAllocated)
            {
                _jobPlayers.Dispose();
                _jobSides.Dispose();
                _jobTopSpeeds.Dispose();
                _jobDistances.Dispose();
                _jobIntercepts.Dispose();
                _jobAllocated = false;
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private void BuildMesh(Material source)
        {
            _mesh = new Mesh { name = "PoFootball Separation" };
            _mesh.MarkDynamic();

            GameObject host = new GameObject("Separation");
            host.transform.SetParent(transform, false);

            MeshFilter filter = host.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            _meshRenderer = host.AddComponent<MeshRenderer>();

            // sharedMaterial, never material (.claude/rules/performance.md).
            _meshRenderer.sharedMaterial = source;
            _meshRenderer.sortingOrder = SORTING_ORDER;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _meshRenderer.enabled = false;
        }

        /// <summary>LateUpdate so a ring drawn round a receiver lands on him this frame.</summary>
        private void LateUpdate()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();

            bool enabledBySetting = Systems_BroadcastSettings.SpaceControl;
            bool live = _play.Phase == Systems_PlayPhase.Live;

            Systems_IPlayerHandle carrier = _registry.LiveCarrier(_play, _ball);

            bool passPending = live && IsPassPending(carrier);
            bool carrierRunning = live && carrier != null && !passPending;

            if (enabledBySetting && (passPending || carrierRunning))
            {
                RunSeparationJob(passPending, carrierRunning ? carrier : null);
            }

            float fade = FADE_RATE * Time.deltaTime;
            float chase = 1f - Mathf.Exp(-CHASE_RATE * Time.deltaTime);

            UpdateSeparation(enabledBySetting && passPending, fade, chase);
            UpdatePursuit(enabledBySetting && carrierRunning ? carrier : null, fade, chase);

            Upload();
        }

        private void RunSeparationJob(bool computeSeparation, Systems_IPlayerHandle carrier)
        {
            if (!_jobAllocated)
            {
                return;
            }

            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                if (handle == null)
                {
                    _jobSides[slotIndex] = Systems_SeparationJob.ABSENT;
                    continue;
                }

                Vector2 position = handle.Position;
                Vector2 velocity = handle.Velocity;

                _jobPlayers[slotIndex] = new float4(position.x, position.y, velocity.x, velocity.y);
                _jobSides[slotIndex] = handle.Side == Systems_TeamSide.Offense ? 0 : 1;
                _jobTopSpeeds[slotIndex] = Systems_RoleTable.TopSpeedOf(
                    handle.Role, _roster == null ? default : _roster.TraitsOf(slotIndex));
            }

            Systems_SeparationJob job = new Systems_SeparationJob
            {
                Players = _jobPlayers,
                Sides = _jobSides,
                TopSpeeds = _jobTopSpeeds,
                CarrierPosition = carrier != null ? new float2(carrier.Position.x, carrier.Position.y) : float2.zero,
                CarrierVelocity = carrier != null ? new float2(carrier.Velocity.x, carrier.Velocity.y) : float2.zero,
                CarrierSide = carrier != null ? (carrier.Side == Systems_TeamSide.Offense ? 0 : 1) : Systems_SeparationJob.ABSENT,
                HasCarrier = carrier != null ? 1 : 0,
                ComputeSeparation = computeSeparation ? 1 : 0,
                DefaultSeparation = GAUGE_FULL_METRES,
                NearestOpponentDistances = _jobDistances,
                InterceptTimes = _jobIntercepts
            };

            job.Schedule(capacity, 4).Complete();
        }

        /// <summary>
        /// The ball is still the quarterback's to throw, on a play called as a
        /// pass, or is a forward pass in the air.
        ///
        /// A quarterback past the line has stopped being a passer and become a
        /// runner — he can no longer legally throw forward — so a scramble switches
        /// the graphic from separation to pursuit at the line, which is where a
        /// broadcast would switch it too.
        /// </summary>
        private bool IsPassPending(Systems_IPlayerHandle carrier)
        {
            if (_ball.IsInFlight)
            {
                return _play.ForwardPassThrown;
            }

            return carrier != null
                && carrier.Role == Systems_PlayerRole.Quarterback
                && carrier.Side == Systems_TeamSide.Offense
                && _play.Call.IsPass()
                && carrier.Position.y <= _play.LineOfScrimmageY;
        }

        private void UpdateSeparation(bool show, float fade, float chase)
        {
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                bool eligible = show
                    && handle != null
                    && handle.Side == Systems_TeamSide.Offense
                    && handle.Role.IsEligibleReceiver();

                float separation = eligible && _jobAllocated ? _jobDistances[slotIndex] : 0f;

                if (eligible && _ringAlpha[slotIndex] <= 0f)
                {
                    // Appearing: start the gauge at the truth, not at the last play's value.
                    _shownSeparation[slotIndex] = separation;
                }
                else if (eligible)
                {
                    _shownSeparation[slotIndex] = Mathf.Lerp(
                        _shownSeparation[slotIndex], separation, chase);
                }

                _ringAlpha[slotIndex] = Mathf.MoveTowards(
                    _ringAlpha[slotIndex], eligible ? 1f : 0f, fade);

                if (_ringAlpha[slotIndex] <= 0.01f || handle == null)
                {
                    continue;
                }

                AppendRing(handle.Position, _shownSeparation[slotIndex], _ringAlpha[slotIndex]);
            }
        }

        private void AppendRing(Vector2 centre, float separation, float alpha)
        {
            Color grade = separation >= OPEN_METRES
                ? Systems_UiTheme.Positive
                : separation >= Systems_SimConstants.PASS_BREAKUP_RADIUS
                    ? Systems_UiTheme.Accent
                    : Systems_UiTheme.Negative;

            Color ringColor = grade;
            ringColor.a = 0.45f * alpha;

            Color gaugeColor = grade;
            gaugeColor.a = 0.9f * alpha;

            AppendArc(centre, RING_RADIUS, 0f, Mathf.PI * 2f, RING_WIDTH, ringColor, RING_SEGMENTS);

            // From twelve o'clock, clockwise, as a dial reads. Built anticlockwise
            // from the far end so every quad winds the same way as the ring's.
            float fill = Mathf.Clamp01(separation / GAUGE_FULL_METRES);

            if (fill > 0.02f)
            {
                int segments = Mathf.Max(2, Mathf.CeilToInt(RING_SEGMENTS * fill));
                float sweep = Mathf.PI * 2f * fill;

                AppendArc(
                    centre,
                    RING_RADIUS,
                    (Mathf.PI * 0.5f) - sweep,
                    Mathf.PI * 0.5f,
                    GAUGE_WIDTH,
                    gaugeColor,
                    segments);
            }
        }

        private void UpdatePursuit(Systems_IPlayerHandle carrier, float fade, float chase)
        {
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                _pursuitChosen[slotIndex] = false;
            }

            if (carrier != null)
            {
                ChoosePursuers(carrier);
            }

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                bool chosen = _pursuitChosen[slotIndex];

                if (chosen)
                {
                    if (_pursuitAlpha[slotIndex] <= 0f)
                    {
                        _shownIntercept[slotIndex] = _targetIntercept[slotIndex];
                    }
                    else
                    {
                        _shownIntercept[slotIndex] = Vector2.Lerp(
                            _shownIntercept[slotIndex], _targetIntercept[slotIndex], chase);
                    }
                }

                _pursuitAlpha[slotIndex] = Mathf.MoveTowards(
                    _pursuitAlpha[slotIndex], chosen ? 1f : 0f, fade);

                if (_pursuitAlpha[slotIndex] <= 0.01f)
                {
                    continue;
                }

                Systems_IPlayerHandle defender = _registry.Get(slotIndex);

                if (defender == null)
                {
                    continue;
                }

                AppendPursuit(
                    defender,
                    _shownIntercept[slotIndex],
                    _pursuitAlpha[slotIndex] * _pursuitRankAlpha[slotIndex]);
            }
        }

        /// <summary>The three opponents with the earliest intercept, by insertion into a fixed array.</summary>
        private void ChoosePursuers(Systems_IPlayerHandle carrier)
        {
            int found = 0;
            int capacity = Systems_PlayerRegistry.CAPACITY;

            if (!_jobAllocated)
            {
                return;
            }

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle defender = _registry.Get(slotIndex);

                if (defender == null || defender.Side == carrier.Side)
                {
                    continue;
                }

                float time = _jobIntercepts[slotIndex];

                if (float.IsInfinity(time) || time > MAX_INTERCEPT_SECONDS)
                {
                    continue;
                }

                int insertAt = found;

                while (insertAt > 0 && _bestTimes[insertAt - 1] > time)
                {
                    if (insertAt < PURSUERS)
                    {
                        _bestTimes[insertAt] = _bestTimes[insertAt - 1];
                        _bestSlots[insertAt] = _bestSlots[insertAt - 1];
                    }

                    insertAt--;
                }

                if (insertAt < PURSUERS)
                {
                    _bestTimes[insertAt] = time;
                    _bestSlots[insertAt] = slotIndex;
                    found = Mathf.Min(found + 1, PURSUERS);
                }
            }

            for (int rank = 0; rank < found; rank++)
            {
                int slotIndex = _bestSlots[rank];

                _pursuitChosen[slotIndex] = true;
                _pursuitRankAlpha[slotIndex] = RankAlpha[rank];
                _targetIntercept[slotIndex] = Systems_FieldModel.ClampToField(
                    carrier.Position + (carrier.Velocity * _bestTimes[rank]));
            }
        }

        private void AppendPursuit(Systems_IPlayerHandle defender, Vector2 point, float alpha)
        {
            Vector2 from = defender.Position;
            Vector2 span = point - from;
            float distance = span.magnitude;

            Color color = Color.white;
            SpriteRenderer body = defender.Id >= 0 && defender.Id < _bodies.Length
                ? _bodies[defender.Id]
                : null;

            if (body != null)
            {
                color = Color.Lerp(body.color, Color.white, COLOR_LIFT);
            }

            color.a = alpha;
            Color32 packed = color;

            Color tip = color;
            tip.a = alpha * 0.5f;
            Color32 packedTip = tip;

            if (distance > BODY_RADIUS + 0.1f)
            {
                Vector2 direction = span / distance;

                AppendQuad(
                    from + (direction * BODY_RADIUS),
                    point,
                    PURSUIT_WIDTH_AT_DEFENDER,
                    PURSUIT_WIDTH_AT_POINT,
                    packed,
                    packedTip);
            }

            // An X where they meet.
            Vector2 diagonal = new Vector2(MARK_HALF_LENGTH, MARK_HALF_LENGTH);
            Vector2 antiDiagonal = new Vector2(MARK_HALF_LENGTH, -MARK_HALF_LENGTH);

            AppendQuad(point - diagonal, point + diagonal, MARK_WIDTH, MARK_WIDTH, packed, packed);
            AppendQuad(point - antiDiagonal, point + antiDiagonal, MARK_WIDTH, MARK_WIDTH, packed, packed);
        }

        private void AppendArc(
            Vector2 centre,
            float radius,
            float fromAngle,
            float toAngle,
            float width,
            Color color,
            int segments)
        {
            Color32 packed = color;
            float step = (toAngle - fromAngle) / segments;
            Vector2 from = centre + (new Vector2(Mathf.Cos(fromAngle), Mathf.Sin(fromAngle)) * radius);

            for (int segment = 1; segment <= segments; segment++)
            {
                float angle = fromAngle + (step * segment);
                Vector2 to = centre + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);

                AppendQuad(from, to, width, width, packed, packed);
                from = to;
            }
        }

        private void AppendQuad(
            Vector2 from,
            Vector2 to,
            float fromWidth,
            float toWidth,
            Color32 fromColor,
            Color32 toColor)
        {
            Vector2 span = to - from;

            if (span.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector2 normal = new Vector2(-span.y, span.x).normalized;
            Vector2 fromOffset = normal * (fromWidth * 0.5f);
            Vector2 toOffset = normal * (toWidth * 0.5f);

            int baseIndex = _vertices.Count;

            _vertices.Add(from - fromOffset);
            _vertices.Add(from + fromOffset);
            _vertices.Add(to + toOffset);
            _vertices.Add(to - toOffset);

            _colors.Add(fromColor);
            _colors.Add(fromColor);
            _colors.Add(toColor);
            _colors.Add(toColor);

            _indices.Add(baseIndex);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex + 3);
        }

        /// <summary>See Systems_IntentOverlayView.Upload — Clear(false) first.</summary>
        private void Upload()
        {
            _mesh.Clear(false);

            if (_vertices.Count == 0)
            {
                _meshRenderer.enabled = false;
                return;
            }

            _meshRenderer.enabled = true;

            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.bounds = FieldBounds;
        }

        /// <summary>See Systems_IntentOverlayView.CollectBodies.</summary>
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

using System.Collections.Generic;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// The chase and the pocket, drawn on the field while the ball is live in
    /// someone's hands.
    ///
    ///     PURSUIT CONES   a narrow wedge from each of the (up to) three opponents
    ///                     who can reach the carrier soonest, to the spot where
    ///                     they meet him, with a ring on the leader's spot. Fainter
    ///                     the longer the chase. This is the angle a defender is
    ///                     PAID to take (Reward_Role.InterceptShaping reads the
    ///                     same Systems_InterceptMath), so a defender running at
    ///                     where the carrier was, instead of where he will be, is
    ///                     visibly not in his own cone.
    ///     POCKET CLOCK    while the quarterback holds the ball behind the line
    ///                     before any forward pass, a ring round him that empties
    ///                     as the nearest rusher's time to reach him falls from
    ///                     POCKET_FULL_SECONDS to zero, green to red. Replaces the
    ///                     cones in that window: a ring of five wedges converging
    ///                     on the passer says less than one clock.
    ///
    /// SPECTATOR-ONLY. Constructed by Systems_StatusHudBootstrap in a game scene
    /// (never in training or batch mode), read-only on the models, and gated on
    /// Systems_PresentationBudget at Start and Systems_BroadcastSettings.Pursuit
    /// every frame. One mesh, vertex colours, the shared particle material, lists
    /// cleared rather than reallocated — the Systems_FatigueRingView recipe — so a
    /// steady-state frame allocates nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_PursuitView : MonoBehaviour
    {
        /// <summary>With the fatigue rings (2): over the turf, under the intent overlay (3).</summary>
        private const int SORTING_ORDER = 2;

        private const int MAX_PURSUERS = 3;

        /// <summary>
        /// Chases longer than this are not drawn. Half the reward's eight-second
        /// horizon: past four seconds the carrier's next cut moves the spot anyway,
        /// and a field of long faint wedges is noise.
        /// </summary>
        private const float PURSUIT_HORIZON_SECONDS = 4f;

        private const float CONE_HALF_WIDTH = 0.55f;
        private const float CONE_ALPHA = 0.32f;
        private const float MEET_RING_RADIUS = 0.45f;
        private const float MEET_RING_WIDTH = 0.08f;

        /// <summary>A rusher this far off or further leaves a full clock.</summary>
        private const float POCKET_FULL_SECONDS = 2.5f;

        private const float POCKET_RING_RADIUS = 1.05f;
        private const float POCKET_RING_WIDTH = 0.14f;
        private const float POCKET_ALPHA = 0.8f;

        /// <summary>A quarterback this far past the line has left the pocket.</summary>
        private const float POCKET_LINE_SLACK = 0.5f;

        private const int SEGMENTS_FULL_CIRCLE = 32;

        /// <summary>See Systems_IntentOverlayView.FieldBounds — same reasoning.</summary>
        private static readonly Bounds FieldBounds =
            new Bounds(Vector3.zero, new Vector3(240f, 240f, 1f));

        private Systems_PlayerRegistry _registry;
        private Systems_PlayModel _play;
        private Systems_BallModel _ball;
        private Systems_Roster _roster;
        private Systems_PresentationBudget _budget;

        private Mesh _mesh;
        private MeshRenderer _meshRenderer;

        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<Color32> _colors = new List<Color32>(1024);
        private readonly List<int> _indices = new List<int>(1536);

        private readonly int[] _pursuerSlots = new int[MAX_PURSUERS];
        private readonly float[] _pursuerSeconds = new float[MAX_PURSUERS];

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

        private void Start()
        {
            if (_budget == null || !_budget.EffectsEnabled
                || _registry == null || _play == null || _ball == null)
            {
                enabled = false;
                return;
            }

            if (!Systems_ParticleMaterial.TryLoad(
                    nameof(Systems_PursuitView), "No pursuit cones are drawn.", out Material source))
            {
                enabled = false;
                return;
            }

            _mesh = new Mesh { name = "PoFootball Pursuit" };
            _mesh.MarkDynamic();

            GameObject host = new GameObject("Pursuit");
            host.transform.SetParent(transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _meshRenderer = host.AddComponent<MeshRenderer>();

            // sharedMaterial, never material (.claude/rules/performance.md).
            _meshRenderer.sharedMaterial = source;
            _meshRenderer.sortingOrder = SORTING_ORDER;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _meshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private void LateUpdate()
        {
            using var presentationCost = Systems_PresentationProfiler.Measure();
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();

            if (Systems_BroadcastSettings.Pursuit
                && _play.Phase == Systems_PlayPhase.Live
                && _ball.IsHeld)
            {
                Systems_IPlayerHandle carrier = FindCarrier();

                if (carrier != null)
                {
                    if (IsInThePocket(carrier))
                    {
                        AppendPocketClock(carrier);
                    }
                    else
                    {
                        AppendPursuit(carrier);
                    }
                }
            }

            Upload();
        }

        private Systems_IPlayerHandle FindCarrier()
        {
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                if (handle != null && handle.IsCarrier)
                {
                    return handle;
                }
            }

            return null;
        }

        /// <summary>Offense attacks +Y (Systems_TeamSide).</summary>
        private bool IsInThePocket(Systems_IPlayerHandle carrier)
        {
            return carrier.Role == Systems_PlayerRole.Quarterback
                && carrier.Side == Systems_TeamSide.Offense
                && _ball.PossessingSide == Systems_TeamSide.Offense
                && !_play.ForwardPassThrown
                && carrier.Position.y <= _play.LineOfScrimmageY + POCKET_LINE_SLACK;
        }

        private float SecondsToReach(int slotIndex, Systems_IPlayerHandle pursuer, Systems_IPlayerHandle carrier)
        {
            Systems_PlayerTraits traits = _roster == null ? default : _roster.TraitsOf(slotIndex);
            float topSpeed = Systems_RoleTable.TopSpeedOf(pursuer.Role, traits);

            return Systems_InterceptMath.InterceptSeconds(
                pursuer.Position, topSpeed, carrier.Position, carrier.Velocity);
        }

        private void AppendPursuit(Systems_IPlayerHandle carrier)
        {
            int found = 0;
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                if (handle == null || handle.Side == carrier.Side)
                {
                    continue;
                }

                float seconds = SecondsToReach(slotIndex, handle, carrier);

                if (!(seconds < PURSUIT_HORIZON_SECONDS))
                {
                    continue;
                }

                found = InsertSorted(slotIndex, seconds, found);
            }

            for (int rank = 0; rank < found; rank++)
            {
                Systems_IPlayerHandle pursuer = _registry.Get(_pursuerSlots[rank]);
                float seconds = _pursuerSeconds[rank];
                Vector2 meet = carrier.Position + (carrier.Velocity * seconds);

                float reach = 1f - (seconds / PURSUIT_HORIZON_SECONDS);
                float rankFade = 1f - (rank * 0.3f);
                Color color = Systems_UiTheme.Negative;
                color.a = CONE_ALPHA * reach * rankFade;
                Color32 packed = color;

                AppendCone(pursuer.Position, meet, packed);

                if (rank == 0)
                {
                    color.a = Mathf.Min(1f, color.a * 2.5f);
                    AppendArc(meet, MEET_RING_RADIUS, 0f, Mathf.PI * 2f, MEET_RING_WIDTH, color);
                }
            }
        }

        /// <summary>Insertion into the fixed top-three arrays, soonest first. Returns the new count.</summary>
        private int InsertSorted(int slotIndex, float seconds, int count)
        {
            int position = count;

            while (position > 0 && _pursuerSeconds[position - 1] > seconds)
            {
                position--;
            }

            if (position >= MAX_PURSUERS)
            {
                return count;
            }

            int last = Mathf.Min(count, MAX_PURSUERS - 1);

            for (int index = last; index > position; index--)
            {
                _pursuerSlots[index] = _pursuerSlots[index - 1];
                _pursuerSeconds[index] = _pursuerSeconds[index - 1];
            }

            _pursuerSlots[position] = slotIndex;
            _pursuerSeconds[position] = seconds;
            return Mathf.Min(count + 1, MAX_PURSUERS);
        }

        private void AppendPocketClock(Systems_IPlayerHandle passer)
        {
            float nearest = float.PositiveInfinity;
            int capacity = Systems_PlayerRegistry.CAPACITY;

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                if (handle == null || handle.Side == passer.Side)
                {
                    continue;
                }

                float seconds = SecondsToReach(slotIndex, handle, passer);

                if (seconds < nearest)
                {
                    nearest = seconds;
                }
            }

            float remaining = float.IsInfinity(nearest)
                ? 1f
                : Mathf.Clamp01(nearest / POCKET_FULL_SECONDS);

            if (remaining <= 0.001f)
            {
                return;
            }

            Color color = Color.Lerp(Systems_UiTheme.Negative, Systems_UiTheme.Positive, remaining);
            color.a = POCKET_ALPHA;

            // Clockwise from twelve o'clock, as Systems_FatigueRingView draws, so
            // the two rings read the same way.
            float start = Mathf.PI * 0.5f;
            AppendArc(passer.Position, POCKET_RING_RADIUS, start, start - (Mathf.PI * 2f * remaining),
                POCKET_RING_WIDTH, color);
        }

        private void AppendCone(Vector2 apex, Vector2 meet, Color32 color)
        {
            Vector2 span = meet - apex;

            if (span.sqrMagnitude < 1e-4f)
            {
                return;
            }

            Vector2 flank = new Vector2(-span.y, span.x).normalized * CONE_HALF_WIDTH;
            Color32 apexColor = color;
            apexColor.a = (byte)(color.a / 3);

            int baseIndex = _vertices.Count;

            _vertices.Add(apex);
            _vertices.Add(meet - flank);
            _vertices.Add(meet + flank);

            _colors.Add(apexColor);
            _colors.Add(color);
            _colors.Add(color);

            _indices.Add(baseIndex);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 2);
        }

        /// <summary>An arc between two angles in radians, either direction, as chords of a ring.</summary>
        private void AppendArc(Vector2 centre, float radius, float fromRadians, float toRadians, float width, Color32 color)
        {
            float sweep = toRadians - fromRadians;
            int segments = Mathf.Max(3, Mathf.CeilToInt(SEGMENTS_FULL_CIRCLE * Mathf.Abs(sweep) / (Mathf.PI * 2f)));
            float step = sweep / segments;

            Vector2 from = centre + (new Vector2(Mathf.Cos(fromRadians), Mathf.Sin(fromRadians)) * radius);

            for (int segment = 1; segment <= segments; segment++)
            {
                float angle = fromRadians + (step * segment);
                Vector2 to = centre + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                AppendQuad(from, to, width, color);
                from = to;
            }
        }

        private void AppendQuad(Vector2 from, Vector2 to, float width, Color32 color)
        {
            Vector2 span = to - from;

            if (span.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector2 offset = new Vector2(-span.y, span.x).normalized * (width * 0.5f);
            int baseIndex = _vertices.Count;

            _vertices.Add(from - offset);
            _vertices.Add(from + offset);
            _vertices.Add(to + offset);
            _vertices.Add(to - offset);

            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);

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
            if (_mesh == null)
            {
                return;
            }

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
    }
}

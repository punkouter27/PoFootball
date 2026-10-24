using System.Collections.Generic;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// The DEBUG sheet's PHYSICS switch: what the solver is working with, drawn on
    /// the field over every body.
    ///
    ///     ring      the body's actual CircleCollider2D, at its world radius —
    ///               thicker on the carrier
    ///     arrow     velocity, a quarter of a second ahead (so its length is
    ///               where the body will be at that speed), yellow
    ///     tick      facing, the vector the officials judge "from behind" by
    ///     red arc   fatigue, clockwise from the facing tick, a full ring at 1
    ///     orange    the ball's velocity, while it moves
    ///
    /// WHY THIS AND NOT THE INTENT ARROWS. Systems_IntentOverlayView draws what a
    /// policy DECIDED; its per-player arrows are a const switch for that reason.
    /// This draws what the bodies DID — and the questions a debug session asks
    /// about a play are physical ones: is that pile three bodies overlapping
    /// past their radii, is a lineman's velocity pointing where he is facing, is
    /// the carrier spent. None of that is readable off twenty-two coloured shapes.
    ///
    /// A DEBUG BUILD'S INSTRUMENT, OFF UNTIL ASKED. Systems_StatusHudBootstrap
    /// constructs it only in a game scene of a debug build, and nothing is drawn
    /// until the sheet's switch turns it on; a retail build has neither the class
    /// instance nor the mesh.
    ///
    /// ONE MESH, ONE DRAW CALL, in the style of Systems_IntentOverlayView, whose
    /// docstring has the reasoning: per-vertex colour on the shared unlit
    /// particle material, lists cleared rather than reallocated, so a frame
    /// allocates nothing. Read-only: it never writes to a body or a model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_PhysicsDebugView : MonoBehaviour
    {
        /// <summary>Above the intent overlay (3) and the ball trail (4): an instrument goes on top.</summary>
        private const int SORTING_ORDER = 6;

        private const int RING_SEGMENTS = 20;
        private const float RING_WIDTH = 0.05f;
        private const float CARRIER_RING_WIDTH = 0.12f;

        /// <summary>Seconds of travel the velocity arrow spans.</summary>
        private const float VELOCITY_LOOKAHEAD = 0.25f;

        private const float ARROW_WIDTH = 0.08f;
        private const float HEAD_LENGTH = 0.3f;
        private const float HEAD_WIDTH = 0.26f;

        /// <summary>Below this a body is standing and gets no arrow — 22 stubs twitching in a formation read as noise.</summary>
        private const float MIN_SPEED_TO_DRAW = 0.4f;

        private const float FACING_LENGTH = 1.5f;
        private const float FATIGUE_GAP = 0.1f;

        private static readonly Color32 OffenseColor = new Color32(90, 215, 255, 230);
        private static readonly Color32 DefenseColor = new Color32(255, 115, 190, 230);
        private static readonly Color32 VelocityColor = new Color32(255, 225, 60, 240);
        private static readonly Color32 FacingColor = new Color32(255, 255, 255, 220);
        private static readonly Color32 FatigueColor = new Color32(235, 60, 70, 240);
        private static readonly Color32 BallColor = new Color32(255, 150, 40, 255);

        /// <summary>See Systems_IntentOverlayView.FieldBounds: a fixed box beats recomputing bounds that can never cull.</summary>
        private static readonly Bounds FieldBounds = new Bounds(Vector3.zero, new Vector3(240f, 240f, 1f));

        private Systems_PlayerRegistry _registry;
        private Systems_BallModel _ball;

        private Mesh _mesh;
        private MeshRenderer _meshRenderer;
        private bool _built;

        private readonly CircleCollider2D[] _colliders = new CircleCollider2D[Systems_PlayerRegistry.CAPACITY];

        private readonly List<Vector3> _vertices = new List<Vector3>(4096);
        private readonly List<Color32> _colors = new List<Color32>(4096);
        private readonly List<int> _indices = new List<int>(6144);

        /// <summary>Written by the DEBUG sheet's switch. Off until then.</summary>
        public bool IsShowing { get; set; }

        [Inject]
        public void Construct(Systems_PlayerRegistry registry, Systems_BallModel ball)
        {
            _registry = registry;
            _ball = ball;
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
            if (!IsShowing)
            {
                if (_meshRenderer != null && _meshRenderer.enabled)
                {
                    _meshRenderer.enabled = false;
                }

                return;
            }

            if (!_built && !TryBuild())
            {
                return;
            }

            using var presentationCost = Systems_PresentationProfiler.Measure();

            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();

            for (int slot = 0; slot < Systems_PlayerRegistry.CAPACITY; slot++)
            {
                Systems_IPlayerHandle handle = _registry.Get(slot);

                if (handle != null)
                {
                    AppendBody(slot, handle);
                }
            }

            if (_ball != null && !_ball.IsHeld && _ball.Velocity.sqrMagnitude > MIN_SPEED_TO_DRAW * MIN_SPEED_TO_DRAW)
            {
                AppendArrow(_ball.Position, _ball.Velocity * VELOCITY_LOOKAHEAD, BallColor);
            }

            Upload();
        }

        /// <summary>
        /// Built on the first frame it is switched on, never before: a session that
        /// never opens the switch never pays for the mesh.
        /// </summary>
        private bool TryBuild()
        {
            if (_registry == null)
            {
                IsShowing = false;
                return false;
            }

            if (!Systems_ParticleMaterial.TryLoad(
                    nameof(Systems_PhysicsDebugView), "No physics view is drawn.", out Material source))
            {
                IsShowing = false;
                return false;
            }

            _mesh = new Mesh { name = "PoFootball Physics Debug" };
            _mesh.MarkDynamic();

            GameObject host = new GameObject("PhysicsDebug");
            host.transform.SetParent(transform, false);

            host.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _meshRenderer = host.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = source;
            _meshRenderer.sortingOrder = SORTING_ORDER;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            _built = true;
            return true;
        }

        private void AppendBody(int slot, Systems_IPlayerHandle handle)
        {
            CircleCollider2D body = _colliders[slot];

            if (body == null && handle is Component component)
            {
                body = component.GetComponentInChildren<CircleCollider2D>();
                _colliders[slot] = body;
            }

            Vector2 centre = handle.Position;
            float radius = 0.5f;

            if (body != null)
            {
                Vector3 scale = body.transform.lossyScale;
                radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                centre = body.transform.TransformPoint(body.offset);
            }

            Color32 teamColor = handle.Side == Systems_TeamSide.Offense ? OffenseColor : DefenseColor;

            AppendArc(centre, radius, 0f, 360f,
                handle.IsCarrier ? CARRIER_RING_WIDTH : RING_WIDTH, teamColor);

            Vector2 facing = handle.Facing;

            if (facing.sqrMagnitude > 1e-4f)
            {
                facing.Normalize();
                AppendQuad(centre + (facing * radius), centre + (facing * radius * FACING_LENGTH),
                    RING_WIDTH * 1.5f, FacingColor);

                float fatigue = Mathf.Clamp01(handle.Fatigue);

                if (fatigue > 0.01f)
                {
                    float start = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
                    AppendArc(centre, radius + FATIGUE_GAP, start - (fatigue * 360f), start,
                        RING_WIDTH * 2f, FatigueColor);
                }
            }

            Vector2 velocity = handle.Velocity;

            if (velocity.sqrMagnitude > MIN_SPEED_TO_DRAW * MIN_SPEED_TO_DRAW)
            {
                AppendArrow(centre, velocity * VELOCITY_LOOKAHEAD, VelocityColor);
            }
        }

        private void AppendArrow(Vector2 from, Vector2 span, Color32 color)
        {
            float length = span.magnitude;

            if (length < 1e-3f)
            {
                return;
            }

            Vector2 direction = span / length;
            float head = Mathf.Min(HEAD_LENGTH, length * 0.5f);
            Vector2 headBase = from + (direction * (length - head));

            AppendQuad(from, headBase, ARROW_WIDTH, color);

            Vector2 flank = new Vector2(-direction.y, direction.x) * (HEAD_WIDTH * 0.5f);
            int baseIndex = _vertices.Count;

            _vertices.Add(headBase - flank);
            _vertices.Add(headBase + flank);
            _vertices.Add(from + span);

            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);

            _indices.Add(baseIndex);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 2);
        }

        /// <summary>An arc from one angle to another, counter-clockwise, in degrees, as chords of a ring.</summary>
        private void AppendArc(Vector2 centre, float radius, float fromDegrees, float toDegrees, float width, Color32 color)
        {
            float sweep = toDegrees - fromDegrees;
            int segments = Mathf.Max(2, Mathf.CeilToInt(RING_SEGMENTS * Mathf.Abs(sweep) / 360f));
            float step = sweep / segments * Mathf.Deg2Rad;
            float angle = fromDegrees * Mathf.Deg2Rad;

            Vector2 previous = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            for (int segment = 1; segment <= segments; segment++)
            {
                angle += step;
                Vector2 next = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                AppendQuad(previous, next, width, color);
                previous = next;
            }
        }

        private void AppendQuad(Vector2 from, Vector2 to, float width, Color32 color)
        {
            Vector2 span = to - from;

            if (span.sqrMagnitude < 1e-8f)
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
    }
}

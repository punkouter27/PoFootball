using System.Collections.Generic;
using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using VContainer;

namespace PoFootball.Views
{
    /// <summary>
    /// A thin arc under every player who is tiring, its length the share of the
    /// tank that is spent: amber as it starts to tell, red when the player is
    /// nearly done.
    ///
    /// WHAT IT MAKES VISIBLE. Fatigue has been real since revision 12 — it reads
    /// load from applied force and from the contact impulse a braced lineman
    /// absorbs, it carries 60% from one play to the next, and it costs a tired
    /// body up to FATIGUE_MAX_PENALTY of its drive — but the only place a viewer
    /// could see it was the focus card, one player at a time. A long drive should
    /// LOOK long: by the twelfth play the defensive front is ringed in red, and the
    /// eight-yard run that follows has a reason a viewer can see.
    ///
    /// ONLY THE TIRED ONES. Rings start at SHOW_FROM: twenty-two fresh players
    /// each wearing a sliver would be visual noise that says nothing.
    ///
    /// One mesh, vertex colours, the shared particle material, rebuilt in
    /// LateUpdate into Lists that are cleared rather than reallocated — the same
    /// recipe as Systems_SeparationView, so a steady-state frame allocates nothing.
    /// Gated on Systems_PresentationBudget and Systems_BroadcastSettings.FatigueRings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Systems_FatigueRingView : MonoBehaviour, Systems_IInjectableView
    {
        /// <summary>Under the separation rings (3) and over the turf marks.</summary>
        private const int SORTING_ORDER = 2;

        /// <summary>Fatigue at which a ring appears.</summary>
        /// <summary>
        /// Fatigue at which a ring appears. 0.3 ringed most of the field by the
        /// first quarter's second drive on a capture; 0.4 keeps it to the tired.
        /// </summary>
        private const float SHOW_FROM = 0.4f;

        /// <summary>Fatigue at which the ring is fully red.</summary>
        private const float RED_AT = 0.75f;

        private const float RING_RADIUS = 0.78f;
        private const float RING_WIDTH = 0.11f;
        private const int SEGMENTS_FULL_CIRCLE = 28;

        private const float RING_ALPHA = 0.7f;

        /// <summary>See Systems_IntentOverlayView.FieldBounds — same reasoning.</summary>
        private static readonly Bounds FieldBounds =
            new Bounds(Vector3.zero, new Vector3(240f, 240f, 1f));

        private Systems_PlayerRegistry _registry;
        private Systems_PresentationBudget _budget;

        private Mesh _mesh;
        private MeshRenderer _meshRenderer;

        private readonly List<Vector3> _vertices = new List<Vector3>(2048);
        private readonly List<Color32> _colors = new List<Color32>(2048);
        private readonly List<int> _indices = new List<int>(3072);

        [Inject]
        public void Construct(Systems_PlayerRegistry registry, Systems_PresentationBudget budget)
        {
            _registry = registry;
            _budget = budget;
        }

        private void Start()
        {
            if (_budget == null || !_budget.EffectsEnabled || _registry == null)
            {
                enabled = false;
                return;
            }

            if (!Systems_ParticleMaterial.TryLoad(
                    nameof(Systems_FatigueRingView), "No fatigue rings are drawn.", out Material source))
            {
                enabled = false;
                return;
            }

            _mesh = new Mesh { name = "PoFootball Fatigue Rings" };
            _mesh.MarkDynamic();

            GameObject host = new GameObject("FatigueRings");
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

            if (Systems_BroadcastSettings.FatigueRings)
            {
                int capacity = Systems_PlayerRegistry.CAPACITY;

                for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
                {
                    Systems_IPlayerHandle handle = _registry.Get(slotIndex);

                    if (handle == null || handle.Fatigue < SHOW_FROM)
                    {
                        continue;
                    }

                    AppendRing(handle.Position, Mathf.Clamp01(handle.Fatigue));
                }
            }

            Upload();
        }

        /// <summary>
        /// The spent share, clockwise from twelve o'clock, so every ring starts
        /// from the same place and two of them can be compared at a glance.
        /// </summary>
        private void AppendRing(Vector2 centre, float fatigue)
        {
            float redness = Mathf.InverseLerp(SHOW_FROM, RED_AT, fatigue);
            Color color = Color.Lerp(Systems_UiTheme.Accent, Systems_UiTheme.Negative, redness);
            color.a = RING_ALPHA * Mathf.InverseLerp(SHOW_FROM, SHOW_FROM + 0.1f, fatigue);
            Color32 packed = color;

            float sweep = Mathf.PI * 2f * fatigue;
            int segments = Mathf.Max(3, Mathf.CeilToInt(SEGMENTS_FULL_CIRCLE * fatigue));
            float start = Mathf.PI * 0.5f;
            float step = -sweep / segments;

            Vector2 from = centre + (new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * RING_RADIUS);

            for (int segment = 1; segment <= segments; segment++)
            {
                float angle = start + (step * segment);
                Vector2 to = centre + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RING_RADIUS);
                AppendQuad(from, to, packed);
                from = to;
            }
        }

        private void AppendQuad(Vector2 from, Vector2 to, Color32 color)
        {
            Vector2 span = to - from;

            if (span.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector2 offset = new Vector2(-span.y, span.x).normalized * (RING_WIDTH * 0.5f);
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

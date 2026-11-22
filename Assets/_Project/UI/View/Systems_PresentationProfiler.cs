using Unity.Profiling;

namespace PoFootball.Views
{
    /// <summary>
    /// One profiler marker every presentation view's per-frame work runs inside,
    /// so the DEBUG sheet can say what the decoration costs a frame on the device
    /// in a player's hand — the one number the broadcast pass of 2026-10-22 shipped
    /// without ("the frame cost has not been profiled", docs/PRESENTATION.md).
    ///
    /// ONE MARKER, NOT ONE PER VIEW. The sheet's question is whether the effects,
    /// as a set, are what stands between this device and 60 FPS; a Profiler capture
    /// already breaks the frame down by script for anyone who needs the split. One
    /// name is also one ProfilerRecorder in Systems_StatusHudView instead of twenty.
    ///
    /// Put it on LEAF methods only — a view's own Update or LateUpdate — and never
    /// on Systems_ScreenView's, which its subclasses call into: a marker nested in
    /// itself is counted twice.
    ///
    /// <c>using var scope = Systems_PresentationProfiler.Measure();</c> as the first
    /// line: ProfilerMarker.AutoScope is a struct, so this allocates nothing, and
    /// in a release player ProfilerMarker compiles to nothing at all.
    /// </summary>
    internal static class Systems_PresentationProfiler
    {
        public const string MARKER_NAME = "PoFootball.Presentation";

        private static readonly ProfilerMarker Marker = new ProfilerMarker(MARKER_NAME);

        public static ProfilerMarker.AutoScope Measure()
        {
            return Marker.Auto();
        }
    }
}

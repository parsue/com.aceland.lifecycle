#if ENABLE_PROFILER
using Unity.Profiling;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Central registry of <see cref="ProfilerMarker"/>s used to surface Lifecycle's per-frame and
    /// per-module work inside the Unity Profiler window (and deep profiling). The entire type is
    /// compiled only when <c>ENABLE_PROFILER</c> is defined (Editor + development builds), so release
    /// builds carry zero marker fields and zero sampling overhead.
    /// </summary>
    /// <remarks>
    /// Naming convention uses '.' separators (never '/', which breaks Profiler chart highlighting):
    /// <list type="bullet">
    ///   <item><c>AceLand.Lifecycle.Pump.&lt;Point&gt;</c> — one per <see cref="PlayerLoopPoint"/> (Level A).</item>
    ///   <item><c>AceLand.Lifecycle.Frame.&lt;Type.Member:line&gt;</c> — one per scheduled action (Level B, built on the handle from the scheduling call site; falls back to the action's <c>Type.Method</c>).</item>
    ///   <item><c>AceLand.Lifecycle.Module.&lt;DisplayName&gt;</c> — one per module init (Level C, built on the entry).</item>
    /// </list>
    /// </remarks>
    internal static class LifecycleProfilerMarkers
    {
        private const string PumpPrefix = "AceLand.Lifecycle.Pump.";
        internal const string FramePrefix = "AceLand.Lifecycle.Frame.";
        internal const string ModulePrefix = "AceLand.Lifecycle.Module.";

        /// <summary>
        /// One marker per <see cref="PlayerLoopPoint"/>, indexed by <c>(int)point</c>. Wraps the whole
        /// per-point pump in <see cref="FrameScheduler"/> so the Profiler shows the aggregate cost of a
        /// given injection point separately from the individual handles running under it.
        /// </summary>
        internal static readonly ProfilerMarker[] Pump = BuildPumpMarkers();

        private static ProfilerMarker[] BuildPumpMarkers()
        {
            var names = System.Enum.GetNames(typeof(PlayerLoopPoint));
            var markers = new ProfilerMarker[names.Length];
            for (var i = 0; i < names.Length; i++)
                markers[i] = new ProfilerMarker(PumpPrefix + names[i]);
            return markers;
        }
    }
}
#endif

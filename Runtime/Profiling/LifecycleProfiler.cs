using System.Collections.Generic;
using System.Diagnostics;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Central switch and collector for initialization profiling. When <see cref="Enabled"/> is true, the
    /// <see cref="ModuleRegistry"/> records per-module (sync / async split, start / end) and per-phase timings,
    /// which the Editor timeline window visualizes as a Gantt chart.
    /// <para>
    /// When disabled, the cost is effectively nothing — a single bool check per module and per phase, and none
    /// of the timing fields are populated. The default is <b>on in the Editor and development builds, off in
    /// release players</b>; it can be toggled online at runtime via <see cref="Enabled"/>.
    /// </para>
    /// All times are in milliseconds relative to the run origin (the moment the first phase starts).
    /// </summary>
    public static class LifecycleProfiler
    {
        private static bool _enabled =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>
        /// Whether initialization profiling data is collected. Can be toggled online; changing it mid-run only
        /// affects modules and phases that have not started yet (data already captured is kept).
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        private static readonly List<PhaseTimingInfo> _phases = new();
        private static readonly Stopwatch _runClock = new();

        /// <summary>Milliseconds elapsed since the run origin. Returns <c>0</c> before the run clock starts.</summary>
        internal static double Now => _runClock.Elapsed.TotalMilliseconds;

        /// <summary>Starts the shared run clock on first use; subsequent calls are no-ops.</summary>
        internal static void EnsureRunStarted()
        {
            if (!_runClock.IsRunning) _runClock.Start();
        }

        /// <summary>Records a completed phase summary.</summary>
        internal static void RecordPhase(in PhaseTimingInfo info) => _phases.Add(info);

        /// <summary>Clears all collected data and the run clock. Called on registry reset (no domain reload).</summary>
        internal static void Reset()
        {
            _phases.Clear();
            _runClock.Reset();
        }

        /// <summary>
        /// Builds an immutable snapshot of the run from the current registry entries and recorded phases.
        /// Returns <see cref="LifecycleTimeline.Empty"/> when nothing was profiled (disabled or no run yet).
        /// </summary>
        internal static LifecycleTimeline BuildTimeline(IReadOnlyList<ModuleEntry> entries, double resultMs)
        {
            if (entries == null || entries.Count == 0)
                return LifecycleTimeline.Empty;

            var modules = new List<ModuleTimingInfo>(entries.Count);
            var maxEnd = 0d;
            var anyRan = false;

            foreach (var e in entries)
            {
                var info = new ModuleTimingInfo(e);
                modules.Add(info);
                if (!info.DidRun) continue;

                anyRan = true;
                if (info.EndedAtMs > maxEnd) maxEnd = info.EndedAtMs;
            }

            if (!anyRan && _phases.Count == 0)
                return LifecycleTimeline.Empty;

            // Modules that ran come first, ordered by start time; ones that never ran (skipped on unmet
            // dependency, or captured while profiling was off) fall to the end for a "did not run" section.
            modules.Sort(static (a, b) =>
            {
                var sa = a.DidRun ? a.StartedAtMs : double.MaxValue;
                var sb = b.DidRun ? b.StartedAtMs : double.MaxValue;
                var c = sa.CompareTo(sb);
                if (c != 0) return c;
                c = ((int)a.Phase).CompareTo((int)b.Phase);
                return c != 0 ? c : a.SortIndex.CompareTo(b.SortIndex);
            });

            // Chart span = the real "last module finished" time (maxEnd). The initialization
            // result time (resultMs) can include long idle waits (scene loads, frame pumping)
            // that would leave the Gantt axis mostly empty, so we prefer maxEnd for the timeline.
            var total = maxEnd > 0 ? maxEnd : resultMs;
            return new LifecycleTimeline(_phases.ToArray(), modules.ToArray(), total);
        }
    }
}

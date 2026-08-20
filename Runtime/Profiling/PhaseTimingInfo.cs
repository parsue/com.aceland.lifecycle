namespace AceLand.Lifecycle
{
    /// <summary>
    /// Timing summary for a single initialization <see cref="ModulePhase"/>, captured by
    /// <see cref="LifecycleProfiler"/>. All times are in milliseconds relative to the run origin
    /// (the moment the first phase started).
    /// </summary>
    public readonly struct PhaseTimingInfo
    {
        /// <summary>Which phase this record describes.</summary>
        public readonly ModulePhase Phase;

        /// <summary>When the phase started, in milliseconds relative to the run origin.</summary>
        public readonly double StartedAtMs;

        /// <summary>When the phase finished, in milliseconds relative to the run origin.</summary>
        public readonly double EndedAtMs;

        /// <summary>How many modules ran in this phase.</summary>
        public readonly int ModuleCount;

        /// <summary>
        /// Number of dependency levels (concurrency batches) the phase was split into. Modules in
        /// the same level have no ordering dependency and — when eligible — run in parallel.
        /// </summary>
        public readonly int Batches;

        /// <summary>True if the phase was forced forward after exceeding its timeout budget.</summary>
        public readonly bool TimedOut;

        public double DurationMs => EndedAtMs - StartedAtMs;

        internal PhaseTimingInfo(ModulePhase phase, double startedAtMs, double endedAtMs,
                                 int moduleCount, int batches, bool timedOut)
        {
            Phase = phase;
            StartedAtMs = startedAtMs;
            EndedAtMs = endedAtMs;
            ModuleCount = moduleCount;
            Batches = batches;
            TimedOut = timedOut;
        }

        public override string ToString() =>
            $"{Phase}: {DurationMs:0.0} ms, {ModuleCount} modules, {Batches} batch(es)" +
            (TimedOut ? " (timed out)" : string.Empty);
    }
}

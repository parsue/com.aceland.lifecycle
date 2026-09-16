namespace AceLand.Lifecycle
{
    /// <summary>
    /// Controls how long the Lifecycle frame pump (the PlayerLoop nodes that drive
    /// <see cref="LifecycleFrame"/> per-frame work) stays alive once a quit has been
    /// requested. The pump is removed at the chosen checkpoint inside the quit
    /// pipeline. Regardless of the choice, the pump is always removed as a final
    /// backstop before the app actually quits, because
    /// <c>LifecyclePlayerLoop.EnsureRemoved()</c> is idempotent.
    /// </summary>
    public enum PlayerLoopQuitLifespan
    {
        /// <summary>
        /// Remove the frame pump the instant a quit is requested — before waiting for
        /// blockers and before running quit handlers.
        ///
        /// WARNING: once the pump is gone, any blocker or quit handler that relies on
        /// frame advancement (<c>LifecycleFrame.RunEveryFrame</c> and friends) can no
        /// longer make progress. If such work is not already idle it will stall until
        /// the pipeline timeout (see <c>TIMEOUT_SECONDS</c>) forces the quit forward.
        /// Only choose this when every blocker/handler is purely async/IO driven and
        /// never depends on the Lifecycle frame pump.
        /// </summary>
        OnWantToQuit,

        /// <summary>
        /// Default. Remove the frame pump after all blockers have drained but before
        /// quit handlers run. Blockers may safely rely on frame advancement; handlers
        /// must not.
        /// </summary>
        AfterBlockers,

        /// <summary>
        /// Keep the frame pump alive until the very last moment — after blockers,
        /// after all quit handlers, and after module shutdown. Safest option: every
        /// blocker and handler can rely on frame advancement.
        /// </summary>
        LastMoment,
    }
}

using System;
using System.Threading;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Frame-level scheduling entry point. Runs plain <see cref="Action"/> callbacks on the Unity main
    /// thread at a chosen <see cref="PlayerLoopPoint"/>, without needing a <c>MonoBehaviour</c> or coroutine.
    /// <para>
    /// Every method returns an <see cref="IFrameHandle"/> that can be:
    /// <list type="bullet">
    /// <item><description><c>using</c>-ed — <c>Dispose()</c> cancels the pending work.</description></item>
    /// <item><description><c>await</c>-ed — resumes once the work has run (or throws
    /// <see cref="OperationCanceledException"/> if cancelled / the app shut down).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Insurance:</b> handles are owned by the internal <see cref="FrameScheduler"/> and force-cancelled
    /// on quit / play-mode exit / assembly reload, so nothing is ever left dangling (CoreCLR premise —
    /// no reliance on domain reload).
    /// </para>
    /// </summary>
    public static class LifecycleFrame
    {
        /// <summary>The player-loop point used when none is specified.</summary>
        public const PlayerLoopPoint DefaultPoint = PlayerLoopPoint.Update;

        // ── RunNextFrame ────────────────────────────────────────────────────

        /// <summary>Runs <paramref name="action"/> once on the next frame.</summary>
        public static IFrameHandle RunNextFrame(Action action, CancellationToken cancellationToken = default)
            => RunAfterFrames(action, 1, DefaultPoint, cancellationToken);

        // ── RunAfterFrames ──────────────────────────────────────────────────

        /// <summary>Runs <paramref name="action"/> once after <paramref name="frames"/> frames (min 1).</summary>
        public static IFrameHandle RunAfterFrames(Action action, int frames, CancellationToken cancellationToken = default)
            => RunAfterFrames(action, frames, DefaultPoint, cancellationToken);

        /// <summary>Runs <paramref name="action"/> once after <paramref name="frames"/> frames at the given <paramref name="point"/>.</summary>
        public static IFrameHandle RunAfterFrames(Action action, int frames, PlayerLoopPoint point,
            CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.AfterFrames(action, Math.Max(1, frames), point, linked.Token, linked));
        }

        // ── RunWhen ─────────────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> once, on the first frame where <paramref name="predicate"/>
        /// evaluates to <c>true</c>. The predicate is polled once per frame on the main thread.
        /// </summary>
        public static IFrameHandle RunWhen(Func<bool> predicate, Action action,
            CancellationToken cancellationToken = default)
            => RunWhen(predicate, action, DefaultPoint, cancellationToken);

        /// <inheritdoc cref="RunWhen(System.Func{bool},System.Action,System.Threading.CancellationToken)"/>
        public static IFrameHandle RunWhen(Func<bool> predicate, Action action, PlayerLoopPoint point,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.When(predicate, action, point, linked.Token, linked));
        }

        // ── RunEveryFrame ───────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> every frame, optionally capped at <paramref name="maxFrames"/>
        /// executions. Dispose (or cancel) the handle to stop it early. Useful as a temporary pump.
        /// </summary>
        public static IFrameHandle RunEveryFrame(Action action, int? maxFrames = null,
            CancellationToken cancellationToken = default)
            => RunEveryFrame(action, DefaultPoint, maxFrames, cancellationToken);

        /// <inheritdoc cref="RunEveryFrame(System.Action,System.Nullable{int},System.Threading.CancellationToken)"/>
        public static IFrameHandle RunEveryFrame(Action action, PlayerLoopPoint point,
            int? maxFrames = null, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.EveryFrame(action, maxFrames, point, linked.Token, linked));
        }

        // ── RunDelayed ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> once after <paramref name="seconds"/> have elapsed, timed on the
        /// main-thread frame pump. Defaults to scaled time; pass <paramref name="unscaled"/> = <c>true</c>
        /// to use unscaled time.
        /// </summary>
        public static IFrameHandle RunDelayed(Action action, float seconds, bool unscaled = false,
            CancellationToken cancellationToken = default)
            => RunDelayed(action, seconds, DefaultPoint, unscaled, cancellationToken);

        /// <inheritdoc cref="RunDelayed(System.Action,float,bool,System.Threading.CancellationToken)"/>
        public static IFrameHandle RunDelayed(Action action, float seconds, PlayerLoopPoint point,
            bool unscaled = false, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.Delayed(action, seconds, unscaled, point, linked.Token, linked));
        }
    }
}

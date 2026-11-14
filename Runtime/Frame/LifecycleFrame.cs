using System;
using System.Runtime.CompilerServices;
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
    /// <para>
    /// <b>Profiling:</b> the <c>[Caller*]</c> parameters are compiler-populated and used only to label the
    /// Unity Profiler marker with the real scheduling call site (e.g. <c>Type.Method:line</c>) so distinct
    /// callers stay distinct instead of collapsing under a shared lambda name. They carry no runtime cost
    /// outside the Profiler-enabled build and should never be supplied explicitly by callers.
    /// </para>
    /// </summary>
    public static class LifecycleFrame
    {
        /// <summary>The player-loop point used when none is specified.</summary>
        public const PlayerLoopPoint DefaultPoint = PlayerLoopPoint.EarlyUpdate;

        // ── RunNextFrame ────────────────────────────────────────────────────

        /// <summary>Runs <paramref name="action"/> once on the next frame.</summary>
        public static IFrameHandle RunNextFrame(Action action, CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
            => RunAfterFrames(action, 1, DefaultPoint, cancellationToken, caller, callerFile, callerLine);
        
        /// <summary>Runs <paramref name="action"/> once after <paramref name="point"/>.</summary>
        public static IFrameHandle RunNextFrame(Action action, PlayerLoopPoint point, CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
            => RunAfterFrames(action, 1, point, cancellationToken, caller, callerFile, callerLine);

        // ── RunAfterFrames ──────────────────────────────────────────────────

        /// <summary>Runs <paramref name="action"/> once after <paramref name="frames"/> frames (min 1).</summary>
        public static IFrameHandle RunAfterFrames(Action action, int frames, CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
            => RunAfterFrames(action, frames, DefaultPoint, cancellationToken, caller, callerFile, callerLine);

        /// <summary>Runs <paramref name="action"/> once after <paramref name="frames"/> frames at the given <paramref name="point"/>.</summary>
        public static IFrameHandle RunAfterFrames(Action action, int frames, PlayerLoopPoint point,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.AfterFrames(action, Math.Max(1, frames), point, linked.Token, linked,
                    CallSite(caller, callerFile, callerLine)));
        }

        // ── RunWhen ─────────────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> once, on the first frame where <paramref name="predicate"/>
        /// evaluates to <c>true</c>. The predicate is polled once per frame on the main thread.
        /// </summary>
        public static IFrameHandle RunWhen(Func<bool> predicate, Action action,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
            => RunWhen(predicate, action, DefaultPoint, cancellationToken, caller, callerFile, callerLine);

        /// <inheritdoc cref="RunWhen(System.Func{bool},System.Action,System.Threading.CancellationToken,string,string,int)"/>
        public static IFrameHandle RunWhen(Func<bool> predicate, Action action, PlayerLoopPoint point,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.When(predicate, action, point, linked.Token, linked,
                    CallSite(caller, callerFile, callerLine)));
        }

        // ── RunEveryFrame ───────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> every frame, optionally capped at <paramref name="maxFrames"/>
        /// executions. Dispose (or cancel) the handle to stop it early. Useful as a temporary pump.
        /// </summary>
        public static IFrameHandle RunEveryFrame(Action action, int? maxFrames = null,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
            => RunEveryFrame(action, DefaultPoint, maxFrames, cancellationToken, caller, callerFile, callerLine);

        /// <inheritdoc cref="RunEveryFrame(System.Action,System.Nullable{int},System.Threading.CancellationToken,string,string,int)"/>
        public static IFrameHandle RunEveryFrame(Action action, PlayerLoopPoint point,
            int? maxFrames = null, CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.EveryFrame(action, maxFrames, point, linked.Token, linked,
                    CallSite(caller, callerFile, callerLine)));
        }

        // ── RunDelayed ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> once after <paramref name="seconds"/> have elapsed, timed on the
        /// main-thread frame pump. Defaults to scaled time; pass <paramref name="unscaled"/> = <c>true</c>
        /// to use unscaled time.
        /// </summary>
        public static IFrameHandle RunDelayed(Action action, float seconds, bool unscaled = false,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
            => RunDelayed(action, seconds, DefaultPoint, unscaled, cancellationToken, caller, callerFile, callerLine);

        /// <inheritdoc cref="RunDelayed(System.Action,float,bool,System.Threading.CancellationToken,string,string,int)"/>
        public static IFrameHandle RunDelayed(Action action, float seconds, PlayerLoopPoint point,
            bool unscaled = false, CancellationToken cancellationToken = default,
            [CallerMemberName] string caller = null, [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var linked = LifecycleToken.CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.Delayed(action, seconds, unscaled, point, linked.Token, linked,
                    CallSite(caller, callerFile, callerLine)));
        }

        /// <summary>
        /// Builds the Profiler marker call-site label "<c>Type.Member:line</c>" from compiler-supplied
        /// caller info. Returns <c>null</c> outside Profiler-enabled builds so no string is ever allocated
        /// when it would not be used (<see cref="FrameHandle"/> then falls back to the action's method name).
        /// </summary>
        private static string CallSite(string caller, string callerFile, int callerLine)
        {
#if ENABLE_PROFILER
            var type = string.IsNullOrEmpty(callerFile)
                ? null
                : System.IO.Path.GetFileNameWithoutExtension(callerFile);
            return string.IsNullOrEmpty(type) ? caller : $"{type}.{caller}:{callerLine}";
#else
            return null;
#endif
        }
    }
}

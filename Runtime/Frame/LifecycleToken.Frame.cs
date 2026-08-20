using System;
using System.Threading;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Frame-level extension of <see cref="LifecycleToken"/> (§2.2): frame-scoped cancellation tokens and
    /// awaitable wait helpers, all driven by the main-thread <see cref="FrameScheduler"/> / <see cref="FrameHandle"/>.
    /// <para>
    /// <b>Insurance (roadmap §6.6):</b> every token / awaitable produced here is <b>linked with
    /// <see cref="ApplicationAlive"/></b> — regardless of any caller-supplied <see cref="CancellationToken"/>,
    /// so they are guaranteed to cancel on application quit / play-mode exit / assembly reload and are
    /// reclaimed in <see cref="ResetStatics"/>. Nothing is ever left waiting.
    /// </para>
    /// <para>
    /// <b>Thread affinity:</b> predicates (<see cref="WaitUntil"/> / <see cref="WaitWhile"/>) and timers
    /// (<see cref="WaitForSeconds"/>) are polled once per frame on the Unity main thread via the frame pump
    /// — never on a background thread. Do not use these from off-thread code.
    /// </para>
    /// </summary>
    public static partial class LifecycleToken
    {
        // ── Frame-scoped tokens ─────────────────────────────────────────────

        /// <summary>
        /// Creates a token that auto-cancels at the tail of the next frame pump (i.e. the current frame's
        /// work window). Linked with <see cref="ApplicationAlive"/>.
        /// </summary>
        public static LinkedTokenSource CreateCurrentFrameToken(params CancellationToken[] others)
            => CreateFrameToken(1, others);

        /// <summary>
        /// Creates a token that auto-cancels after <paramref name="frames"/> frames (min 1), timed on the
        /// main-thread frame pump. Linked with <see cref="ApplicationAlive"/>.
        /// </summary>
        public static LinkedTokenSource CreateFrameToken(int frames, params CancellationToken[] others)
        {
            var source = CreateLinked(others);
            // Cancel the source after N frames. The scheduling handle is bound to source.Token, so if the
            // caller cancels / disposes the source early, the handle self-cancels on the next pump.
            // owned:null — the caller owns the LinkedTokenSource lifetime (or ResetStatics reclaims it).
            FrameScheduler.Register(
                FrameHandle.AfterFrames(source.Cancel, Math.Max(1, frames),
                    LifecycleFrame.DefaultPoint, source.Token, owned: null));
            return source;
        }

        // ── Awaitable waits ─────────────────────────────────────────────────

        /// <summary>Awaits until the next frame. Resumes on the Unity main thread.</summary>
        public static IFrameHandle WaitForNextFrame(CancellationToken cancellationToken = default)
            => WaitForFrames(1, LifecycleFrame.DefaultPoint, cancellationToken);

        /// <summary>Awaits <paramref name="frames"/> frames (min 1). Resumes on the Unity main thread.</summary>
        public static IFrameHandle WaitForFrames(int frames, CancellationToken cancellationToken = default)
            => WaitForFrames(frames, LifecycleFrame.DefaultPoint, cancellationToken);

        /// <summary>Awaits <paramref name="frames"/> frames at the given <paramref name="point"/>.</summary>
        public static IFrameHandle WaitForFrames(int frames, PlayerLoopPoint point,
            CancellationToken cancellationToken = default)
        {
            var linked = CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.AfterFrames(null, Math.Max(1, frames), point, linked.Token, linked));
        }

        /// <summary>
        /// Awaits until <paramref name="predicate"/> becomes <c>true</c>. The predicate is polled once per
        /// frame on the main thread. Resumes on the Unity main thread.
        /// </summary>
        public static IFrameHandle WaitUntil(Func<bool> predicate, CancellationToken cancellationToken = default)
            => WaitUntil(predicate, LifecycleFrame.DefaultPoint, cancellationToken);

        /// <inheritdoc cref="WaitUntil(System.Func{bool},System.Threading.CancellationToken)"/>
        public static IFrameHandle WaitUntil(Func<bool> predicate, PlayerLoopPoint point,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var linked = CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.When(predicate, null, point, linked.Token, linked));
        }

        /// <summary>
        /// Awaits while <paramref name="predicate"/> stays <c>true</c> (resumes once it becomes
        /// <c>false</c>). Polled once per frame on the main thread. Resumes on the Unity main thread.
        /// </summary>
        public static IFrameHandle WaitWhile(Func<bool> predicate, CancellationToken cancellationToken = default)
            => WaitWhile(predicate, LifecycleFrame.DefaultPoint, cancellationToken);

        /// <inheritdoc cref="WaitWhile(System.Func{bool},System.Threading.CancellationToken)"/>
        public static IFrameHandle WaitWhile(Func<bool> predicate, PlayerLoopPoint point,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var linked = CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.When(() => !predicate(), null, point, linked.Token, linked));
        }

        /// <summary>
        /// Awaits <paramref name="seconds"/>, timed on the main-thread frame pump. Defaults to scaled time;
        /// pass <paramref name="unscaled"/> = <c>true</c> for unscaled time. Resumes on the Unity main thread.
        /// </summary>
        public static IFrameHandle WaitForSeconds(float seconds, bool unscaled = false,
            CancellationToken cancellationToken = default)
            => WaitForSeconds(seconds, LifecycleFrame.DefaultPoint, unscaled, cancellationToken);

        /// <inheritdoc cref="WaitForSeconds(float,bool,System.Threading.CancellationToken)"/>
        public static IFrameHandle WaitForSeconds(float seconds, PlayerLoopPoint point,
            bool unscaled = false, CancellationToken cancellationToken = default)
        {
            var linked = CreateLinked(cancellationToken);
            return FrameScheduler.Register(
                FrameHandle.Delayed(null, seconds, unscaled, point, linked.Token, linked));
        }
    }
}

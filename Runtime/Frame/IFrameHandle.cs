using System;
using System.Runtime.CompilerServices;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// A cancellable, awaitable handle to a piece of frame-scheduled work created through
    /// <see cref="LifecycleFrame"/> or the wait helpers on <see cref="LifecycleToken"/>.
    /// <para>
    /// Two usage styles:
    /// <list type="bullet">
    /// <item><description><b><c>using</c></b> — <c>Dispose()</c> cancels the pending work.</description></item>
    /// <item><description><b><c>await</c></b> — resumes on the main thread once the work has run
    /// (or throws <see cref="OperationCanceledException"/> if it was cancelled / the app shut down).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Insurance:</b> every handle is owned by the internal frame scheduler and is force-cancelled
    /// when the scheduler is reset (application quit, play-mode exit, assembly reload), so no awaiter
    /// is ever left dangling — matching the "linked with ApplicationAlive" guarantee.
    /// </para>
    /// </summary>
    public interface IFrameHandle : IDisposable
    {
        /// <summary>True once the scheduled work has finished successfully.</summary>
        bool IsCompleted { get; }

        /// <summary>True if the work was cancelled (via <see cref="IDisposable.Dispose"/>, a cancellation token, or shutdown).</summary>
        bool IsCancelled { get; }

        /// <summary>Enables <c>await handle;</c>.</summary>
        FrameHandleAwaiter GetAwaiter();
    }

    /// <summary>
    /// Awaiter for <see cref="IFrameHandle"/>. Continuations are invoked on the main thread inside the
    /// player-loop pump, so awaited code always resumes on the Unity thread.
    /// </summary>
    public readonly struct FrameHandleAwaiter : INotifyCompletion
    {
        private readonly FrameHandle _handle;

        internal FrameHandleAwaiter(FrameHandle handle) => _handle = handle;

        public bool IsCompleted => _handle == null || _handle.IsCompleted || _handle.IsCancelled;

        public void OnCompleted(Action continuation) => _handle?.RegisterContinuation(continuation);

        public void GetResult()
        {
            if (_handle != null && _handle.IsCancelled)
                throw new OperationCanceledException();
        }
    }
}

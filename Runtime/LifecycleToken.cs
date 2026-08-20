using System;
using System.Collections.Generic;
using System.Threading;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// The global cancellation token hub.
    /// <para><b>ApplicationAlive</b>: cancelled only after the whole quit pipeline has finished.
    /// Use this for the shutdown process itself (waiting for blockers, unloading scenes, writing files, …).</para>
    /// <para><b>Quitting</b>: cancelled the moment the user requests a quit.
    /// Use this for things that should stop immediately, such as game loops, polling and animations.</para>
    /// </summary>
    public static partial class LifecycleToken
    {
        private static CancellationTokenSource _aliveCts;
        private static CancellationTokenSource _quittingCts;
        private static CancellationToken _alive;
        private static CancellationToken _quitting;

        private static readonly HashSet<LinkedTokenSource> linked = new();
        private static readonly object @lock = new();

        static LifecycleToken() => Prepare();

        // ── Main tokens ─────────────────────────────────────────────────────

        public static CancellationToken ApplicationAlive => _alive;
        public static CancellationToken Quitting => _quitting;

        public static bool IsAlive => !_alive.IsCancellationRequested;
        public static bool IsQuitting => _quitting.IsCancellationRequested;

        /// <summary>Does nothing while alive; throws OperationCanceledException once shut down.</summary>
        public static void ThrowIfDead() => _alive.ThrowIfCancellationRequested();

        // ── Linked sources ──────────────────────────────────────────────────

        /// <summary>
        /// Gets a safe source already linked to <see cref="ApplicationAlive"/>.
        /// Always use / Dispose it, otherwise registrations pile up on the main CTS.
        /// </summary>
        public static LinkedTokenSource CreateLinked(params CancellationToken[] others)
            => Create(_alive, null, others);

        /// <summary>Same as above, plus a timeout.</summary>
        public static LinkedTokenSource CreateLinked(TimeSpan timeout, params CancellationToken[] others)
            => Create(_alive, timeout, others);

        public static LinkedTokenSource CreateLinked(int timeoutMilliseconds, params CancellationToken[] others)
            => Create(_alive, TimeSpan.FromMilliseconds(timeoutMilliseconds), others);

        /// <summary>Linked to <see cref="Quitting"/>: cancelled immediately once a quit is requested.</summary>
        public static LinkedTokenSource CreateQuitLinked(params CancellationToken[] others)
            => Create(_quitting, null, others);

        public static LinkedTokenSource CreateQuitLinked(TimeSpan timeout, params CancellationToken[] others)
            => Create(_quitting, timeout, others);

        private static LinkedTokenSource Create(CancellationToken root, TimeSpan? timeout, CancellationToken[] others)
        {
            CancellationTokenSource cts;

            if (others == null || others.Length == 0)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(root);
            }
            else
            {
                var all = new CancellationToken[others.Length + 1];
                all[0] = root;
                Array.Copy(others, 0, all, 1, others.Length);
                cts = CancellationTokenSource.CreateLinkedTokenSource(all);
            }

            if (timeout.HasValue) cts.CancelAfter(timeout.Value);

            var linkedTokenSource = new LinkedTokenSource(cts);
            lock (@lock) LifecycleToken.linked.Add(linkedTokenSource);
            return linkedTokenSource;
        }

        internal static void Unregister(LinkedTokenSource src)
        {
            lock (@lock) linked.Remove(src);
        }

        // // ── Utilities ───────────────────────────────────────────────────────
        //
        // /// <summary>Shorthand for Task.Delay(ms, ApplicationAlive).</summary>
        // public static Task Delay(int milliseconds)
        //     => Task.Delay(milliseconds, s_Alive);
        //
        // public static Task Delay(TimeSpan duration)
        //     => Task.Delay(duration, s_Alive);
        //
        // /// <summary>A non-throwing delay: returns quietly on shutdown.</summary>
        // public static async Task DelaySafe(int milliseconds)
        // {
        //     try { await Task.Delay(milliseconds, s_Alive); }
        //     catch (OperationCanceledException) { }
        // }
        //
        // /// <summary>Polls until the condition becomes true. Returns false on timeout or application shutdown.</summary>
        // public static async Task<bool> WaitUntil(Func<bool> predicate,
        //                                          int pollMilliseconds = 50,
        //                                          TimeSpan? timeout = null)
        // {
        //     var deadline = timeout.HasValue ? DateTime.UtcNow + timeout.Value : DateTime.MaxValue;
        //     while (!predicate())
        //     {
        //         if (s_Alive.IsCancellationRequested) return false;
        //         if (DateTime.UtcNow >= deadline) return false;
        //         try { await Task.Delay(pollMilliseconds, s_Alive); }
        //         catch (OperationCanceledException) { return false; }
        //     }
        //     return true;
        // }

        // ── Lifecycle (internal calls only) ─────────────────────────────────

        internal static void Prepare()
        {
            _aliveCts = new CancellationTokenSource();
            _quittingCts = new CancellationTokenSource();
            _alive = _aliveCts.Token;
            _quitting = _quittingCts.Token;
        }

        internal static void SignalQuitting()
        {
            try { _quittingCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        internal static void SignalDead()
        {
            try { _aliveCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>The reset point when Domain Reload is disabled. Forcibly reclaims every linked source that was not disposed.</summary>
        internal static void ResetStatics()
        {
            LinkedTokenSource[] leaked;
            lock (@lock)
            {
                leaked = new LinkedTokenSource[linked.Count];
                linked.CopyTo(leaked);
                linked.Clear();
            }

            if (leaked.Length > 0)
                LifecycleLog.Warning($"{leaked.Length} LinkedTokenSource were not disposed. " +
                                     "Wrap them in 'using' to avoid leaks when Domain Reload is off.");

            foreach (var l in leaked) l.DisposeInternal();

            SignalQuitting();
            SignalDead();

            _aliveCts?.Dispose();
            _quittingCts?.Dispose();

            Prepare();
        }
    }

    /// <summary>
    /// A CancellationTokenSource wrapper linked to the application lifecycle.
    /// It converts implicitly to a CancellationToken: <c>await Task.Delay(50, cts);</c>
    /// </summary>
    public sealed class LinkedTokenSource : IDisposable
    {
        private CancellationTokenSource _cts;
        private readonly CancellationToken _token;
        private bool _disposed;

        internal LinkedTokenSource(CancellationTokenSource cts)
        {
            _cts = cts;
            _token = cts.Token;      // Cached up front so it can still be read safely after Dispose
        }

        public CancellationToken Token => _token;
        public bool IsCancellationRequested => _token.IsCancellationRequested;

        public void Cancel()
        {
            if (_disposed) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void CancelAfter(TimeSpan delay)
        {
            if (_disposed) return;
            try { _cts.CancelAfter(delay); } catch (ObjectDisposedException) { }
        }

        public void CancelAfter(int milliseconds) => CancelAfter(TimeSpan.FromMilliseconds(milliseconds));

        public void Dispose()
        {
            if (_disposed) return;
            LifecycleToken.Unregister(this);
            DisposeInternal();
        }

        internal void DisposeInternal()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _cts?.Dispose();
            }
            catch
            {
                // ignored
            }

            _cts = null;
        }

        public static implicit operator CancellationToken(LinkedTokenSource source)
            => source?._token ?? CancellationToken.None;
    }
}
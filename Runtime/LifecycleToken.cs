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
    public static class LifecycleToken
    {
        static CancellationTokenSource s_AliveCts;
        static CancellationTokenSource s_QuittingCts;
        static CancellationToken s_Alive;
        static CancellationToken s_Quitting;

        static readonly HashSet<LinkedTokenSource> s_Linked = new HashSet<LinkedTokenSource>();
        static readonly object s_Lock = new object();

        static LifecycleToken() => Prepare();

        // ── Main tokens ─────────────────────────────────────────────────────

        public static CancellationToken ApplicationAlive => s_Alive;
        public static CancellationToken Quitting => s_Quitting;

        public static bool IsAlive => !s_Alive.IsCancellationRequested;
        public static bool IsQuitting => s_Quitting.IsCancellationRequested;

        /// <summary>Does nothing while alive; throws OperationCanceledException once shut down.</summary>
        public static void ThrowIfDead() => s_Alive.ThrowIfCancellationRequested();

        // ── Linked sources ──────────────────────────────────────────────────

        /// <summary>
        /// Gets a safe source already linked to <see cref="ApplicationAlive"/>.
        /// Always use / Dispose it, otherwise registrations pile up on the main CTS.
        /// </summary>
        public static LinkedTokenSource CreateLinked(params CancellationToken[] others)
            => Create(s_Alive, null, others);

        /// <summary>Same as above, plus a timeout.</summary>
        public static LinkedTokenSource CreateLinked(TimeSpan timeout, params CancellationToken[] others)
            => Create(s_Alive, timeout, others);

        public static LinkedTokenSource CreateLinked(int timeoutMilliseconds, params CancellationToken[] others)
            => Create(s_Alive, TimeSpan.FromMilliseconds(timeoutMilliseconds), others);

        /// <summary>Linked to <see cref="Quitting"/>: cancelled immediately once a quit is requested.</summary>
        public static LinkedTokenSource CreateQuitLinked(params CancellationToken[] others)
            => Create(s_Quitting, null, others);

        public static LinkedTokenSource CreateQuitLinked(TimeSpan timeout, params CancellationToken[] others)
            => Create(s_Quitting, timeout, others);

        static LinkedTokenSource Create(CancellationToken root, TimeSpan? timeout, CancellationToken[] others)
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

            var linked = new LinkedTokenSource(cts);
            lock (s_Lock) s_Linked.Add(linked);
            return linked;
        }

        internal static void Unregister(LinkedTokenSource src)
        {
            lock (s_Lock) s_Linked.Remove(src);
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
            s_AliveCts = new CancellationTokenSource();
            s_QuittingCts = new CancellationTokenSource();
            s_Alive = s_AliveCts.Token;
            s_Quitting = s_QuittingCts.Token;
        }

        internal static void SignalQuitting()
        {
            try { s_QuittingCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        internal static void SignalDead()
        {
            try { s_AliveCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>The reset point when Domain Reload is disabled. Forcibly reclaims every linked source that was not disposed.</summary>
        internal static void ResetStatics()
        {
            LinkedTokenSource[] leaked;
            lock (s_Lock)
            {
                leaked = new LinkedTokenSource[s_Linked.Count];
                s_Linked.CopyTo(leaked);
                s_Linked.Clear();
            }

            if (leaked.Length > 0)
                LifecycleLog.Warning($"{leaked.Length} LinkedTokenSource were not disposed. " +
                                     "Wrap them in 'using' to avoid leaks when Domain Reload is off.");

            foreach (var l in leaked) l.DisposeInternal();

            SignalQuitting();
            SignalDead();

            s_AliveCts?.Dispose();
            s_QuittingCts?.Dispose();

            Prepare();
        }
    }

    /// <summary>
    /// A CancellationTokenSource wrapper linked to the application lifecycle.
    /// It converts implicitly to a CancellationToken: <c>await Task.Delay(50, cts);</c>
    /// </summary>
    public sealed class LinkedTokenSource : IDisposable
    {
        CancellationTokenSource _cts;
        readonly CancellationToken _token;
        bool _disposed;

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
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        public static implicit operator CancellationToken(LinkedTokenSource source)
            => source?._token ?? default;
    }
}
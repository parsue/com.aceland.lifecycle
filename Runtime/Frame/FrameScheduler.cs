using System;
using System.Collections.Generic;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Internal main-thread scheduler that drives every <see cref="FrameHandle"/>.
    /// <para>
    /// Subscribes to <see cref="LifecyclePlayerLoop.Tick"/> (installing the pump on demand) and, once per
    /// frame at each <see cref="PlayerLoopPoint"/>, advances the handles registered for that point.
    /// Registration is thread-safe: new handles land in a pending list and are moved into their point's
    /// bucket <b>after</b> that point has been processed, guaranteeing they are never advanced in the same
    /// tick they were registered in (true "next frame" semantics regardless of the calling context).
    /// </para>
    /// <para>
    /// <b>CoreCLR premise / insurance:</b> nothing relies on domain reload. <see cref="ResetStatics"/> and
    /// <see cref="DrainAll"/> unsubscribe from the pump and force-cancel every live handle (draining awaiters
    /// with <see cref="OperationCanceledException"/>) on quit, play-mode exit and assembly reload.
    /// </para>
    /// </summary>
    internal static class FrameScheduler
    {
        private const int PointCount = 8; // PlayerLoopPoint TimeUpdate..PostLateUpdate

        private static readonly List<FrameHandle>[] _buckets = new List<FrameHandle>[PointCount];
        private static readonly List<FrameHandle> _pending = new();
        private static readonly object _lock = new();
        private static bool _subscribed;

        static FrameScheduler()
        {
            for (var i = 0; i < PointCount; i++)
                _buckets[i] = new List<FrameHandle>();
        }

        // ── Registration ────────────────────────────────────────────────────

        /// <summary>Registers a handle and ensures the frame pump is running. Thread-safe.</summary>
        internal static FrameHandle Register(FrameHandle handle)
        {
            if (handle == null) return null;

            // A handle that was born already cancelled (e.g. token pre-cancelled) still needs its
            // continuation drained; queue it so the next pump resolves it on the main thread.
            EnsureSubscribed();
            lock (_lock) _pending.Add(handle);
            return handle;
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            LifecyclePlayerLoop.EnsureInstalled();
            LifecyclePlayerLoop.Tick += OnTick;
        }

        // ── Per-frame pump (main thread) ────────────────────────────────────

        private static void OnTick(PlayerLoopPoint point)
        {
            var bucket = _buckets[(int)point];

            // Advance existing handles; swap-remove finished ones.
            for (var i = bucket.Count - 1; i >= 0; i--)
            {
                var handle = bucket[i];
                bool done;
                try
                {
                    done = handle.Advance();
                }
                catch (Exception e)
                {
                    LifecycleLog.Exception(e);
                    done = true;
                }

                if (!done) continue;

                var last = bucket.Count - 1;
                bucket[i] = bucket[last];
                bucket.RemoveAt(last);
            }

            // Move pending handles bound to this point into the bucket *after* processing,
            // so they first run on the next occurrence of this point.
            lock (_lock)
            {
                for (var i = _pending.Count - 1; i >= 0; i--)
                {
                    var handle = _pending[i];
                    if (handle.Point != point) continue;

                    var last = _pending.Count - 1;
                    _pending[i] = _pending[last];
                    _pending.RemoveAt(last);
                    bucket.Add(handle);
                }
            }
        }

        // ── Diagnostics (PlayerLoop window) ─────────────────────────────────

        /// <summary>
        /// Snapshots every live handle bound to <paramref name="point"/> (already-scheduled ones plus
        /// pending registrations for that point) into <paramref name="buffer"/>. Read-only diagnostic
        /// surface for the PlayerLoop editor window; safe to call from the main thread. Handles that are
        /// already done are skipped so the graph never shows ghosts.
        /// </summary>
        internal static void SnapshotPoint(PlayerLoopPoint point, List<FrameHandle> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();

            var bucket = _buckets[(int)point];
            for (var i = 0; i < bucket.Count; i++)
            {
                var handle = bucket[i];
                if (handle is { IsDone: true, State: not FrameProcessState.Error }) continue;
                buffer.Add(handle);
            }

            lock (_lock)
            {
                for (var i = 0; i < _pending.Count; i++)
                {
                    var handle = _pending[i];
                    if (handle.Point != point) continue;
                    if (handle is { IsDone: true, State: not FrameProcessState.Error }) continue;
                    buffer.Add(handle);
                }
            }
        }

        // ── Teardown (insurance) ────────────────────────────────────────────

        /// <summary>Full reset: unsubscribe the pump and force-cancel every live handle.</summary>
        internal static void ResetStatics()
        {
            if (_subscribed)
            {
                LifecyclePlayerLoop.Tick -= OnTick;
                _subscribed = false;
            }

            DrainAll();
        }

        /// <summary>
        /// Force-cancels every pending / scheduled handle without touching the pump subscription.
        /// Snapshots each collection before cancelling so continuations that re-register during
        /// cancellation cannot corrupt the iteration.
        /// </summary>
        internal static void DrainAll()
        {
            FrameHandle[] pendingSnapshot;
            lock (_lock)
            {
                if (_pending.Count > 0)
                {
                    pendingSnapshot = _pending.ToArray();
                    _pending.Clear();
                }
                else
                {
                    pendingSnapshot = Array.Empty<FrameHandle>();
                }
            }

            foreach (var handle in pendingSnapshot)
                handle.ForceCancel();

            foreach (var bucket in _buckets)
            {
                if (bucket.Count == 0) continue;
                var snapshot = bucket.ToArray();
                bucket.Clear();
                foreach (var handle in snapshot)
                    handle.ForceCancel();
            }
        }
    }
}

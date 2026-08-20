using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Self-contained PlayerLoop driver. Injects one tick node per <see cref="PlayerLoopPoint"/>
    /// into Unity's low-level player loop and raises <see cref="Tick"/> once per frame at each point.
    /// <para>
    /// <b>CoreCLR premise:</b> future Unity will not auto-reload the domain, so nothing here may rely
    /// on domain reload to clear injected delegates. Every node is removed explicitly through
    /// <see cref="EnsureRemoved"/> during the quit pipeline, on play-mode exit and before assembly
    /// reload. <see cref="EnsureRemoved"/> / <see cref="EnsureInstalled"/> are idempotent and self-heal.
    /// </para>
    /// </summary>
    internal static class LifecyclePlayerLoop
    {
        // Marker types give each injected node a stable identity for insert / remove,
        // independent of the delegate instance.
        private struct LifecycleTimeUpdate { }
        private struct LifecycleInitialization { }
        private struct LifecycleEarlyUpdate { }
        private struct LifecycleFixedUpdate { }
        private struct LifecyclePreUpdate { }
        private struct LifecycleUpdate { }
        private struct LifecyclePreLateUpdate { }
        private struct LifecyclePostLateUpdate { }

        private readonly struct Node
        {
            public readonly PlayerLoopPoint Point;
            public readonly Type Parent;   // Unity built-in loop segment to nest under
            public readonly Type Marker;   // our identity type

            public Node(PlayerLoopPoint point, Type parent, Type marker)
            {
                Point = point;
                Parent = parent;
                Marker = marker;
            }
        }

        private static readonly Node[] Nodes =
        {
            new(PlayerLoopPoint.TimeUpdate,     typeof(TimeUpdate),     typeof(LifecycleTimeUpdate)),
            new(PlayerLoopPoint.Initialization, typeof(Initialization), typeof(LifecycleInitialization)),
            new(PlayerLoopPoint.EarlyUpdate,    typeof(EarlyUpdate),    typeof(LifecycleEarlyUpdate)),
            new(PlayerLoopPoint.FixedUpdate,    typeof(FixedUpdate),    typeof(LifecycleFixedUpdate)),
            new(PlayerLoopPoint.PreUpdate,      typeof(PreUpdate),      typeof(LifecyclePreUpdate)),
            new(PlayerLoopPoint.Update,         typeof(Update),         typeof(LifecycleUpdate)),
            new(PlayerLoopPoint.PreLateUpdate,  typeof(PreLateUpdate),  typeof(LifecyclePreLateUpdate)),
            new(PlayerLoopPoint.PostLateUpdate, typeof(PostLateUpdate), typeof(LifecyclePostLateUpdate)),
        };

        /// <summary>Raised once per frame at the given point while installed.</summary>
        internal static event Action<PlayerLoopPoint> Tick;

        private static bool _installed;

        internal static bool IsInstalled => _installed;

        /// <summary>Read-only snapshot of a single Lifecycle-injected player-loop node.</summary>
        internal readonly struct InstalledNode
        {
            public readonly PlayerLoopPoint Point;
            public readonly string ParentSegment; // Unity built-in loop segment it nests under
            public readonly bool Installed;        // present in the live player loop right now

            public InstalledNode(PlayerLoopPoint point, string parentSegment, bool installed)
            {
                Point = point;
                ParentSegment = parentSegment;
                Installed = installed;
            }
        }

        /// <summary>
        /// Enumerates every Lifecycle-injected node with its live install status, in point order.
        /// Only lists nodes owned by Lifecycle — Unity built-in / third-party loop systems are ignored.
        /// Read-only diagnostic surface for the PlayerLoop editor window.
        /// </summary>
        internal static IEnumerable<InstalledNode> EnumerateInstalled()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (var i = 0; i < Nodes.Length; i++)
            {
                var n = Nodes[i];
                yield return new InstalledNode(n.Point, n.Parent.Name, Contains(in loop, n.Marker));
            }
        }

        // ── Install / Remove ────────────────────────────────────────────────

        /// <summary>
        /// Injects the Lifecycle tick nodes if absent. Idempotent; self-heals when the flag says
        /// installed but the nodes are gone (e.g. another system rebuilt the loop).
        /// </summary>
        internal static void EnsureInstalled()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();

            if (_installed)
            {
                if (Contains(in loop, Nodes[0].Marker)) return; // already present
                _installed = false; // self-heal: nodes vanished, fall through to reinstall
            }

            for (var i = 0; i < Nodes.Length; i++)
            {
                var n = Nodes[i];
                if (Contains(in loop, n.Marker)) continue;

                var system = new PlayerLoopSystem
                {
                    type = n.Marker,
                    updateDelegate = MakeDispatch(n.Point),
                    subSystemList = null,
                };
                InsertUnder(ref loop, n.Parent, in system);
            }

            PlayerLoop.SetPlayerLoop(loop);
            _installed = true;
        }

        /// <summary>
        /// Removes every Lifecycle tick node. Idempotent — safe to call repeatedly, and only writes
        /// the loop back when something was actually removed. Always clears the installed flag.
        /// </summary>
        internal static void EnsureRemoved()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var removedAny = false;

            for (var i = 0; i < Nodes.Length; i++)
                removedAny |= RemoveByType(ref loop, Nodes[i].Marker);

            if (removedAny) PlayerLoop.SetPlayerLoop(loop);
            _installed = false;
        }

        private static PlayerLoopSystem.UpdateFunction MakeDispatch(PlayerLoopPoint point)
            => () => Tick?.Invoke(point);

        // ── Low-level loop surgery ──────────────────────────────────────────

        private static bool InsertUnder(ref PlayerLoopSystem loop, Type parent, in PlayerLoopSystem node)
        {
            if (loop.type == parent)
            {
                var list = loop.subSystemList;
                var length = list?.Length ?? 0;
                var next = new PlayerLoopSystem[length + 1];
                if (length > 0) Array.Copy(list, next, length);
                next[length] = node;
                loop.subSystemList = next;
                return true;
            }

            if (loop.subSystemList == null) return false;

            for (var i = 0; i < loop.subSystemList.Length; i++)
                if (InsertUnder(ref loop.subSystemList[i], parent, in node))
                    return true;

            return false;
        }

        private static bool RemoveByType(ref PlayerLoopSystem loop, Type marker)
        {
            var removed = false;

            if (loop.subSystemList != null)
            {
                var kept = new System.Collections.Generic.List<PlayerLoopSystem>(loop.subSystemList.Length);
                for (var i = 0; i < loop.subSystemList.Length; i++)
                {
                    if (loop.subSystemList[i].type == marker)
                    {
                        removed = true;
                        continue;
                    }
                    kept.Add(loop.subSystemList[i]);
                }

                if (removed) loop.subSystemList = kept.ToArray();

                for (var i = 0; i < loop.subSystemList.Length; i++)
                    removed |= RemoveByType(ref loop.subSystemList[i], marker);
            }

            return removed;
        }

        private static bool Contains(in PlayerLoopSystem loop, Type marker)
        {
            if (loop.subSystemList == null) return false;

            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == marker) return true;
                if (Contains(in loop.subSystemList[i], marker)) return true;
            }

            return false;
        }

        // ── Editor safety net ───────────────────────────────────────────────
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void HookEditor()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            AssemblyReloadEvents.beforeAssemblyReload -= EnsureRemoved;
            AssemblyReloadEvents.beforeAssemblyReload += EnsureRemoved;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Remove before the domain (may) reload and before scene objects are torn down,
            // so play-in / play-out cycles never accumulate stale update delegates.
            if (state is PlayModeStateChange.ExitingPlayMode)
            {
                // While the quit pipeline will intercept this exit it owns pump removal: blockers
                // and quit handlers may still rely on the frame pump to advance their per-frame
                // work. The pipeline intercepts ExitingPlayMode (pulls Play Mode back) and strips
                // the nodes at its tail — after ShutdownAll(). This safety net fires on the SAME
                // event dispatch and, because it subscribed first ([InitializeOnLoadMethod] runs
                // before the pipeline's Install()), it runs BEFORE RunAsync() sets Phase. So we
                // must not gate on IsActive (still false at this instant); we gate on
                // WillInterceptQuit (installed && !IsReadyToQuit), which is already true here.
                // The EnteredEditMode branch below remains the final backstop once the pipeline
                // has finished and let Play Mode go.
                if (ApplicationQuitPipeline.WillInterceptQuit) return;
                EnsureRemoved();
                return;
            }

            if (state is PlayModeStateChange.EnteredEditMode)
                EnsureRemoved();
        }
#endif
    }
}

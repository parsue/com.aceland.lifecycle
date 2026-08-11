using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// The module registry. Attributes only feed modules in here; the real execution order is decided by the topological sort in this class.
    /// </summary>
    public static class ModuleRegistry
    {
        static readonly List<ModuleEntry> s_Entries = new List<ModuleEntry>();
        static readonly Dictionary<Type, ModuleEntry> s_ById = new Dictionary<Type, ModuleEntry>();
        static readonly List<ModuleEntry> s_Started = new List<ModuleEntry>();
        static readonly List<string> s_Issues = new List<string>();
        static readonly Dictionary<Type, List<Action<IModule>>> s_ReadyCallbacks
            = new Dictionary<Type, List<Action<IModule>>>();
        internal static IReadOnlyList<ModuleEntry> StartedEntries => s_Started;

        static Task s_Chain = Task.CompletedTask;
        static bool s_Scanned;
        static bool s_ShuttingDown;

        /// <summary>
        /// State change notification. <b>Never replayed</b> — subscribing in Start() misses every event from the Core / Runtime phases.
        /// To obtain a module use <see cref="WhenReady{T}"/>; to observe everything use <see cref="ObserveStates"/>.
        /// </summary>
        public static event Action<ModuleEntry> ModuleStateChanged;

        /// <summary>A Task that completes when the entire initialization chain (including async modules) is done.</summary>
        public static Task Ready => s_Chain;

        public static IReadOnlyList<ModuleEntry> Entries => s_Entries;
        public static IReadOnlyList<string> Issues => s_Issues;

        // ── Registration ────────────────────────────────────────────────────

        /// <summary>Registers using the module's own type as the Id. Unspecified parameters are read from <see cref="LifecycleModuleAttribute"/>.</summary>
        public static void Register(IModule module,
                                    ModulePhase? phase = null,
                                    Type[] dependsOn = null,
                                    int? order = null)
            => Register(module?.GetType(), module, phase, dependsOn, order, autoRegistered: false);

        /// <summary>Registers using <typeparamref name="TId"/> (usually the public-facing interface) as the Id.</summary>
        public static void Register<TId>(IModule module,
                                         ModulePhase? phase = null,
                                         Type[] dependsOn = null,
                                         int? order = null)
            => Register(typeof(TId), module, phase, dependsOn, order, autoRegistered: false);

        internal static void Register(Type id, IModule module, ModulePhase? phase,
                                      Type[] dependsOn, int? order, bool autoRegistered)
        {
            if (module == null) { LifecycleLog.Error("Register(null) ignored."); return; }
            if (id == null) id = module.GetType();

            var attr = (LifecycleModuleAttribute)Attribute.GetCustomAttribute(
                module.GetType(), typeof(LifecycleModuleAttribute));

            if (attr?.Id != null && id == module.GetType()) id = attr.Id;

            if (phase == null)
            {
                if (attr == null)
                {
                    LifecycleLog.Error($"{module.GetType().Name}: phase not specified and no [LifecycleModule].");
                    return;
                }
                phase = attr.Phase;
            }

            var deps = dependsOn ?? attr?.DependsOn ?? Type.EmptyTypes;
            var ord = order ?? attr?.Order ?? 0;

            if (s_ById.TryGetValue(id, out var existing))
            {
                // A manual registration may override the auto-scanned result; anything else counts as a duplicate.
                if (existing.AutoRegistered && !autoRegistered)
                {
                    s_Entries.Remove(existing);
                    s_ById.Remove(id);
                }
                else
                {
                    if (!autoRegistered)
                        LifecycleLog.Warning($"Duplicate registration for '{id.Name}' ignored.");
                    return;
                }
            }

            var entry = new ModuleEntry
            {
                Id = id,
                Module = module,
                Phase = phase.Value,
                Order = ord,
                DependsOn = deps,
                IsAsync = module is IAsyncModule,
                AutoRegistered = autoRegistered,
                State = ModuleState.Registered,
            };

            s_Entries.Add(entry);
            s_ById[id] = entry;
            ModuleStateChanged?.Invoke(entry);
        }

        // ── Queries ─────────────────────────────────────────────────────────

        public static bool IsReady<T>() => IsReady(typeof(T));

        public static bool IsReady(Type id)
            => id != null && s_ById.TryGetValue(id, out var e) && e.State == ModuleState.Ready;

        public static bool TryGet<T>(out T module) where T : class
        {
            if (s_ById.TryGetValue(typeof(T), out var e) && e.State == ModuleState.Ready)
            {
                module = e.Module as T;
                return module != null;
            }
            module = null;
            return false;
        }

        /// <summary>Gets a module, throwing if it is not ready. Use this where the caller guarantees the dependency has been declared.</summary>
        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out var m)) return m;
            throw new InvalidOperationException(
                $"[Lifecycle] Module '{typeof(T).Name}' is not ready. " +
                "Did you declare it in DependsOn?");
        }
        
        // ── Ready callbacks ─────────────────────────────────────────────────

        /// <summary>
        /// Invokes the callback <b>immediately and synchronously</b> if the module is already Ready; otherwise queues it until then.
        /// <para>This is the correct way to obtain a module from a MonoBehaviour — you don't have to worry about subscription timing.</para>
        /// </summary>
        /// <returns>Used to cancel the wait. Returns an empty disposable when it has already run.</returns>
        public static IDisposable WhenReady<T>(Action<T> callback) where T : class
        {
            if (callback == null) return Disposable.Empty;

            var id = typeof(T);

            if (s_ById.TryGetValue(id, out var existing))
            {
                switch (existing.State)
                {
                    case ModuleState.Ready:
                        SafeInvoke(callback, existing.Module as T, id);
                        return Disposable.Empty;

                    case ModuleState.Failed:
                    case ModuleState.Skipped:
                        LifecycleLog.Error(
                            $"WhenReady<{id.Name}>() will never fire: module is {existing.State}. " +
                            $"({existing.Error})");
                        return Disposable.Empty;
                }
            }

            Action<IModule> boxed = m => SafeInvoke(callback, m as T, id);

            if (!s_ReadyCallbacks.TryGetValue(id, out var list))
                s_ReadyCallbacks[id] = list = new List<Action<IModule>>();
            list.Add(boxed);

            return new Disposable(() =>
            {
                if (s_ReadyCallbacks.TryGetValue(id, out var l)) l.Remove(boxed);
            });
        }

        /// <summary>The awaitable version. Returns an already-completed Task when the module is Ready.</summary>
        public static Task<T> WhenReadyAsync<T>(CancellationToken cancellationToken = default)
            where T : class
        {
            if (TryGet<T>(out var ready)) return Task.FromResult(ready);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sub = WhenReady<T>(m => tcs.TrySetResult(m));

            if (cancellationToken.CanBeCanceled)
            {
                var reg = cancellationToken.Register(() =>
                {
                    sub.Dispose();
                    tcs.TrySetCanceled(cancellationToken);
                });
                tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Subscribes to state changes and <b>immediately replays</b> the current state of every known module.
        /// Meant for diagnostics and the Editor window; to simply obtain a module use <see cref="WhenReady{T}"/> instead.
        /// </summary>
        public static IDisposable ObserveStates(Action<ModuleEntry> observer)
        {
            if (observer == null) return Disposable.Empty;

            for (int i = 0; i < s_Entries.Count; i++)
            {
                try { observer(s_Entries[i]); }
                catch (Exception ex) { LifecycleLog.Exception(ex); }
            }

            ModuleStateChanged += observer;
            return new Disposable(() => ModuleStateChanged -= observer);
        }

        static void SafeInvoke<T>(Action<T> callback, T module, Type id) where T : class
        {
            if (module == null)
            {
                LifecycleLog.Error($"WhenReady<{id.Name}>(): registered instance is not a {id.Name}.");
                return;
            }

            try { callback(module); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
        }

        static void FlushReadyCallbacks(ModuleEntry entry)
        {
            if (!s_ReadyCallbacks.TryGetValue(entry.Id, out var list) || list.Count == 0) return;

            // Take a copy: a callback may register new callbacks while running
            var snapshot = list.ToArray();
            s_ReadyCallbacks.Remove(entry.Id);

            for (int i = 0; i < snapshot.Length; i++) snapshot[i](entry.Module);
        }

        // ── Execution ───────────────────────────────────────────────────────

        internal static void RunPhase(ModulePhase phase)
        {
            s_Chain = RunPhaseChained(s_Chain, phase);
        }

        static async Task RunPhaseChained(Task previous, ModulePhase phase)
        {
            // When previous is already completed, the await continues synchronously → fully synchronous projects behave exactly as they do today.
            try { await previous; }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
            await RunPhaseInternal(phase);
        }

        static async Task RunPhaseInternal(ModulePhase phase)
        {
            EnsureScanned();

            var batch = new List<ModuleEntry>();
            for (int i = 0; i < s_Entries.Count; i++)
                if (s_Entries[i].Phase == phase && s_Entries[i].State == ModuleState.Registered)
                    batch.Add(s_Entries[i]);

            if (batch.Count == 0) return;

            var sorted = ModuleSorter.Sort(batch, s_ById, s_Issues);
            LifecycleLog.DumpOrder(phase, sorted);

            var token = GetAppToken();

            for (int i = 0; i < sorted.Count; i++)
            {
                var e = sorted[i];
                e.SortIndex = i;

                var missing = FirstUnmetDependency(e);
                if (missing != null)
                {
                    e.State = ModuleState.Skipped;
                    e.Error = $"dependency '{missing.Name}' is not ready";
                    LifecycleLog.Error($"Skip {e.DisplayName}: {e.Error}.");
                    ModuleStateChanged?.Invoke(e);
                    continue;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    e.State = ModuleState.Initializing;
                    ModuleStateChanged?.Invoke(e);

                    e.Module.Initialize();

                    if (e.Module is IAsyncModule async)
                        await async.InitializeAsync(token);

                    e.State = ModuleState.Ready;
                    s_Started.Add(e);
                    FlushReadyCallbacks(e);
                }
                catch (OperationCanceledException)
                {
                    e.State = ModuleState.Skipped;
                    e.Error = "cancelled";
                    return;
                }
                catch (Exception ex)
                {
                    e.State = ModuleState.Failed;
                    e.Error = ex.Message;
                    LifecycleLog.Exception(
                        new Exception($"[Lifecycle] '{e.DisplayName}' initialization failed.", ex));
                }
                finally
                {
                    sw.Stop();
                    e.InitMilliseconds = sw.Elapsed.TotalMilliseconds;
                    ModuleStateChanged?.Invoke(e);
                }
            }
        }

        static Type FirstUnmetDependency(ModuleEntry e)
        {
            for (int i = 0; i < e.DependsOn.Length; i++)
            {
                var d = e.DependsOn[i];
                if (d == null) continue;
                if (!s_ById.TryGetValue(d, out var dep) || dep.State != ModuleState.Ready)
                    return d;
            }
            return null;
        }

        static CancellationToken GetAppToken() => LifecycleToken.ApplicationAlive;

        static void EnsureScanned()
        {
            if (s_Scanned) return;
            s_Scanned = true;
            ModuleAutoScanner.ScanAndRegister();
        }

        // ── Shutdown ────────────────────────────────────────────────────────

        /// <summary>Shuts down all modules in the reverse order of initialization. Re-entrant.</summary>
        internal static void ShutdownAll()
        {
            if (s_ShuttingDown) return;
            s_ShuttingDown = true;

            for (int i = s_Started.Count - 1; i >= 0; i--)
            {
                var e = s_Started[i];
                try
                {
                    e.Module.Shutdown();
                    e.State = ModuleState.ShutDown;
                }
                catch (Exception ex)
                {
                    e.State = ModuleState.Failed;
                    e.Error = ex.Message;
                    LifecycleLog.Exception(ex);
                }
                ModuleStateChanged?.Invoke(e);
            }

            s_Started.Clear();
            LifecycleHost.DestroyRoot();
            s_ShuttingDown = false;
        }

        /// <summary>
        /// The reset point when Domain Reload is disabled. Called by LifecycleDriver before entering Play Mode.
        /// </summary>
        internal static void ResetStatics()
        {
            ShutdownAll();
            s_Entries.Clear();
            s_ById.Clear();
            s_Started.Clear();
            s_Issues.Clear();
            s_Chain = Task.CompletedTask;
            s_Scanned = false;
            s_ReadyCallbacks.Clear();
            s_ShuttingDown = false;
            ModuleStateChanged = null;
        }
    }
}
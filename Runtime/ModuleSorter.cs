using System;
using System.Collections.Generic;

namespace AceLand.Lifecycle
{
    internal static class ModuleSorter
    {
        /// <summary>
        /// Topologically sorts the modules of a single phase. The input is first normalized by (Order, FullName) to keep the result deterministic.
        /// A detected cycle is recorded as an issue and the offending node is pushed to the end.
        /// </summary>
        public static List<ModuleEntry> Sort(List<ModuleEntry> batch,
                                             IReadOnlyDictionary<Type, ModuleEntry> all,
                                             List<string> issues)
        {
            batch.Sort(CompareStable);

            var local = new Dictionary<Type, ModuleEntry>(batch.Count);
            foreach (var e in batch) local[e.Id] = e;

            var color = new Dictionary<Type, int>(batch.Count); // 0=white 1=gray 2=black
            var result = new List<ModuleEntry>(batch.Count);
            var stack = new List<Type>();

            foreach (var e in batch) Visit(e);
            return result;

            void Visit(ModuleEntry e)
            {
                color.TryGetValue(e.Id, out var c);
                if (c == 2) return;
                if (c == 1)
                {
                    var path = string.Join(" -> ", stack.ConvertAll(t => t.Name));
                    var msg = $"Circular dependency: {path} -> {e.Id.Name}";
                    if (!issues.Contains(msg)) issues.Add(msg);
                    LifecycleLog.Error(msg);
                    return;
                }

                color[e.Id] = 1;
                stack.Add(e.Id);

                var deps = new List<Type>(e.DependsOn);
                deps.Sort((a, b) => string.CompareOrdinal(a?.FullName, b?.FullName));

                foreach (var d in deps)
                {
                    if (d == null) continue;

                    if (local.TryGetValue(d, out var sameBatch))
                    {
                        Visit(sameBatch);
                        continue;
                    }

                    if (!all.TryGetValue(d, out var other))
                    {
                        var msg = $"'{e.Id.Name}' depends on '{d.Name}' which is not registered.";
                        if (!issues.Contains(msg)) issues.Add(msg);
                        continue;
                    }

                    if (other.Phase > e.Phase)
                    {
                        var msg = $"'{e.Id.Name}' ({e.Phase}) depends on '{d.Name}' " +
                                  $"in a later phase ({other.Phase}).";
                        if (!issues.Contains(msg)) issues.Add(msg);
                    }

                    if (other.IsAsync && !e.IsAsync)
                    {
                        var msg = $"'{e.Id.Name}' is synchronous but depends on async module " +
                                  $"'{d.Name}'. Make it an IAsyncModule.";
                        if (!issues.Contains(msg)) issues.Add(msg);
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                color[e.Id] = 2;
                result.Add(e);
            }
        }

        private static int CompareStable(ModuleEntry a, ModuleEntry b)
        {
            var c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : string.CompareOrdinal(a.Id?.FullName, b.Id?.FullName);
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEditor;

namespace AceLand.Lifecycle.Editor
{
    internal static class ModuleGraphModel
    {
        /// <summary>
        /// edit mode: scan static by TypeCache [LifecycleModule]
        /// play mode: on active ModuleRegistry + static nodes
        /// </summary>
        public static GraphData Build(bool preferLive)
        {
            var data = new GraphData();
            var byId = new Dictionary<Type, GraphNode>();

            // ── 1. declare static ──
            foreach (var t in TypeCache.GetTypesWithAttribute<LifecycleModuleAttribute>())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IModule).IsAssignableFrom(t)) continue;

                var attr = (LifecycleModuleAttribute)Attribute.GetCustomAttribute(
                    t, typeof(LifecycleModuleAttribute));
                if (attr == null) continue;

                var id = attr.Id ?? t;
                if (byId.ContainsKey(id)) continue;
                
                var asm = t.Assembly;
                var optedIn = asm.GetCustomAttributes(typeof(LifecycleAssemblyAttribute), false).Length > 0;

                var node = new GraphNode
                {
                    Id = id,
                    DisplayName = id == t ? t.Name : $"{id.Name}  ({t.Name})",
                    Namespace = t.Namespace ?? "-",
                    Phase = attr.Phase,
                    Order = attr.Order,
                    DependsOn = attr.DependsOn ?? Type.EmptyTypes,
                    IsAsync = typeof(IAsyncModule).IsAssignableFrom(t),
                    AutoRegister = attr.AutoRegister,
                    State = ModuleState.Declared,
                    AssemblyName = asm.GetName().Name,
                    AssemblyOptedIn = optedIn,
                };
                
                if (!optedIn && attr.AutoRegister)
                    AddIssue(data, $"'{node.DisplayName}' is in assembly '{node.AssemblyName}' which lacks " +
                                   $"[assembly: LifecycleAssembly] — it will never be registered at runtime.");
                
                byId[id] = node;
                data.Nodes.Add(node);
            }

            // ── 2. cover on playing ──
            data.IsLive = preferLive && EditorApplication.isPlayingOrWillChangePlaymode;
            if (data.IsLive)
            {
                foreach (var e in ModuleRegistry.Entries)
                {
                    if (!byId.TryGetValue(e.Id, out var node))
                    {
                        node = new GraphNode
                        {
                            Id = e.Id,
                            DisplayName = e.DisplayName,
                            Namespace = e.Module?.GetType().Namespace ?? "-",
                            LiveOnly = true,
                        };
                        byId[e.Id] = node;
                        data.Nodes.Add(node);
                    }

                    node.Phase = e.Phase;
                    node.Order = e.Order;
                    node.DependsOn = e.DependsOn;
                    node.IsAsync = e.IsAsync;
                    node.State = e.State;
                    node.Error = e.Error;
                    node.InitMilliseconds = e.InitMilliseconds;
                    node.SortIndex = e.SortIndex;
                }

                foreach (var issue in ModuleRegistry.Issues) data.Issues.Add(issue);
            }

            // ── 3. invert edge + verify ──
            foreach (var n in data.Nodes)
            {
                foreach (var d in n.DependsOn)
                {
                    if (d == null) continue;
                    if (byId.TryGetValue(d, out var dep))
                    {
                        dep.Dependents.Add(n);

                        if (dep.Phase > n.Phase)
                            AddIssue(data, $"'{n.DisplayName}' ({n.Phase}) depends on " +
                                           $"'{dep.DisplayName}' in later phase ({dep.Phase}).");

                        if (dep.IsAsync && !n.IsAsync)
                            AddIssue(data, $"'{n.DisplayName}' is sync but depends on async " +
                                           $"'{dep.DisplayName}'.");
                    }
                    else
                    {
                        AddIssue(data, $"'{n.DisplayName}' depends on unknown module '{d.Name}'.");
                    }
                }
            }

            DetectCycles(data, byId);
            GraphLayout.Layout(data, byId);
            return data;
        }

        static void AddIssue(GraphData data, string msg)
        {
            if (!data.Issues.Contains(msg)) data.Issues.Add(msg);
        }

        static void DetectCycles(GraphData data, Dictionary<Type, GraphNode> byId)
        {
            var color = new Dictionary<Type, int>();
            var stack = new List<GraphNode>();

            foreach (var n in data.Nodes) Visit(n);

            void Visit(GraphNode n)
            {
                color.TryGetValue(n.Id, out var c);
                if (c == 2) return;
                if (c == 1)
                {
                    var idx = stack.FindIndex(x => x.Id == n.Id);
                    var names = new List<string>();
                    for (int i = Math.Max(idx, 0); i < stack.Count; i++)
                    {
                        data.CycleMembers.Add(stack[i].Id);
                        names.Add(stack[i].DisplayName);
                    }
                    data.CycleMembers.Add(n.Id);
                    names.Add(n.DisplayName);
                    AddIssue(data, "Circular dependency: " + string.Join(" -> ", names));
                    return;
                }

                color[n.Id] = 1;
                stack.Add(n);
                foreach (var d in n.DependsOn)
                    if (d != null && byId.TryGetValue(d, out var dep)) Visit(dep);
                stack.RemoveAt(stack.Count - 1);
                color[n.Id] = 2;
            }
        }
    }
}
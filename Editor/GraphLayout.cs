using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal static class GraphLayout
    {
        public const float NodeWidth = 220f;
        public const float NodeHeight = 62f;

        const float ColumnGap = 90f;
        const float RowGap = 26f;
        const float PhaseGap = 70f;
        const float BandPadding = 18f;
        const float HeaderHeight = 30f;
        const float OriginX = 40f;
        const float OriginY = 70f;

        public static void Layout(GraphData data, Dictionary<Type, GraphNode> byId)
        {
            data.Bands.Clear();

            float x = OriginX;
            float maxY = OriginY;

            foreach (ModulePhase phase in Enum.GetValues(typeof(ModulePhase)))
            {
                var inPhase = new List<GraphNode>();
                foreach (var n in data.Nodes)
                    if (n.Phase == phase) inPhase.Add(n);

                if (inPhase.Count == 0) continue;

                var depth = new Dictionary<Type, int>();
                foreach (var n in inPhase) Depth(n, new HashSet<Type>());

                var columns = new SortedDictionary<int, List<GraphNode>>();
                foreach (var n in inPhase)
                {
                    var d = depth[n.Id];
                    if (!columns.TryGetValue(d, out var list))
                        columns[d] = list = new List<GraphNode>();
                    list.Add(n);
                }

                float bandStart = x - BandPadding;

                foreach (var kv in columns)
                {
                    kv.Value.Sort((a, b) =>
                    {
                        var c = a.SortIndex >= 0 && b.SortIndex >= 0
                            ? a.SortIndex.CompareTo(b.SortIndex)
                            : a.Order.CompareTo(b.Order);
                        return c != 0 ? c : string.CompareOrdinal(a.DisplayName, b.DisplayName);
                    });

                    float y = OriginY;
                    foreach (var n in kv.Value)
                    {
                        n.Rect = new Rect(x, y, NodeWidth, NodeHeight);
                        y += NodeHeight + RowGap;
                    }
                    maxY = Mathf.Max(maxY, y);
                    x += NodeWidth + ColumnGap;
                }

                x -= ColumnGap; // 最後一欄不需要欄距

                data.Bands.Add(new PhaseBand
                {
                    Phase = phase,
                    Count = inPhase.Count,
                    Rect = new Rect(bandStart,
                                    OriginY - HeaderHeight - BandPadding,
                                    x - bandStart + BandPadding,
                                    0f) // 高度稍後統一補
                });

                x += PhaseGap;

                int Depth(GraphNode n, HashSet<Type> guard)
                {
                    if (depth.TryGetValue(n.Id, out var cached)) return cached;
                    if (!guard.Add(n.Id)) return 0; // 有環，就地截斷

                    int max = 0;
                    foreach (var d in n.DependsOn)
                    {
                        if (d == null) continue;
                        if (!byId.TryGetValue(d, out var dep)) continue;
                        if (dep.Phase != phase) continue;
                        max = Mathf.Max(max, Depth(dep, guard) + 1);
                    }

                    guard.Remove(n.Id);
                    depth[n.Id] = max;
                    return max;
                }
            }

            float bandHeight = maxY - (OriginY - HeaderHeight - BandPadding) + BandPadding;
            for (int i = 0; i < data.Bands.Count; i++)
            {
                var b = data.Bands[i].Rect;
                b.height = bandHeight;
                data.Bands[i].Rect = b;
            }

            data.Bounds = new Rect(0f, 0f, x + OriginX, maxY + 40f);
        }
    }
}
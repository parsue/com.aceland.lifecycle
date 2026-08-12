using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal static class GraphLayout
    {
        private const float NODE_WIDTH = 220f;
        private const float NODE_HEIGHT = 76f;

        private const float COLUMN_GAP = 110f;
        private const float ROW_GAP = 26f;
        private const float PHASE_GAP = 70f;
        private const float BAND_PADDING = 18f;
        private const float HEADER_HEIGHT = 30f;
        private const float ORIGIN_X = 40f;
        private const float ORIGIN_Y = 70f;

        public static void Layout(GraphData data, Dictionary<Type, GraphNode> byId)
        {
            data.Bands.Clear();

            var x = ORIGIN_X;
            var maxY = ORIGIN_Y;

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

                var bandStart = x - BAND_PADDING;

                foreach (var kv in columns)
                {
                    kv.Value.Sort((a, b) =>
                    {
                        var c = a.SortIndex >= 0 && b.SortIndex >= 0
                            ? a.SortIndex.CompareTo(b.SortIndex)
                            : a.Order.CompareTo(b.Order);
                        return c != 0 ? c : string.CompareOrdinal(a.DisplayName, b.DisplayName);
                    });

                    var y = ORIGIN_Y;
                    foreach (var n in kv.Value)
                    {
                        n.Rect = new Rect(x, y, NODE_WIDTH, NODE_HEIGHT);
                        y += NODE_HEIGHT + ROW_GAP;
                    }
                    maxY = Mathf.Max(maxY, y);
                    x += NODE_WIDTH + COLUMN_GAP;
                }

                x -= COLUMN_GAP; // 最後一欄不需要欄距

                data.Bands.Add(new PhaseBand
                {
                    Phase = phase,
                    Count = inPhase.Count,
                    Rect = new Rect(bandStart,
                                    ORIGIN_Y - HEADER_HEIGHT - BAND_PADDING,
                                    x - bandStart + BAND_PADDING,
                                    0f) // 高度稍後統一補
                });

                x += PHASE_GAP;

                int Depth(GraphNode n, HashSet<Type> guard)
                {
                    if (depth.TryGetValue(n.Id, out var cached)) return cached;
                    if (!guard.Add(n.Id)) return 0; // 有環，就地截斷

                    var max = 0;
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

            var bandHeight = maxY - (ORIGIN_Y - HEADER_HEIGHT - BAND_PADDING) + BAND_PADDING;
            foreach (var t in data.Bands)
            {
                var b = t.Rect;
                b.height = bandHeight;
                t.Rect = b;
            }

            data.Bounds = new Rect(0f, 0f, x + ORIGIN_X, maxY + 40f);
        }
    }
}
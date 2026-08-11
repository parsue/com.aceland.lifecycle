using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AceLand.Lifecycle
{
    public static class LifecycleLog
    {
        const string Prefix = "<b>[Lifecycle]</b> ";

        /// <summary>Turn this off to mute everything (exceptions are still logged).</summary>
        public static bool Enabled = true;

        /// <summary>Whether to print the actual execution order for each phase.</summary>
        public static bool DumpExecutionOrder = true;

#if UNITY_2022_2_OR_NEWER
        [HideInCallstack]
#endif
        public static void Info(string message)
        {
            if (Enabled) Debug.Log(Prefix + message);
        }

#if UNITY_2022_2_OR_NEWER
        [HideInCallstack]
#endif
        public static void Warning(string message)
        {
            if (Enabled) Debug.LogWarning(Prefix + message);
        }

#if UNITY_2022_2_OR_NEWER
        [HideInCallstack]
#endif
        public static void Error(string message) => Debug.LogError(Prefix + message);

        public static void Exception(Exception ex) => Debug.LogException(ex);

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        internal static void DumpOrder(ModulePhase phase, List<ModuleEntry> sorted)
        {
            if (!Enabled || !DumpExecutionOrder || sorted.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append(Prefix).Append("phase <b>").Append(phase).Append("</b> order (")
              .Append(sorted.Count).AppendLine("):");

            for (int i = 0; i < sorted.Count; i++)
            {
                var e = sorted[i];
                sb.Append("  ").Append(i.ToString("00")).Append(". ")
                  .Append(e.DisplayName);
                if (e.IsAsync) sb.Append(" [async]");
                if (e.DependsOn.Length > 0)
                {
                    sb.Append("  <- ");
                    for (int d = 0; d < e.DependsOn.Length; d++)
                    {
                        if (d > 0) sb.Append(", ");
                        sb.Append(e.DependsOn[d]?.Name ?? "null");
                    }
                }
                sb.AppendLine();
            }

            Debug.Log(sb.ToString());
        }
    }
}
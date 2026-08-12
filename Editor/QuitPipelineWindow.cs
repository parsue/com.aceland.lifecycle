using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Standalone quit pipeline inspector. Same data as the graph window's bottom panel,
    /// laid out vertically for a narrow docked column.
    /// </summary>
    internal sealed class QuitPipelineWindow : EditorWindow
    {
        private const float LABEL_WIDTH = 108f;

        private Vector2 _scroll;
        private double _lastRepaint;
        private bool _showReasons = true;

        [MenuItem("Tools/AceLand/Lifecycle/Quit Pipeline", priority = 13)]
        public static void Open()
        {
            var w = GetWindow<QuitPipelineWindow>();
            w.titleContent = new GUIContent("Quit Pipeline");
            w.minSize = new Vector2(300f, 320f);
            w.Show();
        }

        private void OnEnable() => EditorApplication.update += Tick;
        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            var interval = Application.isPlaying && ApplicationQuitPipeline.IsActive ? 0.1 : 0.4;
            if (EditorApplication.timeSinceStartup - _lastRepaint < interval) return;
            _lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(4f);

            DrawStaleBanner();
            DrawStatus();
            DrawBlockers();
            DrawSteps();

            EditorGUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(!Application.isPlaying || ApplicationQuitPipeline.IsQuitting))
            {
                if (GUILayout.Button("Simulate Quit", GUILayout.Height(24f)))
                    ApplicationQuitPipeline.Quit();
            }

            EditorGUILayout.EndScrollView();
        }

        // ── toolbar ────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var phase = ApplicationQuitPipeline.Phase;
                var c = GUI.contentColor;
                GUI.contentColor = QuitPanel.PhaseColor(phase);
                GUILayout.Label(phase.ToString(), EditorStyles.toolbarButton, GUILayout.Width(130f));
                GUI.contentColor = c;

                GUILayout.FlexibleSpace();

                _showReasons = GUILayout.Toggle(_showReasons, "Reasons",
                    EditorStyles.toolbarButton, GUILayout.Width(64f));

                using (new EditorGUI.DisabledScope(
                    Application.isPlaying || !ApplicationQuitPipeline.HasResult))
                {
                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                        ApplicationQuitPipeline.ClearDiagnostics();
                }
            }
        }

        private static void DrawStaleBanner()
        {
            if (Application.isPlaying || !ApplicationQuitPipeline.HasResult) return;

            EditorGUILayout.HelpBox(
                "Showing the last session's result. Statics survive Stop until the next domain reload.",
                MessageType.Warning);
            EditorGUILayout.Space(2f);
        }

        // ── status ─────────────────────────────────────────────────────────

        private static void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            Row("Phase", ApplicationQuitPipeline.Phase.ToString(),
                QuitPanel.PhaseColor(ApplicationQuitPipeline.Phase));
            Row("Quitting", ApplicationQuitPipeline.IsQuitting ? "yes" : "no");
            Row("Ready to quit", ApplicationQuitPipeline.IsReadyToQuit ? "yes" : "no");

            if (Application.isPlaying)
            {
                Row("Alive token", LifecycleToken.IsAlive ? "alive" : "cancelled");
                Row("Quitting token", LifecycleToken.IsQuitting ? "cancelled" : "alive");
            }

            if (ApplicationQuitPipeline.IsQuitting)
            {
                var done = ApplicationQuitPipeline.HasResult;
                Row(done ? "Total" : "Elapsed", $"{ApplicationQuitPipeline.ElapsedSeconds:0.0} s");

                if (!done)
                {
                    var rem = ApplicationQuitPipeline.RemainingSeconds;
                    Row("Remaining", rem < 0d ? "unlimited" : $"{rem:0.0} s");
                }

                if (ApplicationQuitPipeline.TIMEOUT_SECONDS > 0f)
                {
                    var bar = GUILayoutUtility.GetRect(1f, 6f, GUILayout.ExpandWidth(true));
                    var t = Mathf.Clamp01((float)(ApplicationQuitPipeline.ElapsedSeconds /
                                                  ApplicationQuitPipeline.TIMEOUT_SECONDS));
                    EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.35f));
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * t, bar.height),
                        done ? new Color(0.35f, 0.85f, 0.45f)
                             : t > 0.8f ? new Color(0.95f, 0.4f, 0.3f)
                                        : new Color(0.4f, 0.7f, 1f));
                    EditorGUILayout.Space(2f);
                }
            }
            else
            {
                Row("Timeout", ApplicationQuitPipeline.TIMEOUT_SECONDS <= 0f
                    ? "unlimited"
                    : $"{ApplicationQuitPipeline.TIMEOUT_SECONDS:0} s");
            }

            var status = ApplicationQuitPipeline.CurrentStatus;
            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.Space(2f);
                // Multi-line safe: reports its own height back to the layout system.
                EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
            }

            var handler = ApplicationQuitPipeline.CurrentHandler;
            if (!string.IsNullOrEmpty(handler)) Row("Running", handler);

            EditorGUILayout.Space(8f);
        }

        // ── blockers ───────────────────────────────────────────────────────

        private void DrawBlockers()
        {
            var show = Application.isPlaying || ApplicationQuitPipeline.HasResult;
            var blockers = show
                ? ApplicationQuitPipeline.GetBlockers()
                : (System.Collections.Generic.IReadOnlyList<ApplicationQuitPipeline.QuitBlockerInfo>)
                  System.Array.Empty<ApplicationQuitPipeline.QuitBlockerInfo>();

            var busy = 0;
            foreach (var b in blockers) if (b.IsBusy) busy++;

            EditorGUILayout.LabelField(
                busy > 0 ? $"Blockers ({blockers.Count})  —  {busy} busy" : $"Blockers ({blockers.Count})",
                EditorStyles.boldLabel);

            if (blockers.Count == 0)
            {
                EditorGUILayout.LabelField(show ? "    none registered" : "    (play mode only)",
                    EditorStyles.miniLabel);
                EditorGUILayout.Space(8f);
                return;
            }

            foreach (var b in blockers)
            {
                // Name row: fixed height, single line, with the dot.
                var row = EditorGUILayout.GetControlRect(false, 18f);
                EditorGUI.DrawRect(new Rect(row.x + 4f, row.y + 5f, 8f, 8f),
                    b.IsBusy ? new Color(1f, 0.65f, 0.25f) : new Color(0.42f, 0.45f, 0.48f));

                var nameRect = new Rect(row.x + 18f, row.y, row.width - 18f, row.height);
                EditorGUI.LabelField(nameRect, b.Name,
                    b.IsBusy ? EditorStyles.boldLabel : EditorStyles.label);

                if (!_showReasons || string.IsNullOrEmpty(b.Reason)) continue;

                // Reason: wrapped and indented — this is what was overflowing before.
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(18f);
                    EditorGUILayout.LabelField(b.Reason, WrappedIndent(b.IsBusy));
                }

                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.Space(8f);
        }

        // ── steps ──────────────────────────────────────────────────────────

        private static void DrawSteps()
        {
            var steps = ApplicationQuitPipeline.GetSteps();
            var active = ApplicationQuitPipeline.IsActive;
            var suffix = active ? "running" : ApplicationQuitPipeline.HasResult ? "finished" : "plan";

            EditorGUILayout.LabelField($"Handlers ({steps.Count})  —  {suffix}", EditorStyles.boldLabel);

            if (steps.Count == 0)
            {
                EditorGUILayout.LabelField("    no IQuitHandler registered", EditorStyles.miniLabel);
                return;
            }

            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var row = EditorGUILayout.GetControlRect(false, 18f);

                EditorGUI.DrawRect(new Rect(row.x + 4f, row.y + 2f, 3f, row.height - 4f),
                    QuitPanel.StepColor(s.State));

                var text = $"{i:00}. {s.Name}";
                if (s.Order != 0) text += $"   [{s.Order}]";
                if (!s.FromModule) text += "   (manual)";

                var right = s.Milliseconds > 0d ? $"{s.State}  {s.Milliseconds:0} ms" : s.State.ToString();
                var rw = Mathf.Min(140f, row.width * 0.45f);

                var c = GUI.contentColor;
                if (s.State == ApplicationQuitPipeline.QuitStepState.Pending && active)
                    GUI.contentColor = new Color(1f, 1f, 1f, 0.45f);

                EditorGUI.LabelField(new Rect(row.x + 12f, row.y, row.width - 12f - rw, row.height),
                    text, s.State == ApplicationQuitPipeline.QuitStepState.Running
                        ? EditorStyles.boldLabel : EditorStyles.label);
                GUI.contentColor = c;

                EditorGUI.LabelField(new Rect(row.xMax - rw, row.y, rw, row.height), right,
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal = { textColor = QuitPanel.StepColor(s.State) },
                    });
            }
        }

        // ── helpers ────────────────────────────────────────────────────────

        private static GUIStyle WrappedIndent(bool busy) => new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            normal =
            {
                textColor = busy
                    ? new Color(1f, 0.78f, 0.5f)
                    : new Color(0.62f, 0.62f, 0.65f),
            },
        };

        private static void Row(string key, string value) => Row(key, value, Color.clear);

        private static void Row(string key, string value, Color valueColor)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(key, GUILayout.Width(LABEL_WIDTH));

                if (valueColor == Color.clear)
                {
                    EditorGUILayout.LabelField(value, EditorStyles.miniLabel);
                }
                else
                {
                    var c = GUI.contentColor;
                    GUI.contentColor = valueColor;
                    EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
                    GUI.contentColor = c;
                }
            }
        }
    }
}
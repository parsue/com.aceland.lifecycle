using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Bottom panel of the dependency graph: live quit pipeline state, blockers and handler plan.
    /// Reads the pipeline statics directly, so it needs no graph rebuild.
    /// </summary>
    internal sealed class QuitPanel
    {
        private const float STATE_COLUMN = 230f;
        private const float ROW_HEIGHT = 18f;

        private Vector2 _blockerScroll;
        private Vector2 _stepScroll;

        public void Draw(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.185f, 0.185f, 0.195f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0f, 0f, 0f, 0.5f));

            var stale = !Application.isPlaying && ApplicationQuitPipeline.HasResult;
            var showBanner = !Application.isPlaying;

            if (showBanner)
            {
                var banner = new Rect(rect.x, rect.y + 1f, rect.width, 20f);
                EditorGUI.DrawRect(banner, stale
                    ? new Color(0.28f, 0.24f, 0.16f)
                    : new Color(0.2f, 0.2f, 0.21f));

                var text = stale
                    ? "Showing the LAST SESSION result — statics survive until the next domain reload."
                    : "Quit pipeline is inactive in Edit Mode. Handler plan preview is shown below.";

                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = stale ? new Color(1f, 0.82f, 0.5f) : new Color(0.65f, 0.65f, 0.68f) },
                };
                EditorGUI.LabelField(new Rect(banner.x + 8f, banner.y, banner.width - 90f, banner.height),
                                     text, style);

                if (stale)
                {
                    var btn = new Rect(banner.xMax - 74f, banner.y + 2f, 66f, 16f);
                    if (GUI.Button(btn, "Clear", EditorStyles.miniButton))
                        ApplicationQuitPipeline.ClearDiagnostics();
                }
            }

            var top = showBanner ? 26f : 6f;
            var body = new Rect(rect.x + 8f, rect.y + top, rect.width - 16f, rect.height - top - 6f);

            var col1 = new Rect(body.x, body.y, STATE_COLUMN, body.height);
            var remaining = body.width - STATE_COLUMN - 16f;
            var col2 = new Rect(col1.xMax + 8f, body.y, remaining * 0.45f, body.height);
            var col3 = new Rect(col2.xMax + 8f, body.y, remaining * 0.55f, body.height);

            var dim = stale ? 0.55f : 1f;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, dim);

            DrawState(col1, stale);
            DrawBlockers(col2, stale);
            DrawSteps(col3);

            GUI.color = prev;
        }

        // ── column 1 ───────────────────────────────────────────────────────

        private static void DrawState(Rect rect, bool stale)
        {
            GUILayout.BeginArea(rect);

            EditorGUILayout.LabelField(stale ? "Quit State  (last session)" : "Quit State",
                EditorStyles.boldLabel);

            var phase = ApplicationQuitPipeline.Phase;
            var phaseColor = PhaseColor(phase);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Phase", GUILayout.Width(76f));
                var c = GUI.contentColor;
                GUI.contentColor = phaseColor;
                EditorGUILayout.LabelField(phase.ToString(), EditorStyles.boldLabel);
                GUI.contentColor = c;
            }

            Row("Quitting", ApplicationQuitPipeline.IsQuitting ? "yes" : "no");
            Row("Ready", ApplicationQuitPipeline.IsReadyToQuit ? "yes" : "no");

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
                    var bar = GUILayoutUtility.GetRect(rect.width - 8f, 6f);
                    var t = Mathf.Clamp01((float)(ApplicationQuitPipeline.ElapsedSeconds /
                                                  ApplicationQuitPipeline.TIMEOUT_SECONDS));
                    EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.35f));
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * t, bar.height),
                        done ? new Color(0.35f, 0.85f, 0.45f)
                        : t > 0.8f ? new Color(0.95f, 0.4f, 0.3f)
                        : new Color(0.4f, 0.7f, 1f));
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
                EditorGUILayout.LabelField(FirstLine(status), EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || ApplicationQuitPipeline.IsQuitting))
            {
                if (GUILayout.Button("Simulate Quit", EditorStyles.miniButton))
                    ApplicationQuitPipeline.Quit();
            }

            GUILayout.EndArea();

            void Row(string k, string v)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(k, GUILayout.Width(76f));
                    EditorGUILayout.LabelField(v, EditorStyles.miniLabel);
                }
            }
        }

        // ── column 2 ───────────────────────────────────────────────────────

        private void DrawBlockers(Rect rect, bool stale)
        {
            GUILayout.BeginArea(rect);

            var show = Application.isPlaying || ApplicationQuitPipeline.HasResult;

            IReadOnlyList<ApplicationQuitPipeline.QuitBlockerInfo> blockers =
                show ? ApplicationQuitPipeline.GetBlockers()
                    : new List<ApplicationQuitPipeline.QuitBlockerInfo>();

            var busy = 0;
            foreach (var b in blockers) if (b.IsBusy) busy++;

            EditorGUILayout.LabelField(
                busy > 0 ? $"Blockers ({blockers.Count})  —  {busy} busy" : $"Blockers ({blockers.Count})",
                EditorStyles.boldLabel);

            if (blockers.Count == 0)
            {
                EditorGUILayout.LabelField(show ? "  none registered" : "  (play mode only)",
                    EditorStyles.miniLabel);
                GUILayout.EndArea();
                return;
            }

            _blockerScroll = EditorGUILayout.BeginScrollView(_blockerScroll);

            foreach (var b in blockers)
            {
                var row = GUILayoutUtility.GetRect(rect.width - 20f, ROW_HEIGHT);
                var dot = new Rect(row.x, row.y + 5f, 8f, 8f);

                EditorGUI.DrawRect(dot, b.IsBusy
                    ? new Color(1f, 0.65f, 0.25f)
                    : new Color(0.42f, 0.45f, 0.48f));

                var label = new Rect(row.x + 14f, row.y, row.width - 14f, row.height);
                var style = b.IsBusy ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUI.LabelField(label, b.Name, style);

                if (!string.IsNullOrEmpty(b.Reason))
                {
                    var reasonStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                    var w = rect.width - 34f;
                    var h = reasonStyle.CalcHeight(new GUIContent(b.Reason), w);

                    var reason = GUILayoutUtility.GetRect(w, Mathf.Min(h, 34f));
                    EditorGUI.LabelField(new Rect(reason.x + 14f, reason.y, w, reason.height),
                        b.Reason, reasonStyle);
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── column 3 ───────────────────────────────────────────────────────

        private void DrawSteps(Rect rect)
        {
            GUILayout.BeginArea(rect);

            var steps = ApplicationQuitPipeline.GetSteps();
            var active = ApplicationQuitPipeline.IsActive;
            var done = ApplicationQuitPipeline.HasResult;

            var suffix = active ? "running" : done ? "finished" : "plan";
            EditorGUILayout.LabelField($"Handlers ({steps.Count})  —  {suffix}", EditorStyles.boldLabel);
            
            var live = ApplicationQuitPipeline.IsQuitting;
            EditorGUILayout.LabelField(
                live ? $"Handlers ({steps.Count})  —  running" : $"Handlers ({steps.Count})  —  plan",
                EditorStyles.boldLabel);

            if (steps.Count == 0)
            {
                EditorGUILayout.LabelField("  no IQuitHandler registered", EditorStyles.miniLabel);
                GUILayout.EndArea();
                return;
            }

            _stepScroll = EditorGUILayout.BeginScrollView(_stepScroll);

            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var row = GUILayoutUtility.GetRect(rect.width - 20f, ROW_HEIGHT);

                EditorGUI.DrawRect(new Rect(row.x, row.y + 2f, 3f, row.height - 4f), StepColor(s.State));

                var text = $"{i:00}. {s.Name}";
                if (s.Order != 0) text += $"   [{s.Order}]";
                if (!s.FromModule) text += "   (manual)";

                var style = s.State == ApplicationQuitPipeline.QuitStepState.Running
                    ? EditorStyles.boldLabel
                    : EditorStyles.label;

                var c = GUI.contentColor;
                if (s.State == ApplicationQuitPipeline.QuitStepState.Pending && active)
                    GUI.contentColor = new Color(1f, 1f, 1f, 0.45f);

                EditorGUI.LabelField(new Rect(row.x + 8f, row.y, row.width - 8f, row.height), text, style);
                GUI.contentColor = c;

                var right = $"{s.State}";
                if (s.Milliseconds > 0d) right += $"  {s.Milliseconds:0} ms";

                var rr = new Rect(row.xMax - 130f, row.y, 130f, row.height);
                var rs = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = StepColor(s.State) },
                };
                EditorGUI.LabelField(rr, right, rs);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── helpers ────────────────────────────────────────────────────────

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var i = text.IndexOf('\n');
            return i < 0 ? text : text.Substring(0, i) + " …";
        }

        internal static Color PhaseColor(ApplicationQuitPipeline.QuitPhase p)
        {
            switch (p)
            {
                case ApplicationQuitPipeline.QuitPhase.WaitingForBlockers: return new Color(1f, 0.65f, 0.25f);
                case ApplicationQuitPipeline.QuitPhase.RunningHandlers: return new Color(0.4f, 0.75f, 1f);
                case ApplicationQuitPipeline.QuitPhase.ShuttingDown: return new Color(0.85f, 0.6f, 1f);
                case ApplicationQuitPipeline.QuitPhase.Completed: return new Color(0.35f, 0.85f, 0.45f);
                case ApplicationQuitPipeline.QuitPhase.Forced: return new Color(0.95f, 0.35f, 0.3f);
                default: return new Color(0.6f, 0.6f, 0.64f);
            }
        }

        internal static Color StepColor(ApplicationQuitPipeline.QuitStepState s)
        {
            switch (s)
            {
                case ApplicationQuitPipeline.QuitStepState.Running: return new Color(1f, 0.85f, 0.3f);
                case ApplicationQuitPipeline.QuitStepState.Done: return new Color(0.35f, 0.85f, 0.45f);
                case ApplicationQuitPipeline.QuitStepState.Failed: return new Color(0.95f, 0.35f, 0.3f);
                case ApplicationQuitPipeline.QuitStepState.TimedOut: return new Color(1f, 0.5f, 0.2f);
                case ApplicationQuitPipeline.QuitStepState.Skipped: return new Color(1f, 0.6f, 0.25f);
                default: return new Color(0.45f, 0.47f, 0.5f);
            }
        }
    }
}
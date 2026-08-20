using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// A Gantt-style visualization of the last (or in-progress) initialization run, driven by
    /// <see cref="ModuleRegistry.GetTimeline"/>. Each module is a bar positioned on a shared time
    /// axis, coloured by phase; the async portion of a bar is drawn lighter so parallel awaits are
    /// visible, and modules that failed / were skipped / timed out are outlined in red.
    ///
    /// The chart opens with a "Total / Whole Initialization" root row whose bar spans the whole run,
    /// segmented by phase colour, followed by collapsible per-phase groups.
    /// </summary>
    internal sealed class InitializationTimelineWindow : EditorWindow
    {
        // ── Tunables ────────────────────────────────────────────────────────
        // Adjust the constants below to change the timeline's appearance and interaction feel.
        //   Layout (all sizes are in pixels)
        private const float ROW_HEIGHT = 22f;      // Height of each module row
        private const float ROW_GAP = 3f;          // Vertical gap between rows
        private const float HEADER_HEIGHT = 22f;   // Height of a phase header row
        private const float LABEL_WIDTH = 240f;    // Width of the left-hand name column
        private const float RULER_HEIGHT = 22f;    // Height of the top time ruler
        private const float TRACK_PADDING = 12f;   // Left/right inner padding of the track
        private const float MIN_BAR_WIDTH = 3f;    // Minimum bar width (drawn at least this wide even when very short)
        private const float SIDEBAR_WIDTH = 300f;  // Width of the right-hand inspector panel

        //   Zoom & interaction
        private const float DEFAULT_PIXELS_PER_MS = 6f; // Default zoom when the window opens (pixels per millisecond)
        private const float ZOOM_MIN = 0.02f;   // Minimum zoom: smaller values let you "zoom out" further (see a longer time span)
        private const float ZOOM_MAX = 60f;     // Maximum zoom: larger values let you "zoom in" further (inspect tiny segments)
        private const float ZOOM_WHEEL_STEP = 1.15f; // Zoom multiplier per scroll-wheel notch (larger = faster zoom)
        private const float TICK_TARGET_PX = 70f;    // Target tick spacing: smaller value -> denser ticks (more time marks)

        // Phase → base colour. Async segments render at a lighter tint of the same hue.
        private static readonly Color coreColor    = new(0.36f, 0.62f, 0.92f);
        private static readonly Color runtimeColor = new(0.42f, 0.78f, 0.55f);
        private static readonly Color sceneColor   = new(0.85f, 0.70f, 0.32f);
        private static readonly Color lateColor    = new(0.70f, 0.52f, 0.85f);

        private static readonly Color problemOutline = new(1.00f, 0.28f, 0.22f);
        private static readonly Color rulerColor     = new(0.30f, 0.31f, 0.34f);
        private static readonly Color rowAltColor    = new(1f, 1f, 1f, 0.03f);
        private static readonly Color selectedTint   = new(1f, 1f, 1f, 0.18f);
        private static readonly Color rootRowColor   = new(1f, 1f, 1f, 0.06f);
        private static readonly Color headerRowColor = new(1f, 1f, 1f, 0.05f);

        private LifecycleTimeline _timeline;
        private ModuleTimingInfo? _selected;
        private Vector2 _listScroll;
        private Vector2 _inspectorScroll;
        private float _pixelsPerMs = DEFAULT_PIXELS_PER_MS;
        private bool _liveMode = true;
        private double _lastRepaint;

        // Per-phase collapse state for the grouped rows (default: expanded).
        private readonly Dictionary<ModulePhase, bool> _collapsed = new();

        private GUIStyle _barLabelStyle;
        private GUIStyle _rowLabelStyle;
        private GUIStyle _rulerStyle;
        private GUIStyle _rootLabelStyle;
        private GUIStyle _foldoutStyle;

        [MenuItem("Tools/AceLand/Lifecycle/Initialization Timeline", priority = 11)]
        public static void Open()
        {
            var w = GetWindow<InitializationTimelineWindow>();
            w.titleContent = new GUIContent("Init Timeline");
            w.minSize = new Vector2(820f, 480f);
            w.Show();
        }

        /// <summary>
        /// Opens the Initialization Timeline, then selects and scrolls to the module whose
        /// display name matches <paramref name="displayName"/>. Used by the Initialization
        /// Graph's "View in Initialization Timeline" action (reverse cross-jump).
        /// </summary>
        public static void FocusModule(string displayName)
        {
            var w = GetWindow<InitializationTimelineWindow>();
            w.titleContent = new GUIContent("Init Timeline");
            w.minSize = new Vector2(820f, 480f);
            w.Show();
            w.Focus();
            w.Rebuild();
            w.SelectByDisplayName(displayName);
            w.Repaint();
        }

        private void SelectByDisplayName(string displayName)
        {
            if (_timeline == null || string.IsNullOrEmpty(displayName)) return;

            var modules = _timeline.Modules;

            ModuleTimingInfo? found = null;
            for (var i = 0; i < modules.Count; i++)
            {
                if (modules[i].DisplayName != displayName) continue;
                found = modules[i];
                break;
            }
            if (!found.HasValue) return;

            _selected = found;
            // Ensure the phase containing the target is expanded so it is visible.
            SetCollapsed(found.Value.Phase, false);

            // Replicate the chart layout to scroll the target row into view.
            var y = RULER_HEIGHT + ROW_HEIGHT + ROW_GAP; // ruler + root row
            var groups = BuildGroups(modules);
            foreach (var g in groups)
            {
                y += HEADER_HEIGHT + ROW_GAP;
                if (IsCollapsed(g.phase)) continue;

                foreach (var m in g.mods)
                {
                    if (m.Id == found.Value.Id)
                    {
                        _listScroll.y = Mathf.Max(0f, y - (position.height - RULER_HEIGHT) * 0.5f);
                        return;
                    }
                    y += ROW_HEIGHT + ROW_GAP;
                }
            }
        }

        private void OnEnable()
        {
            Rebuild();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            Rebuild();
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += Repaint;
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;
            if (!_liveMode) return;

            // Refresh briskly while modules are still settling, then relax.
            var interval = ModuleRegistry.IsInitialized ? 0.5 : 0.15;
            if (EditorApplication.timeSinceStartup - _lastRepaint < interval) return;
            _lastRepaint = EditorApplication.timeSinceStartup;

            Rebuild();
            Repaint();
        }

        private void Rebuild()
        {
            _timeline = ModuleRegistry.GetTimeline();

            // Re-resolve the current selection against the fresh snapshot.
            if (_selected.HasValue)
            {
                var id = _selected.Value.Id;
                _selected = null;
                foreach (var m in _timeline.Modules)
                {
                    if (m.Id != id) continue;
                    _selected = m;
                    break;
                }
            }
        }

        // ── Grouping ────────────────────────────────────────────────────────

        private static List<(ModulePhase phase, List<ModuleTimingInfo> mods)> BuildGroups(
            IReadOnlyList<ModuleTimingInfo> modules)
        {
            var groups = new List<(ModulePhase phase, List<ModuleTimingInfo> mods)>();
            var index = new Dictionary<ModulePhase, int>();

            foreach (var m in modules)
            {
                if (!index.TryGetValue(m.Phase, out var gi))
                {
                    gi = groups.Count;
                    index[m.Phase] = gi;
                    groups.Add((m.Phase, new List<ModuleTimingInfo>()));
                }
                groups[gi].mods.Add(m);
            }

            return groups;
        }

        private Dictionary<ModulePhase, PhaseTimingInfo> BuildPhaseLookup()
        {
            var map = new Dictionary<ModulePhase, PhaseTimingInfo>();
            foreach (var p in _timeline.Phases) map[p.Phase] = p;
            return map;
        }

        private bool IsCollapsed(ModulePhase p) => _collapsed.TryGetValue(p, out var c) && c;
        private void SetCollapsed(ModulePhase p, bool v) => _collapsed[p] = v;

        // ── GUI ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            if (_timeline == null) Rebuild();

            DrawToolbar();

            if (_timeline == null || _timeline.IsEmpty)
            {
                DrawEmptyState();
                return;
            }

            var body = new Rect(0f, EditorGUIUtility.singleLineHeight + 4f, position.width,
                position.height - EditorGUIUtility.singleLineHeight - 4f);

            var chartRect = new Rect(body.x, body.y, body.width - SIDEBAR_WIDTH, body.height);
            var sidebarRect = new Rect(chartRect.xMax, body.y, SIDEBAR_WIDTH, body.height);

            DrawChart(chartRect);
            DrawSidebar(sidebarRect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _liveMode = GUILayout.Toggle(_liveMode, "Live", EditorStyles.toolbarButton,
                    GUILayout.Width(48f));

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    Rebuild();

                if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    SetAllCollapsed(false);
                if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    SetAllCollapsed(true);

                GUILayout.Space(8f);
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(36f));
                _pixelsPerMs = GUILayout.HorizontalSlider(_pixelsPerMs, ZOOM_MIN, ZOOM_MAX, GUILayout.Width(120f));

                GUILayout.Space(8f);
                DrawExportDropdown();

                GUILayout.FlexibleSpace();

                var total = _timeline?.TotalMs ?? 0d;
                var problems = _timeline?.ProblemCount ?? 0;
                var moduleCount = _timeline?.Modules.Count ?? 0;
                var enabled = LifecycleProfiler.Enabled ? "on" : "off";
                var summary = $"{moduleCount} modules · {total:0.0} ms · profiler {enabled}";
                if (problems > 0) summary += $" · {problems} problem(s)";
                GUILayout.Label(summary, EditorStyles.miniLabel);
            }
        }

        private void SetAllCollapsed(bool collapsed)
        {
            if (_timeline == null) return;
            foreach (var g in BuildGroups(_timeline.Modules))
                SetCollapsed(g.phase, collapsed);
        }

        private void DrawExportDropdown()
        {
            var hasData = _timeline != null && !_timeline.IsEmpty;
            using (new EditorGUI.DisabledScope(!hasData))
            {
                if (!EditorGUILayout.DropdownButton(new GUIContent("Export"),
                        FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(70f)))
                    return;

                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Export as JSON"), false,
                    () => Export(TimelineExportFormat.Json));
                menu.AddItem(new GUIContent("Export as CSV"), false,
                    () => Export(TimelineExportFormat.Csv));
                menu.ShowAsContext();
            }
        }

        private void Export(TimelineExportFormat format)
        {
            var path = LifecycleTimelineExporter.ExportWithDialog(_timeline, format);
            if (!string.IsNullOrEmpty(path))
                ShowNotification(new GUIContent($"Exported to {System.IO.Path.GetFileName(path)}"));
        }

        private void DrawEmptyState()
        {
            var msg = LifecycleProfiler.Enabled
                ? "No initialization timeline captured yet.\nEnter Play mode to record module timings."
                : "Profiler is disabled (LifecycleProfiler.Enabled = false).\nEnable it and enter Play mode to record timings.";

            var r = new Rect(0f, 0f, position.width, position.height);
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12, wordWrap = true };
            GUI.Label(r, msg, style);
        }

        private void DrawChart(Rect rect)
        {
            var modules = _timeline.Modules;
            var total = Mathf.Max(1f, (float)_timeline.TotalMs);

            // Mouse-wheel to zoom (cursor-anchored) + left/middle drag to pan.
            HandleChartInput(rect);

            var trackLeft = rect.x + LABEL_WIDTH;
            var trackWidth = Mathf.Max(120f, total * _pixelsPerMs + TRACK_PADDING * 2f);

            var groups = BuildGroups(modules);
            var phaseLookup = BuildPhaseLookup();

            // Height = ruler + root row + Σ(header + visible module rows).
            var contentHeight = RULER_HEIGHT + (ROW_HEIGHT + ROW_GAP);
            foreach (var g in groups)
            {
                contentHeight += HEADER_HEIGHT + ROW_GAP;
                if (!IsCollapsed(g.phase))
                    contentHeight += g.mods.Count * (ROW_HEIGHT + ROW_GAP);
            }
            contentHeight += 20f;

            var viewRect = new Rect(0f, 0f, LABEL_WIDTH + trackWidth, contentHeight);
            _listScroll = GUI.BeginScrollView(rect, _listScroll, viewRect);

            var localTrackLeft = trackLeft - rect.x + TRACK_PADDING;

            DrawRuler(new Rect(trackLeft - rect.x, 0f, trackWidth, RULER_HEIGHT), total);

            var y = RULER_HEIGHT;

            // Root row: whole-run total bar segmented by phase colour.
            DrawRootRow(new Rect(0f, y, viewRect.width, ROW_HEIGHT), localTrackLeft, y, total);
            y += ROW_HEIGHT + ROW_GAP;

            var rowParity = 0;
            foreach (var g in groups)
            {
                phaseLookup.TryGetValue(g.phase, out var pInfo);
                var hasInfo = phaseLookup.ContainsKey(g.phase);

                DrawPhaseHeader(new Rect(0f, y, viewRect.width, HEADER_HEIGHT), g.phase,
                    hasInfo ? pInfo : (PhaseTimingInfo?)null, localTrackLeft, g.mods.Count);
                y += HEADER_HEIGHT + ROW_GAP;

                if (IsCollapsed(g.phase)) continue;

                foreach (var m in g.mods)
                {
                    var rowRect = new Rect(0f, y, viewRect.width, ROW_HEIGHT);

                    if (rowParity % 2 == 1) EditorGUI.DrawRect(rowRect, rowAltColor);
                    if (_selected.HasValue && _selected.Value.Id == m.Id)
                        EditorGUI.DrawRect(rowRect, selectedTint);

                    // Label column (indented under the phase header).
                    var labelRect = new Rect(20f, y, LABEL_WIDTH - 24f, ROW_HEIGHT);
                    _rowLabelStyle.normal.textColor =
                        m.IsProblem ? problemOutline : EditorStyles.label.normal.textColor;
                    GUI.Label(labelRect, m.DisplayName, _rowLabelStyle);

                    if (m.DidRun)
                        DrawBar(m, localTrackLeft, y);

                    // Row hit-testing for selection & cross-jump.
                    if (Event.current.type == EventType.MouseDown &&
                        rowRect.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.button == 1)
                            ShowRowContextMenu(m);
                        else
                            _selected = m;
                        Event.current.Use();
                        Repaint();
                    }

                    y += ROW_HEIGHT + ROW_GAP;
                    rowParity++;
                }
            }

            GUI.EndScrollView();
        }

        /// <summary>
        /// Chart interaction: mouse-wheel zooms the time axis (anchored so the time under the
        /// cursor stays put), and dragging with the left or middle button pans the view.
        /// Tune feel via <see cref="ZOOM_WHEEL_STEP"/>, <see cref="ZOOM_MIN"/> and <see cref="ZOOM_MAX"/>.
        /// </summary>
        private void HandleChartInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                {
                    // Content-space x under the cursor (accounts for current horizontal scroll).
                    var contentX = _listScroll.x + (e.mousePosition.x - rect.x);
                    var trackX = contentX - LABEL_WIDTH - TRACK_PADDING;
                    var timeUnderCursor = trackX / _pixelsPerMs;

                    // e.delta.y > 0 means scrolling down / towards the user → zoom out.
                    var factor = e.delta.y > 0f ? 1f / ZOOM_WHEEL_STEP : ZOOM_WHEEL_STEP;
                    var newZoom = Mathf.Clamp(_pixelsPerMs * factor, ZOOM_MIN, ZOOM_MAX);
                    if (!Mathf.Approximately(newZoom, _pixelsPerMs))
                    {
                        _pixelsPerMs = newZoom;
                        // Re-anchor: keep the same time value under the cursor after zooming.
                        var newTrackX = timeUnderCursor * _pixelsPerMs;
                        _listScroll.x = Mathf.Max(0f,
                            newTrackX + LABEL_WIDTH + TRACK_PADDING - (e.mousePosition.x - rect.x));
                        Repaint();
                    }
                    e.Use();
                    break;
                }

                case EventType.MouseDrag:
                {
                    // Left (0) or middle (2) button drags the whole view.
                    if (e.button != 0 && e.button != 2) break;
                    _listScroll.x = Mathf.Max(0f, _listScroll.x - e.delta.x);
                    _listScroll.y = Mathf.Max(0f, _listScroll.y - e.delta.y);
                    Repaint();
                    e.Use();
                    break;
                }
            }
        }

        private void DrawRootRow(Rect rowRect, float trackLeft, float rowY, float total)
        {
            EditorGUI.DrawRect(rowRect, rootRowColor);

            var labelRect = new Rect(4f, rowY, LABEL_WIDTH - 8f, ROW_HEIGHT);
            GUI.Label(labelRect, "Total / Whole Initialization", _rootLabelStyle);

            var barY = rowY + 3f;
            var barH = ROW_HEIGHT - 6f;

            if (_timeline.Phases.Count == 0)
            {
                // Fallback: single grey bar spanning the whole run.
                var w = Mathf.Max(MIN_BAR_WIDTH, total * _pixelsPerMs);
                EditorGUI.DrawRect(new Rect(trackLeft, barY, w, barH), Color.gray);
            }
            else
            {
                foreach (var p in _timeline.Phases)
                {
                    var segX = trackLeft + (float)p.StartedAtMs * _pixelsPerMs;
                    var segW = Mathf.Max(1f, (float)p.DurationMs * _pixelsPerMs);
                    EditorGUI.DrawRect(new Rect(segX, barY, segW, barH), PhaseColor(p.Phase));
                }
            }

            // Total duration printed just past the end of the bar.
            var endX = trackLeft + total * _pixelsPerMs;
            GUI.Label(new Rect(endX + 6f, rowY, 90f, ROW_HEIGHT), $"{_timeline.TotalMs:0.0} ms",
                _rulerStyle);
        }

        private void DrawPhaseHeader(Rect rowRect, ModulePhase phase, PhaseTimingInfo? info,
            float trackLeft, int moduleCount)
        {
            EditorGUI.DrawRect(rowRect, headerRowColor);

            // Colour swatch.
            var swatch = new Rect(4f, rowRect.y + (HEADER_HEIGHT - 12f) * 0.5f, 12f, 12f);
            EditorGUI.DrawRect(swatch, PhaseColor(phase));

            // Faint phase span in the track area for orientation.
            if (info.HasValue)
            {
                var p = info.Value;
                var segX = trackLeft + (float)p.StartedAtMs * _pixelsPerMs;
                var segW = Mathf.Max(1f, (float)p.DurationMs * _pixelsPerMs);
                var c = PhaseColor(phase);
                c.a = 0.30f;
                EditorGUI.DrawRect(new Rect(segX, rowRect.y + 5f, segW, HEADER_HEIGHT - 10f), c);
            }

            var label = phase.ToString();
            if (info.HasValue)
            {
                var p = info.Value;
                label += $"  ({p.DurationMs:0.0} ms · {p.ModuleCount} mod · {p.Batches} batch";
                if (p.TimedOut) label += " · TIMEOUT";
                label += ")";
            }
            else
            {
                label += $"  ({moduleCount} mod)";
            }

            var foldRect = new Rect(20f, rowRect.y + (HEADER_HEIGHT - EditorGUIUtility.singleLineHeight) * 0.5f,
                LABEL_WIDTH - 24f, EditorGUIUtility.singleLineHeight);
            var expanded = EditorGUI.Foldout(foldRect, !IsCollapsed(phase), label, true, _foldoutStyle);
            SetCollapsed(phase, !expanded);
        }

        private void DrawBar(ModuleTimingInfo m, float trackLeft, float rowY)
        {
            var barY = rowY + 3f;
            var barH = ROW_HEIGHT - 6f;

            var startX = trackLeft + (float)m.StartedAtMs * _pixelsPerMs;
            var totalW = Mathf.Max(MIN_BAR_WIDTH, (float)m.TotalMs * _pixelsPerMs);

            var baseColor = PhaseColor(m.Phase);

            // Sync segment (solid) + async segment (lighter) so awaits are distinguishable.
            var syncW = Mathf.Max(0f, (float)m.SyncMs * _pixelsPerMs);
            var asyncW = Mathf.Max(0f, (float)m.AsyncMs * _pixelsPerMs);
            if (syncW + asyncW < MIN_BAR_WIDTH) syncW = totalW;

            var syncRect = new Rect(startX, barY, Mathf.Max(1f, syncW), barH);
            EditorGUI.DrawRect(syncRect, baseColor);

            if (asyncW >= 1f)
            {
                var asyncRect = new Rect(startX + syncW, barY, asyncW, barH);
                var lighter = Color.Lerp(baseColor, Color.white, 0.45f);
                lighter.a = 0.85f;
                EditorGUI.DrawRect(asyncRect, lighter);
            }

            // Failure / skip / timeout emphasis: red outline.
            if (m.IsProblem)
                DrawOutline(new Rect(startX, barY, Mathf.Max(totalW, syncW + asyncW), barH),
                    problemOutline, 2f);

            // Inline duration label when the bar is wide enough.
            var fullW = Mathf.Max(totalW, syncW + asyncW);
            if (fullW > 34f)
            {
                var textRect = new Rect(startX + 3f, barY, fullW - 6f, barH);
                GUI.Label(textRect, $"{m.TotalMs:0.0}", _barLabelStyle);
            }
        }

        private void DrawRuler(Rect rect, float totalMs)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), rulerColor);

            // Tick spacing is derived from the current zoom so the axis auto-densifies as you
            // zoom in and coarsens as you zoom out (target pixel gap = TICK_TARGET_PX).
            var step = NiceStep();
            var left = rect.x + TRACK_PADDING;

            // Minor ticks at step/5 give a finer reference without cluttering labels.
            var minor = step / 5f;
            var minorColor = rulerColor;
            minorColor.a *= 0.5f;
            for (var t = 0f; t <= totalMs + 0.001f; t += minor)
            {
                var mx = left + t * _pixelsPerMs;
                EditorGUI.DrawRect(new Rect(mx, rect.y + 12f, 1f, rect.height - 12f), minorColor);
            }

            for (var t = 0f; t <= totalMs + 0.001f; t += step)
            {
                var x = left + t * _pixelsPerMs;
                EditorGUI.DrawRect(new Rect(x, rect.y + 4f, 1f, rect.height - 4f), rulerColor);
                GUI.Label(new Rect(x + 2f, rect.y, 60f, rect.height), FormatTick(t), _rulerStyle);
            }
        }

        private static string FormatTick(float ms) =>
            ms >= 1000f ? $"{ms / 1000f:0.##}s" : $"{ms:0.##}ms";

        private void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), rulerColor);

            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            EditorGUILayout.LabelField("Phases", EditorStyles.boldLabel);
            foreach (var p in _timeline.Phases)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var swatch = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
                    EditorGUI.DrawRect(swatch, PhaseColor(p.Phase));
                    var label = $"{p.Phase}: {p.DurationMs:0.0} ms · {p.ModuleCount} mod · {p.Batches} batch";
                    if (p.TimedOut) label += " · TIMEOUT";
                    EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(8f);

            if (_selected.HasValue)
            {
                var m = _selected.Value;
                EditorGUILayout.LabelField("Selected Module", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Name", m.DisplayName);
                EditorGUILayout.LabelField("Phase", m.Phase.ToString());
                EditorGUILayout.LabelField("State", m.State.ToString());
                EditorGUILayout.LabelField("Async", m.IsAsync ? "yes" : "no");
                EditorGUILayout.LabelField("Parallel", m.AllowParallel ? "yes" : "no");
                if (m.Level >= 0) EditorGUILayout.LabelField("Batch (level)", m.Level.ToString());
                if (m.DidRun)
                {
                    EditorGUILayout.LabelField("Start", $"{m.StartedAtMs:0.0} ms");
                    EditorGUILayout.LabelField("End", $"{m.EndedAtMs:0.0} ms");
                    EditorGUILayout.LabelField("Sync", $"{m.SyncMs:0.0} ms");
                    EditorGUILayout.LabelField("Async", $"{m.AsyncMs:0.0} ms");
                    EditorGUILayout.LabelField("Total", $"{m.TotalMs:0.0} ms");
                }
                else
                {
                    EditorGUILayout.LabelField("(did not run)");
                }

                if (!string.IsNullOrEmpty(m.Error))
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(m.Error, MessageType.Error);
                }

                EditorGUILayout.Space(6f);
                if (GUILayout.Button("View in Initialization Graph"))
                    DependencyGraphWindow.FocusNode(m.DisplayName);
            }
            else
            {
                EditorGUILayout.HelpBox("Select a module bar to inspect its timing.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void ShowRowContextMenu(ModuleTimingInfo m)
        {
            _selected = m;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("View in Initialization Graph"), false,
                () => DependencyGraphWindow.FocusNode(m.DisplayName));
            menu.ShowAsContext();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static Color PhaseColor(ModulePhase phase) => phase switch
        {
            ModulePhase.Core => coreColor,
            ModulePhase.Runtime => runtimeColor,
            ModulePhase.Scene => sceneColor,
            ModulePhase.Late => lateColor,
            _ => Color.gray,
        };

        private static void DrawOutline(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        /// <summary>
        /// Zoom-aware tick step (in ms). Picks the smallest 1/2/5 × 10^n value whose on-screen
        /// width is at least <see cref="TICK_TARGET_PX"/> pixels — so ticks stay readable while
        /// automatically getting denser as you zoom in. Lower TICK_TARGET_PX ⇒ more ticks.
        /// </summary>
        private float NiceStep()
        {
            var rawMs = TICK_TARGET_PX / Mathf.Max(0.0001f, _pixelsPerMs);
            var mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(0.0001f, rawMs))));
            var norm = rawMs / mag;
            float snapped;
            if (norm <= 1f) snapped = 1f;
            else if (norm <= 2f) snapped = 2f;
            else if (norm <= 5f) snapped = 5f;
            else snapped = 10f;
            return Mathf.Max(0.01f, snapped * mag);
        }

        private void EnsureStyles()
        {
            _barLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 9,
                normal = { textColor = new Color(0.05f, 0.05f, 0.06f) },
            };
            _rowLabelStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                clipping = TextClipping.Clip,
            };
            _rulerStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 9,
                normal = { textColor = new Color(0.7f, 0.7f, 0.72f) },
            };
            _rootLabelStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            _foldoutStyle ??= new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
            };
        }
    }
}

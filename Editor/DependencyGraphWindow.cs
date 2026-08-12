using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal sealed class DependencyGraphWindow : EditorWindow
    {
        private const float MIN_ZOOM = 0.35f;
        private const float MAX_ZOOM = 1.6f;
        private const float SIDEBAR_WIDTH = 300f;
        private const float ACCENT_WIDTH = 8f;
        private const float QUIT_BADGE_WIDTH = 5f;
        private const float PANEL_MIN = 90f;
        private const float PANEL_MAX = 420f;
        private const float SPLITTER = 5f;
        
        private const float EDGE_WIDTH = 2.5f;
        private const float EDGE_WIDTH_HI = 4.5f;
        private const float EDGE_HALO = 2f;
        private const float ARROW_SIZE = 9f;
        
        private static readonly Color edgeNormal   = new(0.62f, 0.65f, 0.70f);
        private static readonly Color edgeMuted    = new(0.30f, 0.31f, 0.34f);
        private static readonly Color edgeUpstream = new(0.35f, 0.72f, 1.00f);
        private static readonly Color edgeDownstrm = new(1.00f, 0.76f, 0.25f);
        private static readonly Color edgeCycle    = new(1.00f, 0.28f, 0.22f);
        private static readonly Color edgeHalo     = new(0.08f, 0.08f, 0.09f);

        private readonly QuitPanel _quitPanel = new();
        private bool _showQuitPanel = true;
        private float _panelHeight = 150f;
        private bool _draggingSplitter;

        private GraphData _data;
        private Vector2 _pan = Vector2.zero;
        private float _zoom = 1f;
        private GraphNode _selected;
        private Vector2 _issueScroll;
        private Vector2 _inspectorScroll;
        private bool _showIssues = true;
        private bool _liveMode = true;
        private string _search = string.Empty;
        private double _lastRepaint;

        private readonly HashSet<Type> _highlightUp = new();
        private readonly HashSet<Type> _highlightDown = new();

        [MenuItem("Tools/AceLand/Lifecycle/Dependency Graph", priority = 10)]
        public static void Open()
        {
            var w = GetWindow<DependencyGraphWindow>();
            w.titleContent = new GUIContent("Lifecycle Graph");
            w.minSize = new Vector2(860f, 560f);
            w.Show();
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

            // Statics survive Stop; force one paint so the final quit result is accurate.
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += Repaint;
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;

            var quitting = ApplicationQuitPipeline.IsQuitting;
            var interval = quitting ? 0.1 : 0.5;

            if (EditorApplication.timeSinceStartup - _lastRepaint < interval) return;
            _lastRepaint = EditorApplication.timeSinceStartup;

            if (_liveMode) Rebuild();
            Repaint();
        }

        private void Rebuild()
        {
            _data = ModuleGraphModel.Build(_liveMode);
            if (_selected != null)
            {
                var id = _selected.Id;
                _selected = _data.Nodes.Find(n => n.Id == id);
                RecalculateHighlight();
            }
        }

        // ── GUI ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_data == null) Rebuild();

            DrawToolbar();

            var body = new Rect(0f, EditorStyles.toolbar.fixedHeight,
                position.width, position.height - EditorStyles.toolbar.fixedHeight);

            var panelH = _showQuitPanel ? Mathf.Clamp(_panelHeight, PANEL_MIN, PANEL_MAX) : 0f;
            var upperH = body.height - panelH - (_showQuitPanel ? SPLITTER : 0f);

            var graphRect = new Rect(body.x, body.y, body.width - SIDEBAR_WIDTH, upperH);
            var sideRect = new Rect(body.xMax - SIDEBAR_WIDTH, body.y, SIDEBAR_WIDTH, upperH);

            HandleInput(graphRect);
            DrawGraph(graphRect);
            DrawSidebar(sideRect);

            if (!_showQuitPanel) return;

            var splitRect = new Rect(body.x, graphRect.yMax, body.width, SPLITTER);
            HandleSplitter(splitRect, body);

            _quitPanel.Draw(new Rect(body.x, splitRect.yMax, body.width, panelH));
        }

        private void HandleSplitter(Rect rect, Rect body)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.13f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

            var e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _draggingSplitter = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingSplitter)
            {
                _panelHeight = Mathf.Clamp(body.yMax - e.mousePosition.y, PANEL_MIN, PANEL_MAX);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && _draggingSplitter)
            {
                _draggingSplitter = false;
                e.Use();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    Rebuild();

                EditorGUI.BeginChangeCheck();
                _liveMode = GUILayout.Toggle(_liveMode, "Live", EditorStyles.toolbarButton,
                                             GUILayout.Width(46f));
                if (EditorGUI.EndChangeCheck()) Rebuild();

                if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                    FrameAll();

                GUILayout.Space(8f);
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(36f));
                _zoom = GUILayout.HorizontalSlider(_zoom, MIN_ZOOM, MAX_ZOOM, GUILayout.Width(90f));
                
                GUILayout.Space(8f);

                var quitting = Application.isPlaying && ApplicationQuitPipeline.IsQuitting;
                var qc = GUI.color;
                if (quitting) GUI.color = new Color(1f, 0.7f, 0.35f);
                _showQuitPanel = GUILayout.Toggle(_showQuitPanel,
                    quitting ? $"Quit ▸ {ApplicationQuitPipeline.Phase}" : "Quit",
                    EditorStyles.toolbarButton, GUILayout.Width(quitting ? 170f : 46f));
                GUI.color = qc;

                GUILayout.FlexibleSpace();

                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField,
                                              GUILayout.Width(180f));

                var issueCount = _data?.Issues.Count ?? 0;
                var c = GUI.color;
                if (issueCount > 0) GUI.color = new Color(1f, 0.6f, 0.4f);
                _showIssues = GUILayout.Toggle(_showIssues, $"Issues ({issueCount})",
                                               EditorStyles.toolbarButton, GUILayout.Width(90f));
                GUI.color = c;

                GUILayout.Label(_data is { IsLive: true } ? "runtime" : "static",
                                EditorStyles.miniLabel, GUILayout.Width(48f));
            }
        }

        private void FrameAll()
        {
            if (_data == null) return;
            _zoom = 1f;
            _pan = new Vector2(10f, 10f);
        }

        private void HandleInput(Rect graphRect)
        {
            var e = Event.current;
            if (!graphRect.Contains(e.mousePosition)) return;

            if (e.type == EventType.ScrollWheel)
            {
                var before = ScreenToWorld(e.mousePosition, graphRect);
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.035f), MIN_ZOOM, MAX_ZOOM);
                var after = ScreenToWorld(e.mousePosition, graphRect);
                _pan += (after - before) * _zoom;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag &&
                     (e.button == 2 || (e.button == 0 && e.alt)))
            {
                _pan += e.delta;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                var world = ScreenToWorld(e.mousePosition, graphRect);
                GraphNode hit = null;
                foreach (var n in _data.Nodes)
                    if (n.Rect.Contains(world)) { hit = n; break; }

                _selected = hit;
                RecalculateHighlight();

                if (hit != null && e.clickCount == 2)
                    ScriptLocator.Open(hit.Id);
                
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ContextClick)
            {
                var world = ScreenToWorld(e.mousePosition, graphRect);
                GraphNode hit = null;
                foreach (var n in _data.Nodes)
                    if (n.Rect.Contains(world)) { hit = n; break; }

                if (hit != null)
                {
                    SelectNode(hit);
                    ShowNodeMenu(hit);
                    e.Use();
                }
            }
        }
        
        void ShowNodeMenu(GraphNode node)
        {
            var menu = new GenericMenu();
            var id = node.Id;

            menu.AddItem(new GUIContent("Ping Script"), false, () => ScriptLocator.Ping(id));
            menu.AddItem(new GUIContent("Open Script"), false, () => ScriptLocator.Open(id));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy Type Name"), false,
                () => EditorGUIUtility.systemCopyBuffer = id.FullName);
            menu.AddItem(new GUIContent("Copy DependsOn Snippet"), false, () =>
                EditorGUIUtility.systemCopyBuffer =
                    $"DependsOn = new[] {{ typeof({id.Name}) }}");
            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"Origin: {ScriptLocator.DescribeOrigin(id)}"), false, null);

            menu.ShowAsContext();
        }

        private Vector2 ScreenToWorld(Vector2 screen, Rect graphRect)
            => (screen - graphRect.position - _pan) / _zoom;

        private Rect WorldToScreen(Rect world, Rect graphRect)
            => new(world.x * _zoom + _pan.x + graphRect.x,
                        world.y * _zoom + _pan.y + graphRect.y,
                        world.width * _zoom,
                        world.height * _zoom);

        private void RecalculateHighlight()
        {
            _highlightUp.Clear();
            _highlightDown.Clear();
            if (_selected == null) return;

            var byId = new Dictionary<Type, GraphNode>();
            foreach (var n in _data.Nodes) byId[n.Id] = n;

            void Up(GraphNode n)
            {
                foreach (var d in n.DependsOn)
                    if (d != null && _highlightUp.Add(d) && byId.TryGetValue(d, out var dep)) Up(dep);
            }
            void Down(GraphNode n)
            {
                foreach (var dep in n.Dependents)
                    if (_highlightDown.Add(dep.Id)) Down(dep);
            }

            Up(_selected);
            Down(_selected);
        }

        // ── Draw ────────────────────────────────────────────────────────────

        private void DrawGraph(Rect graphRect)
        {
            EditorGUI.DrawRect(graphRect, new Color(0.16f, 0.16f, 0.17f));
            GUI.BeginClip(graphRect);
            var local = new Rect(0f, 0f, graphRect.width, graphRect.height);

            DrawGrid(local);

            // Phase bands
            foreach (var band in _data.Bands)
            {
                var r = WorldToScreen(band.Rect, local);
                EditorGUI.DrawRect(r, PhaseColor(band.Phase) * new Color(1f, 1f, 1f, 0.07f));

                var header = new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 20f);
                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = Mathf.RoundToInt(Mathf.Lerp(9f, 14f, Mathf.InverseLerp(MIN_ZOOM, MAX_ZOOM, _zoom))),
                    normal = { textColor = PhaseColor(band.Phase) }
                };
                GUI.Label(header, $"{band.Phase}  ({band.Count})  ·  {PhaseHint(band.Phase)}", style);
            }

            // Edges — two passes so highlighted edges are never buried.
            Handles.BeginGUI();

            var highlighted = new List<(GraphNode from, GraphNode to)>();

            foreach (var n in _data.Nodes)
            {
                foreach (var d in n.DependsOn)
                {
                    if (d == null) continue;

                    var dep = _data.Nodes.Find(x => x.Id == d);
                    if (dep == null) continue;

                    if (IsEdgeHighlighted(dep, n)) highlighted.Add((dep, n));
                    else DrawEdge(dep, n, local);
                }
            }

            foreach (var (from, to) in highlighted) DrawEdge(from, to, local);

            Handles.EndGUI();

            // Nodes
            foreach (var n in _data.Nodes) DrawNode(n, local);

            GUI.EndClip();
        }
        
        private bool IsEdgeHighlighted(GraphNode from, GraphNode to)
        {
            if (_data.CycleMembers.Contains(from.Id) && _data.CycleMembers.Contains(to.Id))
                return true;

            if (_selected == null) return false;

            return IsUpstreamEdge(from, to) || IsDownstreamEdge(from, to);
        }

        private bool IsUpstreamEdge(GraphNode from, GraphNode to)
            => _highlightUp.Contains(from.Id) &&
               (to.Id == _selected.Id || _highlightUp.Contains(to.Id));

        private bool IsDownstreamEdge(GraphNode from, GraphNode to)
            => _highlightDown.Contains(to.Id) &&
               (from.Id == _selected.Id || _highlightDown.Contains(from.Id));

        private void DrawGrid(Rect rect)
        {
            const float step = 24f;
            var spacing = step * _zoom;
            if (spacing < 6f) return;

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.035f);
            for (var x = _pan.x % spacing; x < rect.width; x += spacing)
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, rect.height));
            for (var y = _pan.y % spacing; y < rect.height; y += spacing)
                Handles.DrawLine(new Vector3(0f, y), new Vector3(rect.width, y));
            Handles.EndGUI();
        }

        private void DrawEdge(GraphNode from, GraphNode to, Rect local)
        {
            var a = WorldToScreen(from.Rect, local);
            var b = WorldToScreen(to.Rect, local);

            var start = new Vector3(a.xMax, a.center.y);
            var end = new Vector3(b.xMin, b.center.y);

            // Cull off-screen edges before any curve work.
            var span = Rect.MinMaxRect(
                Mathf.Min(start.x, end.x) - 4f, Mathf.Min(start.y, end.y) - 4f,
                Mathf.Max(start.x, end.x) + 4f, Mathf.Max(start.y, end.y) + 4f);
            if (!span.Overlaps(local)) return;

            var tangent = Mathf.Max(40f, Mathf.Abs(end.x - start.x) * 0.5f);
            var c0 = start + Vector3.right * tangent;
            var c1 = end + Vector3.left * tangent;

            Color color;
            bool emphasise;

            if (_data.CycleMembers.Contains(from.Id) && _data.CycleMembers.Contains(to.Id))
            {
                color = edgeCycle;
                emphasise = true;
            }
            else if (_selected == null)
            {
                color = edgeNormal;
                emphasise = false;
            }
            else if (IsUpstreamEdge(from, to))
            {
                color = edgeUpstream;
                emphasise = true;
            }
            else if (IsDownstreamEdge(from, to))
            {
                color = edgeDownstrm;
                emphasise = true;
            }
            else
            {
                color = edgeMuted;
                emphasise = false;
            }

            // Zoom-scaled, floored so lines never thin out to nothing.
            var scale = Mathf.Clamp(_zoom, 0.7f, 1.3f);
            var width = (emphasise ? EDGE_WIDTH_HI : EDGE_WIDTH) * scale;

            // Dark halo underneath separates the line from grid and bands.
            if (emphasise || _selected == null)
                Handles.DrawBezier(start, end, c0, c1, edgeHalo, null, width + EDGE_HALO * scale);

            Handles.DrawBezier(start, end, c0, c1, color, null, width);

            DrawArrowHead(end, c1, color, emphasise, scale);
        }

        private static void DrawArrowHead(Vector3 tip, Vector3 control, Color color,
                                          bool emphasise, float scale)
        {
            var dir = (tip - control).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;

            var size = ARROW_SIZE * scale * (emphasise ? 1.15f : 1f);
            var normal = new Vector3(-dir.y, dir.x, 0f);
            var back = tip - dir * size;

            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                tip,
                back + normal * (size * 0.45f),
                back - normal * (size * 0.45f));
        }

        private void DrawNode(GraphNode n, Rect local)
        {
            var r = WorldToScreen(n.Rect, local);
            if (!r.Overlaps(local)) return;

            var dimmed = !string.IsNullOrEmpty(_search) &&
                         n.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0;

            var accent = StateColor(n.State);
            var bg = new Color(0.22f, 0.23f, 0.25f, dimmed ? 0.35f : 1f);

            EditorGUI.DrawRect(r, bg);
            EditorGUI.DrawRect(new Rect(r.x, r.y, ACCENT_WIDTH * _zoom, r.height), accent);

            DrawQuitBadges(n, r);

            var selected = _selected != null && _selected.Id == n.Id;
            var inCycle = _data.CycleMembers.Contains(n.Id);
            var inert = n.IsQuitBlocker && n.State == ModuleState.Ready && !n.BlockerRegistered;
            var notOptedIn = !n.AssemblyOptedIn && n.State == ModuleState.Declared;

            Color outline;
            float outlineW;

            if (selected) { outline = Color.white; outlineW = 2f; }
            else if (inCycle) { outline = new Color(1f, 0.3f, 0.25f); outlineW = 2f; }
            else if (notOptedIn || inert) { outline = new Color(1f, 0.35f, 0.2f); outlineW = 2f; }
            else { outline = new Color(0f, 0f, 0f, 0.6f); outlineW = 1f; }

            DrawOutline(r, outline, outlineW);

            if (_zoom < 0.5f) return;

            var fs = Mathf.RoundToInt(11f * Mathf.Clamp(_zoom, 0.6f, 1.2f));
            var nodeTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = fs,
                normal = { textColor = dimmed ? Color.gray : Color.white },
                clipping = TextClipping.Clip,
            };
            var sub = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Mathf.Max(9, fs - 2),
                normal = { textColor = new Color(0.7f, 0.7f, 0.72f) },
                clipping = TextClipping.Clip,
            };

            var pad = 8f * _zoom;
            var x = r.x + pad + ACCENT_WIDTH * _zoom;
            var w = r.width - pad * 2f - ACCENT_WIDTH * _zoom - QUIT_BADGE_WIDTH * _zoom;

            GUI.Label(new Rect(x, r.y + 4f * _zoom, w, 18f * _zoom), n.DisplayName, nodeTitle);

            var line2 = n.SortIndex >= 0
                ? $"#{n.SortIndex:00}  {n.State}  {n.InitMilliseconds:0.00} ms"
                : $"{n.State}   order {n.Order}";
            GUI.Label(new Rect(x, r.y + 22f * _zoom, w, 16f * _zoom), line2, sub);

            // Row 3: tags.
            var tags = string.Empty;
            if (n.IsAsync) tags += "async  ";
            if (n.IsQuitBlocker) tags += n.BlockerIsBusy ? "BUSY  " : "blocker  ";
            if (n.IsQuitHandler) tags += n.HasQuitOrder ? $"quit[{n.QuitOrder}]  " : "quit  ";
            if (n.LiveOnly) tags += "manual  ";
            if (!n.AutoRegister && !n.LiveOnly) tags += "no-autoreg  ";

            if (tags.Length > 0)
                GUI.Label(new Rect(x, r.y + 38f * _zoom, w, 16f * _zoom), tags, sub);

            // Row 4: warnings — previously collided with the tag row.
            var warning = notOptedIn ? "⚠ assembly not opted in"
                        : inert ? "⚠ blocker not registered"
                        : null;

            if (warning == null) return;

            var warn = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Mathf.Max(9, fs - 2),
                normal = { textColor = new Color(1f, 0.45f, 0.3f) },
                clipping = TextClipping.Clip,
            };
            GUI.Label(new Rect(x, r.y + 54f * _zoom, w, 16f * _zoom), warning, warn);
        }

        private void DrawQuitBadges(GraphNode n, Rect r)
        {
            var segments = new List<Color>(2);

            if (n.IsQuitHandler) segments.Add(new Color(0.85f, 0.6f, 1f));

            if (n.IsQuitBlocker)
            {
                if (n.BlockerIsBusy) segments.Add(new Color(1f, 0.65f, 0.25f));
                else if (n.BlockerRegistered || n.State == ModuleState.Declared)
                    segments.Add(new Color(0.35f, 0.75f, 0.72f));
                else segments.Add(new Color(0.5f, 0.5f, 0.52f));
            }

            if (segments.Count == 0) return;

            var w = QUIT_BADGE_WIDTH * _zoom;
            var h = r.height / segments.Count;

            for (var i = 0; i < segments.Count; i++)
                EditorGUI.DrawRect(new Rect(r.xMax - w, r.y + h * i, w, h), segments[i]);
        }

        private static void DrawOutline(Rect r, Color c, float w)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        // ── sidebar ────────────────────────────────────────────────────────────

        private void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.21f, 0.21f, 0.22f));
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f));

            if (_showIssues && _data.Issues.Count > 0)
            {
                EditorGUILayout.LabelField($"Issues ({_data.Issues.Count})", EditorStyles.boldLabel);
                _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.MaxHeight(160f));
                foreach (var issue in _data.Issues)
                    EditorGUILayout.HelpBox(issue, MessageType.Warning);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(6f);
            }

            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);

            if (_selected != null)
            {
                if (_selected.IsQuitBlocker || _selected.IsQuitHandler)
                {
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField("Quit Pipeline", EditorStyles.boldLabel);

                    if (_selected.IsQuitHandler)
                    {
                        Row("Handler", "IQuitHandler");
                        Row("Quit order", _selected.HasQuitOrder
                            ? _selected.QuitOrder.ToString()
                            : "0  (reverse init)");
                    }

                    if (_selected.IsQuitBlocker)
                    {
                        Row("Blocker", "IQuitBlocker");

                        if (Application.isPlaying)
                        {
                            Row("Registered", _selected.BlockerRegistered ? "yes" : "NO");
                            Row("Busy", _selected.BlockerIsBusy ? "YES — blocking" : "idle");

                            if (!string.IsNullOrEmpty(_selected.BlockerReason))
                                EditorGUILayout.LabelField(_selected.BlockerReason,
                                    EditorStyles.wordWrappedMiniLabel);

                            if (!_selected.BlockerRegistered && _selected.State == ModuleState.Ready)
                                EditorGUILayout.HelpBox(
                                    "Implements IQuitBlocker but AddBlocker() was never called. " +
                                    "Call it from Initialize().", MessageType.Error);
                            else if (!_selected.BlockerIsBusy)
                                EditorGUILayout.HelpBox(
                                    "Registered but idle. It will not hold a quit until IsBusy returns true.",
                                    MessageType.Info);
                        }
                        else
                        {
                            Row("Registered", "(play mode only)");
                        }
                    }
                }
            }

            if (_selected == null)
            {
                EditorGUILayout.HelpBox(
                    "Click a node to inspect.\n\n" +
                    "Drag: middle mouse / alt+left\n" +
                    "Zoom: scroll wheel\n" +
                    "Double click: open script\n" +
                    "Right click: context menu",
                    MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            EditorGUILayout.LabelField(_selected.DisplayName, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(ScriptLocator.DescribeOrigin(_selected.Id), EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            Row("Phase", _selected.Phase.ToString());
            Row("Order", _selected.Order.ToString());
            Row("Async", _selected.IsAsync ? "yes" : "no");
            Row("State", _selected.State.ToString());
            if (_selected.SortIndex >= 0) Row("Exec index", _selected.SortIndex.ToString("00"));
            if (_selected.InitMilliseconds > 0d) Row("Init time", $"{_selected.InitMilliseconds:0.000} ms");

            if (!string.IsNullOrEmpty(_selected.Error))
                EditorGUILayout.HelpBox(_selected.Error, MessageType.Error);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Depends On ({_selected.DependsOn.Length})", EditorStyles.boldLabel);
            foreach (var d in _selected.DependsOn)
            {
                if (d == null) continue;
                if (GUILayout.Button(d.Name, EditorStyles.miniButton)) SelectById(d);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Dependents ({_selected.Dependents.Count})", EditorStyles.boldLabel);
            foreach (var dep in _selected.Dependents)
                if (GUILayout.Button(dep.DisplayName, EditorStyles.miniButton)) SelectNode(dep);

            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping")) ScriptLocator.Ping(_selected.Id);
                if (GUILayout.Button("Open")) ScriptLocator.Open(_selected.Id);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            void Row(string k, string v)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(k, GUILayout.Width(88f));
                    EditorGUILayout.LabelField(v, EditorStyles.miniLabel);
                }
            }
        }

        private void SelectById(Type id)
        {
            var n = _data.Nodes.Find(x => x.Id == id);
            if (n != null) SelectNode(n);
        }

        private void SelectNode(GraphNode n)
        {
            _selected = n;
            RecalculateHighlight();
            Repaint();
        }

        private static void PingType(Type t) => ScriptLocator.Ping(t);

        private static Color PhaseColor(ModulePhase p)
        {
            switch (p)
            {
                case ModulePhase.Core: return new Color(0.45f, 0.72f, 1f);
                case ModulePhase.Runtime: return new Color(0.55f, 0.95f, 0.6f);
                case ModulePhase.Scene: return new Color(1f, 0.78f, 0.4f);
                default: return new Color(0.85f, 0.6f, 1f);
            }
        }

        private static string PhaseHint(ModulePhase p)
        {
            switch (p)
            {
                case ModulePhase.Core: return "AfterAssembliesLoaded";
                case ModulePhase.Runtime: return "BeforeSceneLoad";
                case ModulePhase.Scene: return "AfterSceneLoad";
                default: return "AfterSceneLoad (late)";
            }
        }

        private static Color StateColor(ModuleState s)
        {
            switch (s)
            {
                case ModuleState.Ready: return new Color(0.35f, 0.85f, 0.45f);
                case ModuleState.Initializing: return new Color(1f, 0.85f, 0.3f);
                case ModuleState.Failed: return new Color(0.95f, 0.35f, 0.3f);
                case ModuleState.Skipped: return new Color(1f, 0.6f, 0.25f);
                case ModuleState.ShutDown: return new Color(0.45f, 0.45f, 0.48f);
                case ModuleState.Registered: return new Color(0.55f, 0.7f, 0.95f);
                default: return new Color(0.4f, 0.4f, 0.44f);
            }
        }
    }
}
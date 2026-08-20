using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Node-based, read-only diagnostic view of the application quit pipeline, styled after the
    /// Initialization / Player Loop graphs (pan / zoom / select).
    /// <para>
    /// <b>Bands</b> are the two pipeline stages: <c>Blockers</c> (gates that must report not-busy before
    /// handlers run) and <c>Handlers</c> (the ordered <see cref="IQuitHandler"/> plan). Blockers are shown
    /// only in play mode or when a finished result is retained.
    /// </para>
    /// <para>
    /// <b>Nodes</b> are individual blockers / handler steps. A handler node's accent reflects its live state
    /// (pending / running / done / failed / …) via <see cref="QuitPanel.StepColor"/>; a blocker node is
    /// orange while busy and grey otherwise. Double-clicking a handler node opens the declaring script.
    /// </para>
    /// </summary>
    internal sealed class QuitPipelineWindow : EditorWindow
    {
        private const float MIN_ZOOM = 0.35f;
        private const float MAX_ZOOM = 1.6f;
        private const float SIDEBAR_WIDTH = 320f;
        private const float ACCENT_WIDTH = 8f;

        private const float NODE_WIDTH = 240f;
        private const float NODE_HEIGHT = 56f;
        private const float ROW_GAP = 18f;
        private const float COLUMN_GAP = 70f;
        private const float BAND_PADDING = 16f;
        private const float HEADER_HEIGHT = 30f;
        private const float EMPTY_BAND_HEIGHT = 70f;
        private const float ORIGIN_X = 40f;
        private const float ORIGIN_Y = 70f;

        private static readonly Color BackColor = new(0.16f, 0.16f, 0.17f);
        private static readonly Color NodeBg = new(0.22f, 0.23f, 0.25f);
        private static readonly Color BlockerBusyColor = new(1f, 0.65f, 0.25f);
        private static readonly Color BlockerIdleColor = new(0.42f, 0.45f, 0.48f);
        private static readonly Color ErrorColor = new(0.95f, 0.35f, 0.3f);

        private enum NodeKind { Blocker, Handler }

        // ── model (rebuilt each repaint; cheap) ─────────────────────────────

        private sealed class GraphNode
        {
            public Rect Rect;
            public NodeKind Kind;
            public string Title;
            public string SubLabel;
            public Color Accent;

            // Resolved declaring type (blockers store their live instance type here so
            // Open/Ping Script never depends on fragile name resolution).
            public Type NodeType;

            // handler
            public int StepIndex;
            public ApplicationQuitPipeline.QuitStepState State;
            public int Order;
            public bool FromModule;
            public double Milliseconds;

            // blocker
            public bool IsBusy;
            public string Reason;

            // Number of collapsed same-type instances (blockers). 1 unless duplicates exist.
            public int Count = 1;
        }

        private sealed class Band
        {
            public string Title;
            public string SubLabel;
            public Color Color;
            public Rect Rect;
            public readonly List<GraphNode> Nodes = new();
        }

        private readonly List<Band> _bands = new();
        private readonly List<GraphNode> _nodes = new();

        private const string SEARCH_CONTROL = "QuitPipelineGraphSearch";
        private string _search = string.Empty;
        private bool _showReasons = true;

        private Vector2 _pan = Vector2.zero;
        private float _zoom = 1f;
        private GraphNode _selected;
        private Vector2 _inspectorScroll;
        private double _lastRepaint;

        // Cross-window focus request (e.g. from the Initialization Graph's IQuitHandler context menu).
        private string _pendingFocus;

        [MenuItem("Tools/AceLand/Lifecycle/Quit Pipeline Graph", priority = 12)]
        public static void Open()
        {
            var w = GetWindow<QuitPipelineWindow>();
            w.titleContent = new GUIContent("Quit Pipeline Graph");
            w.minSize = new Vector2(720f, 480f);
            w.Show();
        }

        /// <summary>
        /// Opens the Quit Pipeline Graph and focuses the handler step matching <paramref name="handlerName"/>
        /// (case-insensitive). Used by the Initialization Graph's "View in Quit Pipeline" action. If no match
        /// exists yet (e.g. plan not built), the request is retried on the next rebuild.
        /// </summary>
        public static void FocusStep(string handlerName)
        {
            var w = GetWindow<QuitPipelineWindow>();
            w.titleContent = new GUIContent("Quit Pipeline Graph");
            w.minSize = new Vector2(720f, 480f);
            w.Show();
            w.Focus();

            w._search = string.Empty;
            w._pendingFocus = handlerName;
            w.Repaint();
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

        // ── build ──────────────────────────────────────────────────────────

        private void Rebuild()
        {
            _bands.Clear();
            _nodes.Clear();

            // Blockers band (play mode / retained result only).
            var showBlockers = Application.isPlaying || ApplicationQuitPipeline.HasResult;
            var blockers = showBlockers
                ? ApplicationQuitPipeline.GetBlockers()
                : (IReadOnlyList<ApplicationQuitPipeline.QuitBlockerInfo>)
                  Array.Empty<ApplicationQuitPipeline.QuitBlockerInfo>();

            var busy = 0;
            foreach (var b in blockers) if (b.IsBusy) busy++;

            var blockerBand = new Band
            {
                Title = "Blockers",
                SubLabel = showBlockers
                    ? (busy > 0 ? $"{blockers.Count} · {busy} busy" : $"{blockers.Count} registered")
                    : "play mode only",
                Color = busy > 0 ? BlockerBusyColor : BlockerIdleColor,
            };

            // Collapse blockers emitted by the same script (same runtime type) into a single
            // node labelled "Name ×N" (per the "title ×3" convention). This keeps duplicates
            // individually addressable/selectable and lets the node carry its real Type so the
            // Open/Ping Script actions work without fragile name resolution.
            foreach (var b in blockers)
            {
                var type = b.Blocker != null ? b.Blocker.GetType() : ResolveType(b.Name);

                // Merge into an existing node of the same type.
                GraphNode existing = null;
                foreach (var n in blockerBand.Nodes)
                {
                    var sameType = type != null ? n.NodeType == type : n.Title == b.Name;
                    if (sameType) { existing = n; break; }
                }

                if (existing != null)
                {
                    existing.Count++;
                    existing.Title = $"{b.Name} ×{existing.Count}";
                    if (b.IsBusy)
                    {
                        existing.IsBusy = true;
                        existing.Accent = BlockerBusyColor;
                        existing.SubLabel = "busy";
                    }
                    if (!string.IsNullOrEmpty(b.Reason))
                        existing.Reason = string.IsNullOrEmpty(existing.Reason)
                            ? b.Reason
                            : existing.Reason + "\n" + b.Reason;
                    continue;
                }

                var node = new GraphNode
                {
                    Kind = NodeKind.Blocker,
                    Title = b.Name,
                    SubLabel = b.IsBusy ? "busy" : "idle",
                    Accent = b.IsBusy ? BlockerBusyColor : BlockerIdleColor,
                    IsBusy = b.IsBusy,
                    Reason = b.Reason,
                    NodeType = type,
                    Count = 1,
                };
                blockerBand.Nodes.Add(node);
                _nodes.Add(node);
            }

            _bands.Add(blockerBand);

            // Handlers band (always available as a plan preview).
            var steps = ApplicationQuitPipeline.GetSteps();
            var active = ApplicationQuitPipeline.IsActive;
            var suffix = active ? "running"
                : ApplicationQuitPipeline.HasResult ? "finished" : "plan";

            var handlerBand = new Band
            {
                Title = "Handlers",
                SubLabel = $"{steps.Count} · {suffix}",
                Color = QuitPanel.PhaseColor(ApplicationQuitPipeline.Phase),
            };

            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var sub = $"{i:00}";
                if (s.Order != 0) sub += $"  ·  [{s.Order}]";
                sub += s.FromModule ? "  ·  module" : "  ·  manual";
                if (s.Milliseconds > 0d) sub += $"  ·  {s.Milliseconds:0} ms";

                var node = new GraphNode
                {
                    Kind = NodeKind.Handler,
                    Title = s.Name,
                    SubLabel = sub,
                    Accent = QuitPanel.StepColor(s.State),
                    StepIndex = i,
                    State = s.State,
                    Order = s.Order,
                    FromModule = s.FromModule,
                    Milliseconds = s.Milliseconds,
                    NodeType = ResolveType(s.Name),
                };
                handlerBand.Nodes.Add(node);
                _nodes.Add(node);
            }

            _bands.Add(handlerBand);

            Layout();

            // Preserve selection across rebuilds by a stable identity.
            // Blockers are collapsed per-type so (kind, type) is unique; handlers use their
            // step index (stable within a plan) with title as a fallback.
            if (_selected != null)
            {
                var prev = _selected;
                _selected = _nodes.Find(n =>
                {
                    if (n.Kind != prev.Kind) return false;
                    if (prev.Kind == NodeKind.Handler)
                        return n.StepIndex == prev.StepIndex && n.Title == prev.Title;
                    return prev.NodeType != null
                        ? n.NodeType == prev.NodeType
                        : n.Title == prev.Title;
                });
            }
        }

        /// <summary>
        /// Column-per-band layout: each stage is a vertical band of its nodes.
        /// Filter-aware — non-visible bands/nodes are skipped so visible content
        /// packs contiguously with no gaps left by the search filter.
        /// </summary>
        private void Layout()
        {
            var x = ORIGIN_X;
            var maxY = ORIGIN_Y;

            foreach (var band in _bands)
            {
                if (!BandVisible(band))
                    continue;

                var y = ORIGIN_Y;
                var visibleCount = 0;
                foreach (var node in band.Nodes)
                {
                    if (!NodeVisible(node))
                        continue;

                    node.Rect = new Rect(x, y, NODE_WIDTH, NODE_HEIGHT);
                    y += NODE_HEIGHT + ROW_GAP;
                    visibleCount++;
                }

                var contentBottom = visibleCount > 0 ? y - ROW_GAP : ORIGIN_Y + EMPTY_BAND_HEIGHT;
                maxY = Mathf.Max(maxY, contentBottom);

                band.Rect = new Rect(
                    x - BAND_PADDING,
                    ORIGIN_Y - HEADER_HEIGHT - BAND_PADDING,
                    NODE_WIDTH + BAND_PADDING * 2f,
                    0f); // height unified below

                x += NODE_WIDTH + BAND_PADDING * 2f + COLUMN_GAP;
            }

            var bandHeight = maxY - (ORIGIN_Y - HEADER_HEIGHT - BAND_PADDING) + BAND_PADDING;
            foreach (var band in _bands)
            {
                var r = band.Rect;
                r.height = bandHeight;
                band.Rect = r;
            }
        }

        // ── GUI ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            Rebuild();

            DrawToolbar();

            var body = new Rect(0f, EditorStyles.toolbar.fixedHeight,
                position.width, position.height - EditorStyles.toolbar.fixedHeight);

            var graphRect = new Rect(body.x, body.y, body.width - SIDEBAR_WIDTH, body.height);
            var sideRect = new Rect(body.xMax - SIDEBAR_WIDTH, body.y, SIDEBAR_WIDTH, body.height);

            ResolvePendingFocus(graphRect);

            HandleInput(graphRect);
            DrawGraph(graphRect);
            DrawSidebar(sideRect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var phase = ApplicationQuitPipeline.Phase;
                var c = GUI.contentColor;
                GUI.contentColor = QuitPanel.PhaseColor(phase);
                GUILayout.Label(phase.ToString(), EditorStyles.toolbarButton, GUILayout.Width(130f));
                GUI.contentColor = c;

                if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                    FrameAll();

                GUILayout.Space(8f);
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(36f));
                _zoom = GUILayout.HorizontalSlider(_zoom, MIN_ZOOM, MAX_ZOOM, GUILayout.Width(90f));

                GUILayout.Space(8f);
                _showReasons = GUILayout.Toggle(_showReasons, "Reasons",
                    EditorStyles.toolbarButton, GUILayout.Width(64f));

                GUILayout.FlexibleSpace();

                if (GraphSearchField.Draw(180f, SEARCH_CONTROL, ref _search))
                    Repaint();

                using (new EditorGUI.DisabledScope(!Application.isPlaying || ApplicationQuitPipeline.IsQuitting))
                {
                    if (GUILayout.Button("Simulate Quit", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                        ApplicationQuitPipeline.Quit();
                }

                using (new EditorGUI.DisabledScope(Application.isPlaying || !ApplicationQuitPipeline.HasResult))
                {
                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                        ApplicationQuitPipeline.ClearDiagnostics();
                }

                GUILayout.Space(8f);
                GUILayout.Label(Application.isPlaying ? "Play Mode" : "Edit Mode", EditorStyles.miniLabel);
            }
        }

        private void FrameAll()
        {
            _zoom = 1f;
            _pan = new Vector2(10f, 10f);
        }

        // ── focus ──────────────────────────────────────────────────────────

        private void ResolvePendingFocus(Rect graphRect)
        {
            if (string.IsNullOrEmpty(_pendingFocus)) return;

            GraphNode match = null;
            foreach (var n in _nodes)
            {
                if (n.Kind != NodeKind.Handler) continue;
                if (string.Equals(n.Title, _pendingFocus, StringComparison.OrdinalIgnoreCase))
                {
                    match = n;
                    break;
                }
            }

            if (match == null)
            {
                // Plan may not be built yet (edit mode, no modules started). Keep the request pending; a
                // later rebuild can still resolve it. Give up silently if the graph is otherwise populated.
                if (_nodes.Count > 0) _pendingFocus = null;
                return;
            }

            _selected = match;
            FocusOn(match, graphRect);
            _pendingFocus = null;
        }

        private void FocusOn(GraphNode node, Rect graphRect)
        {
            _zoom = 1f;
            var center = node.Rect.center;
            _pan = new Vector2(
                graphRect.width * 0.5f - center.x * _zoom,
                graphRect.height * 0.5f - center.y * _zoom);
        }

        // ── input ──────────────────────────────────────────────────────────

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
                var hit = NodeAt(ScreenToWorld(e.mousePosition, graphRect));
                _selected = hit;

                // Double-click opens the backing script for any node (handler or blocker).
                if (hit != null && e.clickCount == 2)
                    OpenHandlerScript(hit);

                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ContextClick)
            {
                var hit = NodeAt(ScreenToWorld(e.mousePosition, graphRect));
                if (hit != null)
                {
                    _selected = hit;
                    ShowNodeMenu(hit);
                    e.Use();
                }
            }
        }

        private GraphNode NodeAt(Vector2 world)
        {
            foreach (var n in _nodes)
                if (NodeVisible(n) && n.Rect.Contains(world)) return n;
            return null;
        }

        private bool NodeVisible(GraphNode n) => GraphSearchField.Matches(_search, n.Title, n.SubLabel);

        private bool BandVisible(Band b)
        {
            // With an active search, only surface bands that host at least one matching node.
            if (!string.IsNullOrEmpty(_search))
                return b.Nodes.Exists(NodeVisible);
            return true;
        }

        private static void OpenHandlerScript(GraphNode node)
        {
            var t = node.NodeType ?? ResolveType(node.Title);
            if (t != null) ScriptLocator.Open(t);
        }

        private void ShowNodeMenu(GraphNode node)
        {
            var menu = new GenericMenu();
            var t = node.NodeType ?? ResolveType(node.Title);

            if (node.Kind == NodeKind.Handler)
            {
                if (t != null)
                {
                    menu.AddItem(new GUIContent("Ping Script"), false, () => ScriptLocator.Ping(t));
                    menu.AddItem(new GUIContent("Open Script"), false, () => ScriptLocator.Open(t));
                    menu.AddSeparator("");
                }
                menu.AddItem(new GUIContent("View in Initialization Graph"), false,
                    () => DependencyGraphWindow.FocusNode(node.Title));
                menu.AddSeparator("");
            }
            else
            {
                if (t != null)
                {
                    menu.AddItem(new GUIContent("Ping Script"), false, () => ScriptLocator.Ping(t));
                    menu.AddItem(new GUIContent("Open Script"), false, () => ScriptLocator.Open(t));
                    menu.AddSeparator("");
                }
            }

            menu.AddItem(new GUIContent("Copy Name"), false,
                () => EditorGUIUtility.systemCopyBuffer = node.Title);

            menu.ShowAsContext();
        }

        // Name→Type resolution scans every assembly, so its result (including misses) is memoised.
        // Rebuild() runs each repaint; without this cache the graph re-scans the whole AppDomain
        // for every handler on every frame, which makes the window extremely sluggish.
        private static readonly Dictionary<string, Type> _typeCache = new();

        /// <summary>Best-effort resolve a handler / blocker display name back to a <see cref="Type"/>.</summary>
        private static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_typeCache.TryGetValue(name, out var cached)) return cached;

            Type found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                    if (t.Name == name || t.FullName == name) { found = t; break; }
                if (found != null) break;
            }

            _typeCache[name] = found;
            return found;
        }

        private Vector2 ScreenToWorld(Vector2 screen, Rect graphRect)
            => (screen - graphRect.position - _pan) / _zoom;

        private Rect WorldToScreen(Rect world, Rect graphRect)
            => new(world.x * _zoom + _pan.x + graphRect.x,
                   world.y * _zoom + _pan.y + graphRect.y,
                   world.width * _zoom,
                   world.height * _zoom);

        // ── draw ───────────────────────────────────────────────────────────

        private void DrawGraph(Rect graphRect)
        {
            EditorGUI.DrawRect(graphRect, BackColor);
            GUI.BeginClip(graphRect);
            var local = new Rect(0f, 0f, graphRect.width, graphRect.height);

            DrawGrid(local);

            var hasAnyNode = _nodes.Count > 0;
            if (!hasAnyNode)
            {
                var boxWidth = Mathf.Min(460f, local.width - 40f);
                var boxRect = new Rect(
                    (local.width - boxWidth) * 0.5f,
                    (local.height - 60f) * 0.5f,
                    boxWidth, 60f);
                EditorGUI.HelpBox(boxRect,
                    Application.isPlaying
                        ? "No blockers or IQuitHandler registered."
                        : "No IQuitHandler registered.\nEnter play mode to see live blocker state.",
                    MessageType.Info);
                GUI.EndClip();
                return;
            }

            foreach (var band in _bands)
            {
                if (!BandVisible(band)) continue;
                DrawBand(band, local);
            }

            foreach (var node in _nodes)
            {
                if (!NodeVisible(node)) continue;
                DrawNode(node, local);
            }

            GUI.EndClip();
        }

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

        private void DrawBand(Band band, Rect local)
        {
            var r = WorldToScreen(band.Rect, local);
            if (!r.Overlaps(local)) return;

            var color = band.Color;

            EditorGUI.DrawRect(r, color * new Color(1f, 1f, 1f, 0.07f));
            DrawOutline(r, color * new Color(1f, 1f, 1f, 0.5f), 1f);

            if (_zoom < 0.4f) return;

            var header = new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 20f);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Mathf.RoundToInt(Mathf.Lerp(9f, 13f, Mathf.InverseLerp(MIN_ZOOM, MAX_ZOOM, _zoom))),
                normal = { textColor = color },
            };
            GUI.Label(header, band.Title, style);

            var sub = new Rect(r.x + 8f, r.y + 4f + 16f, r.width - 16f, 16f);
            GUI.Label(sub, band.SubLabel, EditorStyles.miniLabel);

            if (band.Nodes.Count == 0)
            {
                var empty = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.62f) },
                };
                var er = new Rect(r.x, r.y + HEADER_HEIGHT * _zoom, r.width, 40f);
                GUI.Label(er, "none", empty);
            }
        }

        private void DrawNode(GraphNode n, Rect local)
        {
            var r = WorldToScreen(n.Rect, local);
            if (!r.Overlaps(local)) return;

            var accent = n.Accent;

            EditorGUI.DrawRect(r, NodeBg);
            EditorGUI.DrawRect(new Rect(r.x, r.y, ACCENT_WIDTH * _zoom, r.height), accent);

            var selected = _selected == n;
            var isError = n.Kind == NodeKind.Handler &&
                          (n.State == ApplicationQuitPipeline.QuitStepState.Failed ||
                           n.State == ApplicationQuitPipeline.QuitStepState.TimedOut);
            var outline = selected ? Color.white
                : isError ? ErrorColor
                : new Color(0f, 0f, 0f, 0.6f);
            DrawOutline(r, outline, selected ? 2f : 1f);

            if (_zoom < 0.5f) return;

            // Rows are laid out proportionally to the (zoomed) node height and clipped, so text
            // never spills past the bottom edge when zoomed out.
            var pad = ACCENT_WIDTH * _zoom + 6f;
            var innerX = r.x + pad;
            var innerW = r.width - pad - 4f;

            var title = new Rect(innerX, r.y + 5f * _zoom, innerW, 20f * _zoom);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
            GUI.Label(title, n.Title, titleStyle);

            var sub = new Rect(innerX, r.y + 24f * _zoom, innerW, 16f * _zoom);
            var subStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            GUI.Label(sub, n.SubLabel, subStyle);

            var stateLabel = n.Kind == NodeKind.Handler
                ? n.State.ToString().ToLowerInvariant()
                : (n.IsBusy ? "busy" : "idle");
            var stateRect = new Rect(innerX, r.y + 38f * _zoom, innerW, 16f * _zoom);
            var stateStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                { clipping = TextClipping.Clip, normal = { textColor = accent } };
            GUI.Label(stateRect, stateLabel, stateStyle);
        }

        private static void DrawOutline(Rect r, Color color, float w)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), color);
        }

        // ── sidebar ────────────────────────────────────────────────────────

        private void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.2f));

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            DrawStatusSummary();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            if (_selected == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a node to inspect it.\n\n" +
                    "Blockers = gates that must report not-busy before handlers run.\n" +
                    "Handlers = the ordered IQuitHandler plan.",
                    MessageType.Info);
            }
            else if (_selected.Kind == NodeKind.Handler)
            {
                var t = _selected.NodeType ?? ResolveType(_selected.Title);
                Row("Handler", _selected.Title);
                Row("Order", _selected.Order.ToString());
                Row("Source", _selected.FromModule ? "module" : "manual");
                Row("State", _selected.State.ToString());
                if (_selected.Milliseconds > 0d)
                    Row("Duration", $"{_selected.Milliseconds:0} ms");
                if (t != null)
                    Row("Origin", ScriptLocator.DescribeOrigin(t));

                EditorGUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(t == null))
                {
                    if (GUILayout.Button("Open Script")) ScriptLocator.Open(t);
                    if (GUILayout.Button("Ping Script")) ScriptLocator.Ping(t);
                }
            }
            else
            {
                var t = _selected.NodeType ?? ResolveType(_selected.Title);
                Row("Blocker", _selected.Title);
                if (_selected.Count > 1)
                    Row("Instances", _selected.Count.ToString());
                Row("Busy", _selected.IsBusy ? "yes" : "no");
                if (t != null)
                    Row("Origin", ScriptLocator.DescribeOrigin(t));
                if (_showReasons && !string.IsNullOrEmpty(_selected.Reason))
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Reason", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(_selected.Reason, EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(t == null))
                {
                    if (GUILayout.Button("Open Script")) ScriptLocator.Open(t);
                    if (GUILayout.Button("Ping Script")) ScriptLocator.Ping(t);
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawStatusSummary()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            Row("Phase", ApplicationQuitPipeline.Phase.ToString(),
                QuitPanel.PhaseColor(ApplicationQuitPipeline.Phase));
            Row("Quitting", ApplicationQuitPipeline.IsQuitting ? "yes" : "no");
            Row("Ready", ApplicationQuitPipeline.IsReadyToQuit ? "yes" : "no");

            if (ApplicationQuitPipeline.IsQuitting)
            {
                var done = ApplicationQuitPipeline.HasResult;
                Row(done ? "Total" : "Elapsed", $"{ApplicationQuitPipeline.ElapsedSeconds:0.0} s");
                if (!done)
                {
                    var rem = ApplicationQuitPipeline.RemainingSeconds;
                    Row("Remaining", rem < 0d ? "unlimited" : $"{rem:0.0} s");
                }
            }
            else
            {
                Row("Timeout", ApplicationQuitPipeline.TIMEOUT_SECONDS <= 0f
                    ? "unlimited"
                    : $"{ApplicationQuitPipeline.TIMEOUT_SECONDS:0} s");
            }

            if (!Application.isPlaying && ApplicationQuitPipeline.HasResult)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.HelpBox(
                    "Showing the last session's result. Statics survive Stop until the next domain reload.",
                    MessageType.Warning);
            }
        }

        private static void Row(string label, string value) => Row(label, value, Color.clear);

        private static void Row(string label, string value, Color valueColor)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(70f));

                if (valueColor == Color.clear)
                {
                    EditorGUILayout.SelectableLabel(value, EditorStyles.wordWrappedLabel,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
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

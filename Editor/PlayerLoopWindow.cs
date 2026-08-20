using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Node-based, read-only diagnostic view of the Lifecycle player-loop injection, styled after the
    /// Initialization Graph (pan / zoom / select / double-click-to-open).
    /// <para>
    /// <b>Groups</b> are the Lifecycle <see cref="PlayerLoopPoint"/>s Lifecycle injects: a group's header
    /// is coloured green when its node is present in the live player loop, red when it self-healed away.
    /// Groups are shown only while the driver is installed and hidden entirely when it is removed.
    /// </para>
    /// <para>
    /// <b>Nodes</b> are the user-injected frame processes scheduled at each point (via
    /// <see cref="LifecycleFrame"/>). A node's title is the scheduled method (<c>Type.Method</c>) and its
    /// accent colour reflects the coarse state: waiting (yellow), running (green) or error (red).
    /// Completed processes vanish immediately; errored processes linger a few seconds so the failure is
    /// visible. Double-clicking a node opens the declaring script.
    /// </para>
    /// <para>
    /// Deliberately has no install / remove controls: install state is driven by the lifecycle and the
    /// quit pipeline, and exposing manual toggles here would break that determinism.
    /// </para>
    /// </summary>
    internal sealed class PlayerLoopWindow : EditorWindow
    {
        private const float MIN_ZOOM = 0.35f;
        private const float MAX_ZOOM = 1.6f;
        private const float SIDEBAR_WIDTH = 280f;
        private const float ACCENT_WIDTH = 8f;

        private const float NODE_WIDTH = 210f;
        private const float NODE_HEIGHT = 56f;
        private const float ROW_GAP = 20f;
        private const float COLUMN_GAP = 20f;
        private const float BAND_PADDING = 16f;
        private const float HEADER_HEIGHT = 30f;
        private const float EMPTY_BAND_HEIGHT = 70f;
        private const float ORIGIN_X = 40f;
        private const float ORIGIN_Y = 70f;

        private static readonly Color BackColor = new(0.16f, 0.16f, 0.17f);
        private static readonly Color NodeBg = new(0.22f, 0.23f, 0.25f);
        private static readonly Color InstalledColor = new(0.35f, 0.85f, 0.45f); // green
        private static readonly Color RemovedColor = new(0.95f, 0.35f, 0.3f);    // red
        private static readonly Color WaitingColor = new(1f, 0.85f, 0.3f);       // yellow
        private static readonly Color RunningColor = new(0.35f, 0.85f, 0.45f);   // green
        private static readonly Color ErrorColor = new(0.95f, 0.35f, 0.3f);      // red

        // ── model (rebuilt each repaint; cheap) ─────────────────────────────

        private sealed class ProcessNode
        {
            public long Id;           // stable, handle-unique identity (survives rebuilds)
            public Rect Rect;
            public string Title;      // Type.Method
            public string SubLabel;   // kind + point
            public FrameProcessState State;
            public Type Owner;        // declaring root type, for open-script
            public string PointName;
            public string KindName;
        }

        private sealed class PointGroup
        {
            public PlayerLoopPoint Point;
            public bool Installed;
            public string ParentSegment;
            public Rect Rect;
            public readonly List<ProcessNode> Nodes = new();
        }

        private readonly List<PointGroup> _groups = new();
        private readonly List<ProcessNode> _nodes = new();
        private readonly List<FrameHandle> _snapshot = new();

        private bool _driverInstalled;

        private const string SEARCH_CONTROL = "PlayerLoopGraphSearch";
        private string _search = string.Empty;
        private bool _showEmptyPoints;

        private Vector2 _pan = Vector2.zero;
        private float _zoom = 1f;
        private ProcessNode _selected;
        private Vector2 _inspectorScroll;
        private double _lastRepaint;

        [MenuItem("Tools/AceLand/Lifecycle/Player Loop Graph", priority = 11)]
        public static void Open()
        {
            var w = GetWindow<PlayerLoopWindow>();
            w.titleContent = new GUIContent("Player Loop Graph");
            w.minSize = new Vector2(720f, 480f);
            w.Show();
        }

        private void OnEnable() => EditorApplication.update += Tick;
        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            // Frame processes churn quickly in play mode; keep a lively cadence there and a calm one in
            // edit mode where only install / remove / self-heal (rare) can change anything.
            var interval = Application.isPlaying ? 0.1 : 0.5;
            if (EditorApplication.timeSinceStartup - _lastRepaint < interval) return;
            _lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        // ── build ──────────────────────────────────────────────────────────

        private void Rebuild()
        {
            _groups.Clear();
            _nodes.Clear();

            _driverInstalled = LifecyclePlayerLoop.IsInstalled;
            if (!_driverInstalled)
            {
                _selected = null;
                return;
            }

            foreach (var installed in LifecyclePlayerLoop.EnumerateInstalled())
            {
                var group = new PointGroup
                {
                    Point = installed.Point,
                    Installed = installed.Installed,
                    ParentSegment = installed.ParentSegment,
                };

                FrameScheduler.SnapshotPoint(installed.Point, _snapshot);
                foreach (var handle in _snapshot)
                {
                    var node = new ProcessNode
                    {
                        Id = handle.Id,
                        Title = string.IsNullOrEmpty(handle.MethodLabel) ? "(anonymous)" : handle.MethodLabel,
                        State = handle.State,
                        Owner = handle.OwnerType,
                        PointName = installed.Point.ToString(),
                        KindName = handle.ProcessKind.ToString(),
                        SubLabel = $"{handle.ProcessKind}  ·  {installed.Point}",
                    };
                    group.Nodes.Add(node);
                    _nodes.Add(node);
                }

                _groups.Add(group);
            }

            Layout();

            // Preserve selection across rebuilds by stable handle id, so nodes that share the same
            // owner / method (the same script scheduling several processes) each stay selectable.
            if (_selected != null)
            {
                var prevId = _selected.Id;
                _selected = _nodes.Find(n => n.Id == prevId);
            }
        }

        /// <summary>
        /// Column-per-point layout: each Lifecycle point is a vertical band of its processes.
        /// Filter-aware — non-visible groups/nodes are skipped so visible content packs
        /// contiguously with no gaps left by the search filter or the show-empty toggle.
        /// </summary>
        private void Layout()
        {
            var x = ORIGIN_X;
            var maxY = ORIGIN_Y;

            foreach (var group in _groups)
            {
                if (!GroupVisible(group))
                    continue;

                var y = ORIGIN_Y;
                var visibleCount = 0;
                foreach (var node in group.Nodes)
                {
                    if (!NodeVisible(node))
                        continue;

                    node.Rect = new Rect(x, y, NODE_WIDTH, NODE_HEIGHT);
                    y += NODE_HEIGHT + ROW_GAP;
                    visibleCount++;
                }

                var contentBottom = visibleCount > 0 ? y - ROW_GAP : ORIGIN_Y + EMPTY_BAND_HEIGHT;
                maxY = Mathf.Max(maxY, contentBottom);

                group.Rect = new Rect(
                    x - BAND_PADDING,
                    ORIGIN_Y - HEADER_HEIGHT - BAND_PADDING,
                    NODE_WIDTH + BAND_PADDING * 2f,
                    0f); // height unified below

                x += NODE_WIDTH + BAND_PADDING * 2f + COLUMN_GAP;
            }

            var bandHeight = maxY - (ORIGIN_Y - HEADER_HEIGHT - BAND_PADDING) + BAND_PADDING;
            foreach (var group in _groups)
            {
                var r = group.Rect;
                r.height = bandHeight;
                group.Rect = r;
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

            HandleInput(graphRect);
            DrawGraph(graphRect);
            DrawSidebar(sideRect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var c = GUI.contentColor;
                GUI.contentColor = _driverInstalled ? InstalledColor : RemovedColor;
                GUILayout.Label(_driverInstalled ? "Driver: Installed" : "Driver: Removed",
                    EditorStyles.toolbarButton, GUILayout.Width(140f));
                GUI.contentColor = c;

                if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                    FrameAll();

                GUILayout.Space(8f);
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(36f));
                _zoom = GUILayout.HorizontalSlider(_zoom, MIN_ZOOM, MAX_ZOOM, GUILayout.Width(90f));

                GUILayout.Space(8f);
                _showEmptyPoints = GUILayout.Toggle(_showEmptyPoints, "Show Empty Points",
                    EditorStyles.toolbarButton, GUILayout.Width(130f));

                GUILayout.FlexibleSpace();

                if (GraphSearchField.Draw(180f, SEARCH_CONTROL, ref _search))
                    Repaint();

                GUILayout.Label($"{_nodes.Count} process(es)", EditorStyles.miniLabel);
                GUILayout.Space(8f);
                GUILayout.Label(Application.isPlaying ? "Play Mode" : "Edit Mode",
                    EditorStyles.miniLabel);
            }
        }

        private void FrameAll()
        {
            _zoom = 1f;
            _pan = new Vector2(10f, 10f);
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

                if (hit is { Owner: not null } && e.clickCount == 2)
                    ScriptLocator.Open(hit.Owner);

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

        private ProcessNode NodeAt(Vector2 world)
        {
            foreach (var n in _nodes)
                if (NodeVisible(n) && n.Rect.Contains(world)) return n;
            return null;
        }

        private bool NodeVisible(ProcessNode n) => GraphSearchField.Matches(_search, n.Title, n.SubLabel);

        private bool GroupVisible(PointGroup g)
        {
            // With an active search, only surface points that host at least one matching process.
            if (!string.IsNullOrEmpty(_search))
                return g.Nodes.Exists(NodeVisible);
            // No search: empty points stay hidden unless the toggle opts them in.
            return _showEmptyPoints || g.Nodes.Count > 0;
        }

        private void ShowNodeMenu(ProcessNode node)
        {
            var menu = new GenericMenu();

            if (node.Owner != null)
            {
                menu.AddItem(new GUIContent("Ping Script"), false, () => ScriptLocator.Ping(node.Owner));
                menu.AddItem(new GUIContent("Open Script"), false, () => ScriptLocator.Open(node.Owner));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Copy Type Name"), false,
                    () => EditorGUIUtility.systemCopyBuffer = node.Owner.FullName);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Open Script (anonymous method)"));
            }

            menu.AddItem(new GUIContent("Copy Method"), false,
                () => EditorGUIUtility.systemCopyBuffer = node.Title);

            menu.ShowAsContext();
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

            if (!_driverInstalled)
            {
                var boxWidth = Mathf.Min(460f, local.width - 40f);
                var boxRect = new Rect(
                    (local.width - boxWidth) * 0.5f,
                    (local.height - 60f) * 0.5f,
                    boxWidth, 60f);
                // Edit mode: informational (driver installs on play). Play mode: unexpected → warning.
                EditorGUI.HelpBox(boxRect,
                    "Lifecycle player-loop driver is not installed.\nGroups appear once the driver is running.",
                    Application.isPlaying ? MessageType.Warning : MessageType.Info);
                GUI.EndClip();
                return;
            }

            foreach (var group in _groups)
            {
                if (!GroupVisible(group)) continue;
                DrawGroup(group, local);
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

        private void DrawGroup(PointGroup group, Rect local)
        {
            var r = WorldToScreen(group.Rect, local);
            if (!r.Overlaps(local)) return;

            var color = group.Installed ? InstalledColor : RemovedColor;

            EditorGUI.DrawRect(r, color * new Color(1f, 1f, 1f, 0.07f));
            DrawOutline(r, color * new Color(1f, 1f, 1f, 0.5f), 1f);

            if (_zoom < 0.4f) return;

            var header = new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 20f);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Mathf.RoundToInt(Mathf.Lerp(9f, 13f, Mathf.InverseLerp(MIN_ZOOM, MAX_ZOOM, _zoom))),
                normal = { textColor = color },
            };
            var dot = group.Installed ? "●" : "○";
            GUI.Label(header, $"{dot} {group.Point}  ({group.Nodes.Count})", style);

            var sub = new Rect(r.x + 8f, r.y + 4f + 16f, r.width - 16f, 16f);
            GUI.Label(sub, group.ParentSegment, EditorStyles.miniLabel);

            if (group.Nodes.Count == 0)
            {
                var empty = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.62f) },
                };
                var er = new Rect(r.x, r.y + HEADER_HEIGHT * _zoom, r.width, 40f);
                GUI.Label(er, "no processes", empty);
            }
        }

        private void DrawNode(ProcessNode n, Rect local)
        {
            var r = WorldToScreen(n.Rect, local);
            if (!r.Overlaps(local)) return;

            var accent = StateColor(n.State);

            EditorGUI.DrawRect(r, NodeBg);
            EditorGUI.DrawRect(new Rect(r.x, r.y, ACCENT_WIDTH * _zoom, r.height), accent);

            var selected = _selected == n;
            var outline = selected ? Color.white
                : n.State == FrameProcessState.Error ? ErrorColor
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

            var stateRect = new Rect(innerX, r.y + 38f * _zoom, innerW, 16f * _zoom);
            var stateStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                { clipping = TextClipping.Clip, normal = { textColor = accent } };
            GUI.Label(stateRect, n.State.ToString().ToLowerInvariant(), stateStyle);
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

            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            if (_selected == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a process node to inspect it.\n\n" +
                    "Groups = Lifecycle points (green = installed, red = removed).\n" +
                    "Nodes = scheduled frame processes (yellow = waiting, green = running, red = error).",
                    MessageType.Info);
            }
            else
            {
                Row("Method", _selected.Title);
                Row("Kind", _selected.KindName);
                Row("Point", _selected.PointName);
                Row("State", _selected.State.ToString());
                Row("Owner", _selected.Owner != null ? _selected.Owner.FullName : "(anonymous)");
                if (_selected.Owner != null)
                    Row("Origin", ScriptLocator.DescribeOrigin(_selected.Owner));

                EditorGUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(_selected.Owner == null))
                {
                    if (GUILayout.Button("Open Script"))
                        ScriptLocator.Open(_selected.Owner);
                    if (GUILayout.Button("Ping Script"))
                        ScriptLocator.Ping(_selected.Owner);
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(70f));
                EditorGUILayout.SelectableLabel(value, EditorStyles.wordWrappedLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        // ── helpers ────────────────────────────────────────────────────────

        private static Color StateColor(FrameProcessState state) => state switch
        {
            FrameProcessState.Waiting => WaitingColor,
            FrameProcessState.Running => RunningColor,
            FrameProcessState.Error => ErrorColor,
            FrameProcessState.Completed => new Color(0.45f, 0.45f, 0.48f),
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
    }
}

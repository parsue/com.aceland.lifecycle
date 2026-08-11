using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal sealed class DependencyGraphWindow : EditorWindow
    {
        const float MinZoom = 0.35f;
        const float MaxZoom = 1.6f;
        const float SidebarWidth = 300f;

        GraphData _data;
        Vector2 _pan = Vector2.zero;
        float _zoom = 1f;
        GraphNode _selected;
        Vector2 _issueScroll;
        Vector2 _inspectorScroll;
        bool _showIssues = true;
        bool _liveMode = true;
        string _search = string.Empty;
        double _lastRepaint;

        readonly HashSet<Type> _highlightUp = new HashSet<Type>();
        readonly HashSet<Type> _highlightDown = new HashSet<Type>();

        [MenuItem("Tools/AceLand/Lifecycle/Dependency Graph", priority = 10)]
        public static void Open()
        {
            var w = GetWindow<DependencyGraphWindow>();
            w.titleContent = new GUIContent("Lifecycle Graph");
            w.minSize = new Vector2(760f, 420f);
            w.Show();
        }

        void OnEnable()
        {
            Rebuild();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnPlayModeChanged(PlayModeStateChange _) => Rebuild();

        void OnEditorUpdate()
        {
            // Play 模式下每 0.5 秒刷新一次狀態顏色，成本可忽略。
            if (!Application.isPlaying || !_liveMode) return;
            if (EditorApplication.timeSinceStartup - _lastRepaint < 0.5) return;
            _lastRepaint = EditorApplication.timeSinceStartup;
            Rebuild();
            Repaint();
        }

        void Rebuild()
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

        void OnGUI()
        {
            if (_data == null) Rebuild();

            DrawToolbar();

            var body = new Rect(0f, EditorStyles.toolbar.fixedHeight,
                                position.width, position.height - EditorStyles.toolbar.fixedHeight);

            var graphRect = new Rect(body.x, body.y, body.width - SidebarWidth, body.height);
            var sideRect = new Rect(body.xMax - SidebarWidth, body.y, SidebarWidth, body.height);

            HandleInput(graphRect);
            DrawGraph(graphRect);
            DrawSidebar(sideRect);
        }

        void DrawToolbar()
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
                _zoom = GUILayout.HorizontalSlider(_zoom, MinZoom, MaxZoom, GUILayout.Width(90f));

                GUILayout.FlexibleSpace();

                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField,
                                              GUILayout.Width(180f));

                var issueCount = _data?.Issues.Count ?? 0;
                var c = GUI.color;
                if (issueCount > 0) GUI.color = new Color(1f, 0.6f, 0.4f);
                _showIssues = GUILayout.Toggle(_showIssues, $"Issues ({issueCount})",
                                               EditorStyles.toolbarButton, GUILayout.Width(90f));
                GUI.color = c;

                GUILayout.Label(_data != null && _data.IsLive ? "runtime" : "static",
                                EditorStyles.miniLabel, GUILayout.Width(48f));
            }
        }

        void FrameAll()
        {
            if (_data == null) return;
            _zoom = 1f;
            _pan = new Vector2(10f, 10f);
        }

        void HandleInput(Rect graphRect)
        {
            var e = Event.current;
            if (!graphRect.Contains(e.mousePosition)) return;

            if (e.type == EventType.ScrollWheel)
            {
                var before = ScreenToWorld(e.mousePosition, graphRect);
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.035f), MinZoom, MaxZoom);
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

                if (hit != null && e.clickCount == 2) PingType(hit.Id);
                e.Use();
                Repaint();
            }
        }

        Vector2 ScreenToWorld(Vector2 screen, Rect graphRect)
            => (screen - graphRect.position - _pan) / _zoom;

        Rect WorldToScreen(Rect world, Rect graphRect)
            => new Rect(world.x * _zoom + _pan.x + graphRect.x,
                        world.y * _zoom + _pan.y + graphRect.y,
                        world.width * _zoom,
                        world.height * _zoom);

        void RecalculateHighlight()
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

        void DrawGraph(Rect graphRect)
        {
            EditorGUI.DrawRect(graphRect, new Color(0.16f, 0.16f, 0.17f));
            GUI.BeginClip(graphRect);
            var local = new Rect(0f, 0f, graphRect.width, graphRect.height);

            DrawGrid(local);

            // Phase bands
            foreach (var band in _data.Bands)
            {
                var r = WorldToScreen(band.Rect, local);
                EditorGUI.DrawRect(r, PhaseColor(band.Phase) * new Color(1f, 1f, 1f, 0.10f));

                var header = new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 20f);
                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = Mathf.RoundToInt(Mathf.Lerp(9f, 14f, Mathf.InverseLerp(MinZoom, MaxZoom, _zoom))),
                    normal = { textColor = PhaseColor(band.Phase) }
                };
                GUI.Label(header, $"{band.Phase}  ({band.Count})  ·  {PhaseHint(band.Phase)}", style);
            }

            // Edges
            Handles.BeginGUI();
            foreach (var n in _data.Nodes)
            {
                foreach (var d in n.DependsOn)
                {
                    if (d == null) continue;
                    var dep = _data.Nodes.Find(x => x.Id == d);
                    if (dep == null) continue;
                    DrawEdge(dep, n, local);
                }
            }
            Handles.EndGUI();

            // Nodes
            foreach (var n in _data.Nodes) DrawNode(n, local);

            GUI.EndClip();
        }

        void DrawGrid(Rect rect)
        {
            const float step = 24f;
            var spacing = step * _zoom;
            if (spacing < 6f) return;

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.035f);
            for (float x = _pan.x % spacing; x < rect.width; x += spacing)
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, rect.height));
            for (float y = _pan.y % spacing; y < rect.height; y += spacing)
                Handles.DrawLine(new Vector3(0f, y), new Vector3(rect.width, y));
            Handles.EndGUI();
        }

        void DrawEdge(GraphNode from, GraphNode to, Rect local)
        {
            var a = WorldToScreen(from.Rect, local);
            var b = WorldToScreen(to.Rect, local);

            var start = new Vector3(a.xMax, a.center.y);
            var end = new Vector3(b.xMin, b.center.y);
            var tangent = Mathf.Max(40f, Mathf.Abs(end.x - start.x) * 0.5f);

            var color = new Color(1f, 1f, 1f, 0.22f);
            float width = 2f;

            bool cyc = _data.CycleMembers.Contains(from.Id) && _data.CycleMembers.Contains(to.Id);
            if (cyc) { color = new Color(1f, 0.3f, 0.25f, 0.95f); width = 3f; }
            else if (_selected != null)
            {
                bool up = to.Id == _selected.Id || (_highlightUp.Contains(to.Id) && _highlightUp.Contains(from.Id))
                          || (to.Id == _selected.Id);
                bool down = from.Id == _selected.Id || _highlightDown.Contains(from.Id);

                if (up && (_highlightUp.Contains(from.Id) || to.Id == _selected.Id))
                    { color = new Color(0.4f, 0.75f, 1f, 0.95f); width = 3f; }
                else if (down)
                    { color = new Color(1f, 0.8f, 0.35f, 0.9f); width = 3f; }
                else
                    color = new Color(1f, 1f, 1f, 0.08f);
            }

            Handles.DrawBezier(start, end,
                               start + Vector3.right * tangent,
                               end + Vector3.left * tangent,
                               color, null, width);
        }

        void DrawNode(GraphNode n, Rect local)
        {
            var r = WorldToScreen(n.Rect, local);
            if (!r.Overlaps(local)) return;

            bool dimmed = !string.IsNullOrEmpty(_search) &&
                          n.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0;

            var accent = StateColor(n.State);
            var bg = new Color(0.22f, 0.23f, 0.25f, dimmed ? 0.35f : 1f);

            EditorGUI.DrawRect(r, bg);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4f * _zoom, r.height), accent);

            // border
            var outline = _selected != null && _selected.Id == n.Id
                ? Color.white
                : (_data.CycleMembers.Contains(n.Id) ? new Color(1f, 0.3f, 0.25f) : new Color(0f, 0f, 0f, 0.6f));
            DrawOutline(r, outline, _selected != null && _selected.Id == n.Id ? 2f : 1f);

            if (_zoom < 0.5f) return;

            int fs = Mathf.RoundToInt(11f * Mathf.Clamp(_zoom, 0.6f, 1.2f));
            var title = new GUIStyle(EditorStyles.boldLabel)
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
            GUI.Label(new Rect(r.x + pad + 4f, r.y + 4f * _zoom, r.width - pad * 2f, 18f * _zoom),
                      n.DisplayName, title);

            var line2 = n.SortIndex >= 0
                ? $"#{n.SortIndex:00}  {n.State}  {n.InitMilliseconds:0.00} ms"
                : $"{n.State}   order {n.Order}";
            GUI.Label(new Rect(r.x + pad + 4f, r.y + 22f * _zoom, r.width - pad * 2f, 16f * _zoom),
                      line2, sub);

            var tags = string.Empty;
            if (n.IsAsync) tags += "async  ";
            if (n.LiveOnly) tags += "manual  ";
            if (!n.AutoRegister && !n.LiveOnly) tags += "no-autoreg  ";
            if (!n.AssemblyOptedIn && n.State == ModuleState.Declared)
            {
                DrawOutline(r, new Color(1f, 0.35f, 0.2f), 2f);
                var warn = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = Mathf.Max(9, fs - 2),
                    normal = { textColor = new Color(1f, 0.45f, 0.3f) },
                };
                GUI.Label(new Rect(r.x + pad + 4f, r.y + 38f * _zoom, r.width - pad * 2f, 16f * _zoom),
                    "⚠ assembly not opted in", warn);
            }
            if (tags.Length > 0)
                GUI.Label(new Rect(r.x + pad + 4f, r.y + 38f * _zoom, r.width - pad * 2f, 16f * _zoom),
                          tags, sub);
        }

        static void DrawOutline(Rect r, Color c, float w)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        // ── sidebar ────────────────────────────────────────────────────────────

        void DrawSidebar(Rect rect)
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

            if (_selected == null)
            {
                EditorGUILayout.HelpBox(
                    "Click a node to inspect.\n\n" +
                    "Drag: middle mouse / alt+left\nZoom: scroll wheel\nDouble click: ping script",
                    MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            EditorGUILayout.LabelField(_selected.DisplayName, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(_selected.Namespace, EditorStyles.miniLabel);
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
            if (GUILayout.Button("Ping Script")) PingType(_selected.Id);

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

        void SelectById(Type id)
        {
            var n = _data.Nodes.Find(x => x.Id == id);
            if (n != null) SelectNode(n);
        }

        void SelectNode(GraphNode n)
        {
            _selected = n;
            RecalculateHighlight();
            Repaint();
        }

        static void PingType(Type t)
        {
            if (t == null) return;
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {t.Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null) continue;
                var cls = ms.GetClass();
                if (cls == t || (cls != null && t.IsAssignableFrom(cls)))
                {
                    EditorGUIUtility.PingObject(ms);
                    Selection.activeObject = ms;
                    return;
                }
            }
            LifecycleLog.Warning($"Script for '{t.Name}' not found in AssetDatabase (precompiled?).");
        }

        static Color PhaseColor(ModulePhase p)
        {
            switch (p)
            {
                case ModulePhase.Core: return new Color(0.45f, 0.72f, 1f);
                case ModulePhase.Runtime: return new Color(0.55f, 0.95f, 0.6f);
                case ModulePhase.Scene: return new Color(1f, 0.78f, 0.4f);
                default: return new Color(0.85f, 0.6f, 1f);
            }
        }

        static string PhaseHint(ModulePhase p)
        {
            switch (p)
            {
                case ModulePhase.Core: return "AfterAssembliesLoaded";
                case ModulePhase.Runtime: return "BeforeSceneLoad";
                case ModulePhase.Scene: return "AfterSceneLoad";
                default: return "AfterSceneLoad (late)";
            }
        }

        static Color StateColor(ModuleState s)
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
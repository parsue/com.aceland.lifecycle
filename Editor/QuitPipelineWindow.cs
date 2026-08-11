using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal sealed class QuitPipelineWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem("Tools/AceLand/Lifecycle/Quit Pipeline", priority = 13)]
        public static void Open()
        {
            var w = GetWindow<QuitPipelineWindow>();
            w.titleContent = new GUIContent("Quit Pipeline");
            w.minSize = new Vector2(380f, 300f);
            w.Show();
        }

        void OnEnable() => EditorApplication.update += Tick;
        void OnDisable() => EditorApplication.update -= Tick;

        double _last;
        void Tick()
        {
            if (EditorApplication.timeSinceStartup - _last < 0.3) return;
            _last = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect the live quit pipeline.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Quitting", ApplicationQuitPipeline.IsQuitting.ToString());
            EditorGUILayout.LabelField("Ready To Quit", ApplicationQuitPipeline.IsReadyToQuit.ToString());
            EditorGUILayout.LabelField("Alive Token", LifecycleToken.IsAlive ? "alive" : "cancelled");
            EditorGUILayout.LabelField("Current", ApplicationQuitPipeline.CurrentStatus ?? "-");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Blockers", EditorStyles.boldLabel);
            var blockers = ApplicationQuitPipeline.Blockers;
            if (blockers.Count == 0) EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
            foreach (var b in blockers)
            {
                var c = GUI.color;
                if (b.IsBusy) GUI.color = new Color(1f, 0.7f, 0.3f);
                EditorGUILayout.LabelField($"  {(b.IsBusy ? "● busy" : "○ idle")}  {b.BusyReason ?? b.GetType().Name}");
                GUI.color = c;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Execution Plan", EditorStyles.boldLabel);
            var plan = ApplicationQuitPipeline.GetPlan();
            if (plan.Count == 0) EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
            for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                EditorGUILayout.LabelField(
                    $"  {i:00}. {p.name}   order {p.order}   {(p.fromModule ? "[module]" : "[manual]")}");
            }

            EditorGUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(ApplicationQuitPipeline.IsQuitting))
            {
                if (GUILayout.Button("Simulate Quit"))
                    ApplicationQuitPipeline.Quit();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
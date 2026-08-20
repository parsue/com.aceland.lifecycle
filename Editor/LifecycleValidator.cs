using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal static class LifecycleValidator
    {
        private const string AUTO_VALIDATE_KEY = "AceLand.Lifecycle.AutoValidate";

        [MenuItem("Tools/AceLand/Lifecycle/Validate Dependencies", priority = 23)]
        public static void Validate()
        {
            var data = ModuleGraphModel.Build(preferLive: false);

            if (data.Issues.Count == 0)
            {
                LifecycleLog.Info($"Validation passed. {data.Nodes.Count} module(s), no issues.");
                return;
            }

            foreach (var issue in data.Issues) LifecycleLog.Error(issue);
            LifecycleLog.Error($"Validation found {data.Issues.Count} issue(s). " +
                               "Open Tools > AceLand > Lifecycle > Initialization Graph.");
        }

        [MenuItem("Tools/AceLand/Lifecycle/Auto Validate On Compile", priority = 24)]
        private static void ToggleAutoValidate()
        {
            var v = !EditorPrefs.GetBool(AUTO_VALIDATE_KEY, false);
            EditorPrefs.SetBool(AUTO_VALIDATE_KEY, v);
            Menu.SetChecked("Tools/AceLand/Lifecycle/Auto Validate On Compile", v);
        }

        [MenuItem("Tools/AceLand/Lifecycle/Auto Validate On Compile", true)]
        private static bool ToggleAutoValidateValidate()
        {
            Menu.SetChecked("Tools/AceLand/Lifecycle/Auto Validate On Compile",
                            EditorPrefs.GetBool(AUTO_VALIDATE_KEY, false));
            return true;
        }

        // caution: don't touch asset. pure reflection TypeCache, safe.
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            if (!EditorPrefs.GetBool(AUTO_VALIDATE_KEY, false)) return;
            EditorApplication.delayCall += Validate;
        }
    }
}
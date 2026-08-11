using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal static class LifecycleValidator
    {
        const string AutoValidateKey = "AceLand.Lifecycle.AutoValidate";

        [MenuItem("Tools/AceLand/Lifecycle/Validate Dependencies", priority = 11)]
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
                               "Open Tools > AceLand > Lifecycle > Dependency Graph.");
        }

        [MenuItem("Tools/AceLand/Lifecycle/Auto Validate On Compile", priority = 12)]
        static void ToggleAutoValidate()
        {
            var v = !EditorPrefs.GetBool(AutoValidateKey, false);
            EditorPrefs.SetBool(AutoValidateKey, v);
            Menu.SetChecked("Tools/AceLand/Lifecycle/Auto Validate On Compile", v);
        }

        [MenuItem("Tools/AceLand/Lifecycle/Auto Validate On Compile", true)]
        static bool ToggleAutoValidateValidate()
        {
            Menu.SetChecked("Tools/AceLand/Lifecycle/Auto Validate On Compile",
                            EditorPrefs.GetBool(AutoValidateKey, false));
            return true;
        }

        // caution: don't touch asset. pure reflection TypeCache, safe.
        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            if (!EditorPrefs.GetBool(AutoValidateKey, false)) return;
            EditorApplication.delayCall += Validate;
        }
    }
}
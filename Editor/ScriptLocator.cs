using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Resolves a <see cref="Type"/> to its source <see cref="MonoScript"/>, working for
    /// embedded packages, PackageCache packages, and types whose file name differs from
    /// the type name. Results are cached per domain.
    /// </summary>
    internal static class ScriptLocator
    {
        static readonly Dictionary<Type, MonoScript> s_Hits = new Dictionary<Type, MonoScript>();
        static readonly HashSet<Type> s_Misses = new HashSet<Type>();

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.projectChanged += Clear;

        public static void Clear()
        {
            s_Hits.Clear();
            s_Misses.Clear();
        }

        // ── public API ─────────────────────────────────────────────────────

        public static bool TryFind(Type type, out MonoScript script)
        {
            script = null;
            if (type == null) return false;

            var root = RootType(type);

            if (s_Hits.TryGetValue(root, out script) && script != null) return true;
            if (s_Misses.Contains(root)) return false;

            script = Locate(root);

            if (script != null) s_Hits[root] = script;
            else s_Misses.Add(root);

            return script != null;
        }

        public static void Ping(Type type)
        {
            if (TryFind(type, out var script) && script != null)
            {
                EditorGUIUtility.PingObject(script);
                Selection.activeObject = script;
                return;
            }

            FallbackReveal(type);
        }

        public static void Open(Type type)
        {
            if (TryFind(type, out var script))
            {
                var line = FindDeclarationLine(script, RootType(type).Name);
                AssetDatabase.OpenAsset(script, line);
                return;
            }

            FallbackReveal(type);
        }

        /// <summary>Human-readable origin, e.g. "com.aceland.library 1.2.0" or "Assets".</summary>
        public static string DescribeOrigin(Type type)
        {
            if (type == null) return "-";

            var pkg = PackageInfo.FindForAssembly(type.Assembly);
            if (pkg != null) return $"{pkg.name} {pkg.version}";

            var asmdef = AsmdefPath(type);
            if (!string.IsNullOrEmpty(asmdef)) return Path.GetFileNameWithoutExtension(asmdef);

            return IsPrecompiled(type) ? "precompiled" : "Assets";
        }

        // ── resolution ─────────────────────────────────────────────────────

        static MonoScript Locate(Type type)
        {
            foreach (var folder in SearchFolders(type))
            {
                var found = SearchFolder(folder, type);
                if (found != null) return found;
            }

            // Last resort: name-based project-wide search.
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (ms != null && ms.GetClass() == type) return ms;
            }

            return null;
        }

        /// <summary>Tightest scope first: asmdef folder, then package root, then Assets.</summary>
        static IEnumerable<string> SearchFolders(Type type)
        {
            var asmdef = AsmdefPath(type);
            if (!string.IsNullOrEmpty(asmdef))
            {
                var dir = Path.GetDirectoryName(asmdef);
                if (!string.IsNullOrEmpty(dir)) yield return dir.Replace('\\', '/');
            }

            var pkg = PackageInfo.FindForAssembly(type.Assembly);
            if (pkg != null && !string.IsNullOrEmpty(pkg.assetPath)) yield return pkg.assetPath;

            yield return "Assets";
        }

        static string AsmdefPath(Type type)
        {
            try
            {
                return CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                    type.Assembly.GetName().Name);
            }
            catch { return null; }
        }

        static MonoScript SearchFolder(string folder, Type type)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;

            string[] guids;
            try { guids = AssetDatabase.FindAssets("t:MonoScript", new[] { folder }); }
            catch { return null; }

            MonoScript textMatch = null;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null) continue;

                // Exact: file name matches the type, so GetClass() resolves.
                if (ms.GetClass() == type) return ms;

                // Fallback: type declared in a differently named file.
                if (textMatch == null && DeclaresType(ms, type)) textMatch = ms;
            }

            return textMatch;
        }

        static bool DeclaresType(MonoScript script, Type type)
        {
            var src = script.text;
            if (string.IsNullOrEmpty(src)) return false;
            if (src.IndexOf(type.Name, StringComparison.Ordinal) < 0) return false;   // cheap reject

            return DeclarationRegex(type.Name).IsMatch(src);
        }

        static int FindDeclarationLine(MonoScript script, string typeName)
        {
            var src = script.text;
            if (string.IsNullOrEmpty(src)) return 0;

            var match = DeclarationRegex(typeName).Match(src);
            if (!match.Success) return 0;

            var line = 1;
            for (var i = 0; i < match.Index && i < src.Length; i++)
                if (src[i] == '\n') line++;

            return line;
        }

        static Regex DeclarationRegex(string typeName) => new Regex(
            $@"\b(class|struct|interface|record|enum)\s+{Regex.Escape(typeName)}\b",
            RegexOptions.Multiline);

        // ── fallbacks ──────────────────────────────────────────────────────

        static bool IsPrecompiled(Type type)
        {
            var loc = SafeLocation(type);
            return !string.IsNullOrEmpty(loc) &&
                   loc.Replace('\\', '/').IndexOf("/ScriptAssemblies/", StringComparison.Ordinal) < 0;
        }

        static void FallbackReveal(Type type)
        {
            // 1. Select the package root so the user at least lands in the right place.
            var pkg = PackageInfo.FindForAssembly(type.Assembly);
            if (pkg != null && !string.IsNullOrEmpty(pkg.assetPath))
            {
                var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>($"{pkg.assetPath}/package.json");
                if (manifest != null)
                {
                    EditorGUIUtility.PingObject(manifest);
                    Selection.activeObject = manifest;
                    LifecycleLog.Warning(
                        $"No source file for '{type.Name}' — selected package '{pkg.name}' instead " +
                        "(precompiled assembly, or the type lives in a differently named file).");
                    return;
                }
            }

            // 2. Select the asmdef.
            var asmdef = AsmdefPath(type);
            if (!string.IsNullOrEmpty(asmdef))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asmdef);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                    LifecycleLog.Warning($"No source file for '{type.Name}' — selected '{asmdef}'.");
                    return;
                }
            }

            // 3. Reveal the DLL on disk.
            var loc = SafeLocation(type);
            if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
            {
                EditorUtility.RevealInFinder(loc);
                LifecycleLog.Warning($"'{type.Name}' has no source file — revealed '{loc}'.");
                return;
            }

            LifecycleLog.Warning(
                $"Could not locate '{type.FullName}' (assembly '{type.Assembly.GetName().Name}').");
        }

        static string SafeLocation(Type type)
        {
            try { return type.Assembly.IsDynamic ? null : type.Assembly.Location; }
            catch { return null; }
        }

        static Type RootType(Type type)
        {
            var t = type;
            while (t.IsNested && t.DeclaringType != null) t = t.DeclaringType;
            return t.IsGenericType ? t.GetGenericTypeDefinition() : t;
        }
    }
}
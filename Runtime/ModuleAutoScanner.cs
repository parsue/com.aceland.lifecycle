using System;
using System.Collections.Generic;
using System.Reflection;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Scans only assemblies marked with <see cref="LifecycleAssemblyAttribute"/>,
    /// finds the types carrying <see cref="LifecycleModuleAttribute"/> with AutoRegister = true and registers them.
    /// </summary>
    internal static class ModuleAutoScanner
    {
        public static void ScanAndRegister()
        {
#if ACELAND_LIFECYCLE_NO_AUTOSCAN
            return;
#else
            // UAC0005: Unity 建議改用 CurrentAssemblies.GetLoadedAssemblies() 或 TypeCache，
            // 但兩者皆為 Editor-only API，無法用於此 Runtime assembly（最低支援 Unity 2022.3）。
            // 這裡以已載入 assembly 逐一 opt-in 掃描，已用 IsDynamic 過濾並包覆例外處理，
            // 因此忽略 dynamic/unloaded assembly 導致的風險，故局部抑制此警告。
#pragma warning disable UAC0005
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
#pragma warning restore UAC0005
            var found = new List<Type>();
            var scannedNames = new List<string>();

            foreach (var asm in assemblies)
            {
                if (asm.IsDynamic) continue;
                
#if ACELAND_LIFECYCLE_SCAN_ALL_ASSEMBLIES
                bool optedIn = true;
#else
                bool optedIn;
                try
                {
                    optedIn = asm.GetCustomAttributes(typeof(LifecycleAssemblyAttribute), false).Length > 0;
                }
                catch { continue; }
#endif
                if (!optedIn) continue;

                scannedNames.Add(asm.GetName().Name);

                object[] marks;
                try { marks = asm.GetCustomAttributes(typeof(LifecycleAssemblyAttribute), false); }
                catch { continue; }
                if (marks.Length == 0) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                    if (!typeof(IModule).IsAssignableFrom(t)) continue;

                    var attr = (LifecycleModuleAttribute)Attribute.GetCustomAttribute(
                        t, typeof(LifecycleModuleAttribute));
                    if (attr == null || !attr.AutoRegister) continue;

                    found.Add(t);
                }
            }

            // Deterministic: the registration order stays fixed even if the assembly load order changes.
            found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            
            LifecycleLog.Info($"Auto-scan: {scannedNames.Count} assembly(ies) opted in " +
                              $"[{string.Join(", ", scannedNames)}], {found.Count} module(s) found.");

            foreach (var t in found)
            {
                try
                {
                    var instance = (IModule)Activator.CreateInstance(t, nonPublic: true);
                    ModuleRegistry.Register(AttrIdOf(t) ?? t, instance, null, null, null, autoRegistered: true);
                }
                catch (Exception ex)
                {
                    LifecycleLog.Exception(
                        new Exception($"[Lifecycle] Auto-register failed for '{t.Name}'.", ex));
                }
            }

            Type AttrIdOf(Type t)
            {
                var a = (LifecycleModuleAttribute)Attribute.GetCustomAttribute(
                    t, typeof(LifecycleModuleAttribute));
                return a?.Id;
            }

            WarnAboutMissedAssemblies(assemblies);
#endif
        }

        private static void WarnAboutMissedAssemblies(Assembly[] assemblies)
        {
#if UNITY_EDITOR
            foreach (var asm in assemblies)
            {
                if (asm.IsDynamic) continue;

                try
                {
                    if (asm.GetCustomAttributes(typeof(LifecycleAssemblyAttribute), false).Length > 0)
                        continue;

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;

                        var attr = (LifecycleModuleAttribute)Attribute.GetCustomAttribute(
                            t, typeof(LifecycleModuleAttribute));
                        if (attr == null || !attr.AutoRegister) continue;

                        LifecycleLog.Error(
                            $"'{t.Name}' has [LifecycleModule] but assembly '{asm.GetName().Name}' " +
                            $"is missing [assembly: LifecycleAssembly]. It will NEVER be registered.");
                    }
                }
                catch
                {
                    // ignored
                }
            }
#endif
        }
    }
}
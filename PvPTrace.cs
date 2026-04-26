using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

/// <summary>
/// Diagnostic trace: attaches logging-only prefixes to every plausible damage
/// entry-point. When `kpvp trace on`, every fire of one of these methods logs
/// a one-line trace ("[KitsunePvP/trace] METHOD fired ..."). Run, take one
/// shot, and the log shows which method 2.0's PvP damage actually flows
/// through. Defaults to off — patches stay attached but log nothing.
/// </summary>
public static class PvPTrace
{
    public static volatile bool Active = false;

    public static void Initialize(Harmony harmony)
    {
        var loggingPrefix = new HarmonyMethod(AccessTools.Method(typeof(PvPTrace), nameof(LogPrefix)));

        // Methods on EntityAlive / EntityPlayer that could carry PvP damage
        TryPatch(harmony, loggingPrefix, typeof(EntityAlive),  "DamageEntity");
        TryPatch(harmony, loggingPrefix, typeof(EntityAlive),  "damageEntityLocal");
        TryPatch(harmony, loggingPrefix, typeof(EntityAlive),  "ProcessDamageResponse");
        TryPatch(harmony, loggingPrefix, typeof(EntityAlive),  "ProcessDamageResponseLocal");
        TryPatch(harmony, loggingPrefix, typeof(EntityAlive),  "ApplyLocalBodyDamage");
        TryPatch(harmony, loggingPrefix, typeof(EntityPlayer), "DamageEntity");
        TryPatch(harmony, loggingPrefix, typeof(EntityPlayer), "damageEntityLocal");

        // Network packets — server-side handlers for client-fired damage
        TryPatchByName(harmony, loggingPrefix, "NetPackageDamageEntity",            "ProcessPackage");
        TryPatchByName(harmony, loggingPrefix, "NetPackageRangeCheckDamageEntity",  "ProcessPackage");

        // Visual Player damage handlers (third-party gun framework, possibly the path)
        TryPatchAllNamedLike(harmony, loggingPrefix, "vp_PlayerDamageHandler",   methodNameContains: "amage");
        TryPatchAllNamedLike(harmony, loggingPrefix, "vp_FPPlayerDamageHandler", methodNameContains: "amage");
        TryPatchAllNamedLike(harmony, loggingPrefix, "vp_DamageHandler",         methodNameContains: "amage");

        Log.Out("[KitsunePvP/trace] trace patches installed (inactive — enable with: kpvp trace on)");
    }

    public static void LogPrefix(MethodBase __originalMethod, object[] __args)
    {
        if (!Active) return;
        try
        {
            string name = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
            string args = "";
            if (__args != null)
            {
                args = string.Join(", ", __args.Select(a =>
                {
                    if (a == null) return "null";
                    var t = a.GetType().Name;
                    if (a is int || a is float || a is bool || a is string || a is double) return $"{t}={a}";
                    return t;
                }));
            }
            Log.Out($"[KitsunePvP/trace] {name}({args})");
        }
        catch { /* never break the patched method */ }
    }

    // -------- patch helpers --------

    private static void TryPatch(Harmony harmony, HarmonyMethod prefix, Type t, string method)
    {
        try
        {
            var m = AccessTools.Method(t, method);
            if (m == null) { Log.Out($"[KitsunePvP/trace]   not found: {t.Name}.{method}"); return; }
            harmony.Patch(m, prefix: prefix);
            Log.Out($"[KitsunePvP/trace]   patched:   {t.Name}.{method}");
        }
        catch (Exception ex) { Log.Warning($"[KitsunePvP/trace]   FAIL {t.Name}.{method}: {ex.Message}"); }
    }

    private static void TryPatchByName(Harmony harmony, HarmonyMethod prefix, string typeName, string method)
    {
        var asm = typeof(EntityAlive).Assembly;
        var t = asm.GetType(typeName) ?? Type.GetType(typeName);
        if (t == null) { Log.Out($"[KitsunePvP/trace]   not found: {typeName} type"); return; }
        TryPatch(harmony, prefix, t, method);
    }

    private static void TryPatchAllNamedLike(Harmony harmony, HarmonyMethod prefix, string typeName, string methodNameContains)
    {
        var asm = typeof(EntityAlive).Assembly;
        var t = asm.GetType(typeName) ?? Type.GetType(typeName);
        if (t == null) { Log.Out($"[KitsunePvP/trace]   not found: {typeName} type"); return; }

        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.DeclaringType == t && m.Name.IndexOf(methodNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        if (methods.Length == 0) { Log.Out($"[KitsunePvP/trace]   no methods matching '{methodNameContains}' on {typeName}"); return; }

        foreach (var m in methods)
        {
            try
            {
                harmony.Patch(m, prefix: prefix);
                Log.Out($"[KitsunePvP/trace]   patched:   {typeName}.{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})");
            }
            catch (Exception ex) { Log.Warning($"[KitsunePvP/trace]   FAIL {typeName}.{m.Name}: {ex.Message}"); }
        }
    }
}

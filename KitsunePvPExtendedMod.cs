using System;
using HarmonyLib;
using System.Reflection;

public class KitsunePvPExtendedMod : IModApi
{
    public const string ModId = "com.adainthelab.kitsunepvpextended";

    public void InitMod(Mod _modInstance)
    {
        var harmony = new Harmony(ModId);

        PvPBalanceConfig.Initialize(_modInstance);
        PvPTelemetry.Initialize(_modInstance);

        // Patch `NetPackageDamageEntity.ProcessPackage` — the server-side entry
        // point for incoming networked damage in 7DTD 2.0. Trace runs confirmed
        // PvP shots flow through this method and NOT through DamageEntity or
        // damageEntityLocal. The client sends a pre-computed damage value; we
        // mutate it here before the response chain applies HP loss.
        var netPkgType = typeof(EntityAlive).Assembly.GetType("NetPackageDamageEntity")
                      ?? Type.GetType("NetPackageDamageEntity");
        bool patched = false;
        if (netPkgType != null)
        {
            var processPkg = AccessTools.Method(netPkgType, "ProcessPackage");
            if (processPkg != null)
            {
                harmony.Patch(processPkg,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PvPDamagePatch),
                                                                nameof(PvPDamagePatch.ProcessPackagePrefix))));
                Log.Out("[KitsunePvP] Patched NetPackageDamageEntity.ProcessPackage");
                patched = true;
            }
        }
        if (!patched)
        {
            Log.Error("[KitsunePvP] FATAL: NetPackageDamageEntity.ProcessPackage not found — mod will not function.");
        }

        // Trace patches (PvPTrace) are kept in source for future debugging but
        // not registered at startup in release builds — they patch many extra
        // methods purely for diagnostics and are unnecessary once the damage
        // path is confirmed. Re-add `PvPTrace.Initialize(harmony);` here if a
        // future game version moves the damage path again.

        Log.Out($"[KitsunePvP] Loaded v0.1.0 — preset: {PvPBalanceConfig.Current.PresetName}, global: {PvPBalanceConfig.Current.GlobalMultiplier:0.00}");
    }
}

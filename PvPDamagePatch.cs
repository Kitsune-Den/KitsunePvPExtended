using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// PvP damage scaling hook on `NetPackageDamageEntity.ProcessPackage`.
/// 7DTD 2.0 has the client send a pre-computed damage value over the network
/// (trusted by the server), so we mutate the value on the incoming packet
/// before the response chain (ProcessDamageResponse → ApplyLocalBodyDamage)
/// applies it to victim HP.
/// </summary>
public static class PvPDamagePatch
{
    private static readonly BindingFlags F =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static FieldInfo _strengthField;
    private static FieldInfo _attackerIdField;
    private static FieldInfo _victimIdField;
    private static FieldInfo _hitTransformNameField;   // string, e.g. "Head" / "Hips" / "RightLeg"
    private static FieldInfo _attackingItemField;       // ItemValue
    private static PropertyInfo _itemValueItemClassProp; // ItemValue.ItemClass
    private static bool _fieldsResolved;
    private static string _resolvedSummary = "<not yet>";

    private static void ResolvePackageFields(Type pkgType)
    {
        _strengthField    = FirstField(pkgType, "strength", "Strength", "damage", "Damage", "_strength");
        _attackerIdField  = FirstField(pkgType, "attackerEntityId", "AttackerEntityId", "attackerId");
        _victimIdField    = FirstField(pkgType, "entityId", "EntityId", "targetEntityId");
        _hitTransformNameField = FirstField(pkgType, "hitTransformName", "HitTransformName");
        _attackingItemField    = FirstField(pkgType, "attackingItem", "AttackingItem");

        // ItemValue.ItemClass — resolve property on the field's runtime type
        if (_attackingItemField != null)
        {
            _itemValueItemClassProp = _attackingItemField.FieldType.GetProperty("ItemClass", F);
        }

        _resolvedSummary =
            $"strength={_strengthField?.Name ?? "<MISSING>"}, " +
            $"attacker={_attackerIdField?.Name ?? "<MISSING>"}, " +
            $"victim={_victimIdField?.Name ?? "<MISSING>"}, " +
            $"hitXform={_hitTransformNameField?.Name ?? "<MISSING>"}, " +
            $"item={_attackingItemField?.Name ?? "<MISSING>"}";

        _fieldsResolved = true;
        Log.Out($"[KitsunePvP] resolved NetPackageDamageEntity fields: {_resolvedSummary}");
    }

    private static FieldInfo FirstField(Type t, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            var f = t.GetField(name, F);
            if (f != null) return f;
        }
        return null;
    }

    public static void ProcessPackagePrefix(object __instance, World _world)
    {
        if (__instance == null || _world == null) return;
        if (!_fieldsResolved) ResolvePackageFields(__instance.GetType());

        try
        {
            // Capture probe state BEFORE the dump call — DumpObject decrements
            // the counter, so PvPProbe.Active flips false mid-prefix and the
            // log-line gate below would otherwise miss the second probed hit.
            bool wasProbeActive = PvPProbe.Active;
            if (wasProbeActive)
            {
                PvPProbe.DumpObject("NetPackageDamageEntity", __instance, _resolvedSummary);
            }

            if (_strengthField == null || _attackerIdField == null || _victimIdField == null) return;

            int attackerId = Convert.ToInt32(_attackerIdField.GetValue(__instance));
            int victimId   = Convert.ToInt32(_victimIdField.GetValue(__instance));
            if (attackerId < 0 || attackerId == victimId) return;

            var attackerEnt = _world.GetEntity(attackerId);
            if (!(attackerEnt is EntityPlayer attacker)) return;
            var victimEnt = _world.GetEntity(victimId);
            if (!(victimEnt is EntityPlayer victim)) return;

            // PvP confirmed.
            var cfg = PvPBalanceConfig.Current;
            if (!cfg.Enabled) return;

            int rawStrength = Convert.ToInt32(_strengthField.GetValue(__instance));
            if (rawStrength <= 0) return;

            string itemName = SafeAttackingItemName(__instance);
            string weaponClass = cfg.ResolveWeaponClass(itemName);
            float weaponMult = cfg.WeaponClassMultiplier(weaponClass);

            string hitTransform = null;
            if (_hitTransformNameField != null)
            {
                try { hitTransform = _hitTransformNameField.GetValue(__instance) as string; }
                catch { }
            }
            float bodyMult = cfg.BodyPartMultiplier(hitTransform);

            float multiplier = cfg.GlobalMultiplier * weaponMult * bodyMult;
            int scaled = Mathf.Max(1, Mathf.RoundToInt(rawStrength * multiplier));

            if (cfg.PerHitCapFractionMaxHp > 0f)
            {
                int maxHp = SafeMaxHealth(victim);
                if (maxHp > 0)
                {
                    int cap = Mathf.Max(1, Mathf.RoundToInt(maxHp * cfg.PerHitCapFractionMaxHp));
                    if (scaled > cap) scaled = cap;
                }
            }

            // Write back in the field's actual type — strength is UInt16 in 2.0,
            // not Int32, so a plain boxed-int SetValue would throw.
            _strengthField.SetValue(__instance, Convert.ChangeType(scaled, _strengthField.FieldType));

            if (cfg.LogEveryHit || wasProbeActive)
            {
                Log.Out($"[KitsunePvP] {attacker.EntityName} -> {victim.EntityName} | " +
                        $"weapon={itemName ?? "?"} class={weaponClass} hit={hitTransform ?? "?"} | " +
                        $"raw={rawStrength} scaled={scaled} mult={multiplier:0.000}");
            }

            PvPTelemetry.LogHit(attacker, victim, itemName, weaponClass, hitTransform, rawStrength, scaled, multiplier);
        }
        catch (Exception ex)
        {
            Log.Warning($"[KitsunePvP] ProcessPackagePrefix failed: {ex.Message}");
        }
    }

    private static string SafeAttackingItemName(object pkgInstance)
    {
        try
        {
            if (_attackingItemField == null || _itemValueItemClassProp == null) return null;
            var itemValue = _attackingItemField.GetValue(pkgInstance);
            if (itemValue == null) return null;
            var itemClass = _itemValueItemClassProp.GetValue(itemValue);
            if (itemClass == null) return null;
            // ItemClass.Name is a public field/property in 7DTD
            var nameField = itemClass.GetType().GetField("Name", F)
                         ?? itemClass.GetType().GetField("Name");
            if (nameField != null) return nameField.GetValue(itemClass) as string;
            var nameProp = itemClass.GetType().GetProperty("Name", F);
            if (nameProp != null) return nameProp.GetValue(itemClass) as string;
        }
        catch { }
        return null;
    }

    private static int SafeMaxHealth(EntityPlayer victim)
    {
        try { return victim.GetMaxHealth(); }
        catch { return 100; }
    }
}

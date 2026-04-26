using System;
using System.Reflection;
using System.Text;

/// <summary>
/// Diagnostic introspection on the next N PvP hits. Enabled via:  kpvp probe [count]
/// Dumps every instance field on DamageSource (name + type + value), the
/// EnumBodyPartHit enum members, the attacker's held ItemClass + tags, and the
/// resolved weapon-class / body-part our config picked. Lets us verify field
/// names and item-name patterns without decompiling or recompiling.
/// </summary>
public static class PvPProbe
{
    private static int _remaining = 0;
    private const BindingFlags FFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static bool Active => _remaining > 0;

    public static void Arm(int count)
    {
        _remaining = Math.Max(0, count);
        Log.Out($"[KitsunePvP/probe] armed for next {_remaining} PvP hits.");
    }

    public static void Disarm()
    {
        _remaining = 0;
        Log.Out("[KitsunePvP/probe] disarmed.");
    }

    /// <summary>
    /// Generic reflection dump — used to inspect any damage-related object
    /// (network package, response, etc.) on the next N hits. Decrements the
    /// remaining-shots counter so probe self-disarms.
    /// </summary>
    public static void DumpObject(string label, object obj, string headerNote = null)
    {
        if (_remaining <= 0 || obj == null) return;
        _remaining--;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"==================== KitsunePvP PROBE: {label} ====================");
        if (!string.IsNullOrEmpty(headerNote)) sb.AppendLine($"note: {headerNote}");
        try
        {
            var t = obj.GetType();
            sb.AppendLine($"runtime_type: {t.FullName}");
            sb.AppendLine("--- fields ---");
            foreach (var f in t.GetFields(FFlags))
            {
                object v;
                try { v = f.GetValue(obj); } catch (Exception ex) { v = $"<err: {ex.Message}>"; }
                sb.AppendLine($"  {f.FieldType.Name,-28} {f.Name,-28} = {v}");
            }
            sb.AppendLine("--- properties ---");
            foreach (var p in t.GetProperties(FFlags))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (!p.CanRead) continue;
                object v;
                try { v = p.GetValue(obj); } catch (Exception ex) { v = $"<err: {ex.Message}>"; }
                sb.AppendLine($"  {p.PropertyType.Name,-28} {p.Name,-28} = {v}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  <reflection failed: {ex.Message}>"); }
        sb.AppendLine($"==================== END PROBE  ({_remaining} hits remaining) ====================");
        Log.Out(sb.ToString());
    }

    public static void Dump(EntityPlayer attacker, EntityPlayer victim, DamageSource src,
        string heldName, string resolvedClass, string resolvedBody,
        int rawDamage, int scaledDamage, float multiplier)
    {
        if (_remaining <= 0) return;
        _remaining--;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("==================== KitsunePvP PROBE ====================");
        sb.AppendLine($"attacker: id={attacker.entityId} name={attacker.EntityName}");
        sb.AppendLine($"victim:   id={victim.entityId}   name={victim.EntityName}");
        sb.AppendLine($"raw_damage={rawDamage}  scaled_damage={scaledDamage}  multiplier={multiplier:0.0000}");
        sb.AppendLine($"resolved: weapon_class={resolvedClass}  body_part={resolvedBody ?? "<null>"}");

        // === DamageSource fields ===
        sb.AppendLine("--- DamageSource (instance fields) ---");
        try
        {
            var t = src.GetType();
            sb.AppendLine($"  runtime_type: {t.FullName}");
            foreach (var f in t.GetFields(FFlags))
            {
                object v;
                try { v = f.GetValue(src); } catch (Exception ex) { v = $"<err: {ex.Message}>"; }
                sb.AppendLine($"  {f.FieldType.Name,-28} {f.Name,-28} = {v}");
            }
            // Useful properties too
            foreach (var p in t.GetProperties(FFlags))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = p.GetValue(src); } catch (Exception ex) { v = $"<err: {ex.Message}>"; }
                sb.AppendLine($"  [prop] {p.PropertyType.Name,-22} {p.Name,-28} = {v}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  <reflection failed: {ex.Message}>"); }

        // === EnumBodyPartHit values (so we know what body-part strings are valid) ===
        sb.AppendLine("--- EnumBodyPartHit members ---");
        try
        {
            var enumType = typeof(DamageSource).Assembly.GetType("EnumBodyPartHit")
                        ?? Type.GetType("EnumBodyPartHit");
            if (enumType != null)
            {
                foreach (var n in Enum.GetNames(enumType)) sb.Append("  ").Append(n);
                sb.AppendLine();
            }
            else sb.AppendLine("  <EnumBodyPartHit type not found>");
        }
        catch (Exception ex) { sb.AppendLine($"  <enum reflection failed: {ex.Message}>"); }

        // === Attacker held item ===
        sb.AppendLine("--- Attacker held item ---");
        try
        {
            var inv = attacker.inventory;
            var item = inv?.holdingItem;
            sb.AppendLine($"  holdingItem.Name: {item?.Name ?? "<null>"}");
            if (item != null)
            {
                // ItemClass.Name vs DisplayName etc.
                foreach (var p in new[] { "Name", "GetItemName", "DisplayName", "MadeOfMaterial", "type" })
                {
                    var pi = item.GetType().GetProperty(p, FFlags | BindingFlags.Static);
                    var fi = item.GetType().GetField(p, FFlags | BindingFlags.Static);
                    if (pi != null) { try { sb.AppendLine($"  ItemClass.{p,-18} = {pi.GetValue(item)}"); } catch { } }
                    else if (fi != null) { try { sb.AppendLine($"  ItemClass.{p,-18} = {fi.GetValue(item)}"); } catch { } }
                }
                // Tags
                var tagsField = item.GetType().GetField("ItemTags", FFlags) ?? item.GetType().GetField("Tags", FFlags);
                var tagsProp  = item.GetType().GetProperty("ItemTags", FFlags) ?? item.GetType().GetProperty("Tags", FFlags);
                object tagsVal = null;
                if (tagsField != null) try { tagsVal = tagsField.GetValue(item); } catch { }
                if (tagsVal == null && tagsProp != null) try { tagsVal = tagsProp.GetValue(item); } catch { }
                sb.AppendLine($"  ItemClass.Tags     = {tagsVal ?? "<not found>"}");
            }
            sb.AppendLine($"  resolved_pattern_match -> {resolvedClass}  (heldName='{heldName}')");
        }
        catch (Exception ex) { sb.AppendLine($"  <held-item reflection failed: {ex.Message}>"); }

        sb.AppendLine($"==================== END PROBE  ({_remaining} hits remaining) ====================");
        Log.Out(sb.ToString());
    }
}

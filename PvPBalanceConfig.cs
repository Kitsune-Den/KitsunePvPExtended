using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

public sealed class PvPBalanceConfig
{
    public string PresetName { get; private set; } = "medium";
    public bool Enabled { get; private set; } = true;
    public bool LogEveryHit { get; private set; } = false;
    public float GlobalMultiplier { get; private set; } = 0.6f;
    public float PerHitCapFractionMaxHp { get; private set; } = 0.5f;

    private readonly Dictionary<string, float> _weaponClassMults = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _bodyPartMults    = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string pattern, string cls)> _weaponPatterns = new List<(string, string)>();
    private const string DefaultWeaponClass = "default";
    private const string DefaultBodyPart    = "default";

    public static PvPBalanceConfig Current { get; private set; } = new PvPBalanceConfig();

    private static string _modRoot;
    private static string LiveConfigPath  => Path.Combine(_modRoot, "Config", "balance.xml");
    private static string PresetsDir      => Path.Combine(_modRoot, "Config", "presets");

    public static void Initialize(Mod mod)
    {
        _modRoot = mod?.Path ?? Directory.GetCurrentDirectory();
        Reload();
    }

    public static void Reload()
    {
        try
        {
            var path = LiveConfigPath;
            if (!File.Exists(path))
            {
                Log.Warning($"[KitsunePvP] balance.xml missing at {path} — using built-in defaults.");
                Current = new PvPBalanceConfig();
                Current.SeedBuiltinDefaults();
                return;
            }
            var doc = XDocument.Load(path);
            Current = Parse(doc);
            Log.Out($"[KitsunePvP] Reloaded balance.xml — preset={Current.PresetName} global={Current.GlobalMultiplier:0.00} cap={Current.PerHitCapFractionMaxHp:0.00}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KitsunePvP] Reload failed: {ex.Message}");
        }
    }

    public static bool ApplyPreset(string name)
    {
        var src = Path.Combine(PresetsDir, name + ".xml");
        if (!File.Exists(src))
        {
            Log.Warning($"[KitsunePvP] preset not found: {name} (looked in {PresetsDir})");
            return false;
        }
        try
        {
            File.Copy(src, LiveConfigPath, overwrite: true);
            Reload();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[KitsunePvP] preset apply failed: {ex.Message}");
            return false;
        }
    }

    public static IEnumerable<string> ListPresets()
    {
        if (!Directory.Exists(PresetsDir)) yield break;
        foreach (var f in Directory.GetFiles(PresetsDir, "*.xml"))
            yield return Path.GetFileNameWithoutExtension(f);
    }

    private void SeedBuiltinDefaults()
    {
        _weaponClassMults["default"]   = 1.0f;
        _weaponClassMults["pistol"]    = 0.85f;
        _weaponClassMults["smg"]       = 0.80f;
        _weaponClassMults["ar"]        = 0.75f;
        _weaponClassMults["shotgun"]   = 0.70f;
        _weaponClassMults["sniper"]    = 0.55f;
        _weaponClassMults["bow"]       = 0.85f;
        _weaponClassMults["melee"]     = 0.80f;
        _weaponClassMults["explosive"] = 0.60f;

        _bodyPartMults["default"] = 1.0f;
        _bodyPartMults["head"]    = 1.6f;
        _bodyPartMults["chest"]   = 1.0f;
        _bodyPartMults["arm"]     = 0.7f;
        _bodyPartMults["leg"]     = 0.75f;

        SeedDefaultPatterns();
    }

    private void SeedDefaultPatterns()
    {
        _weaponPatterns.Add(("sniperRifle", "sniper"));
        _weaponPatterns.Add(("huntingRifle","sniper"));
        _weaponPatterns.Add(("marksmanRifle","sniper"));
        _weaponPatterns.Add(("pistol",      "pistol"));
        _weaponPatterns.Add(("magnum",      "pistol"));
        _weaponPatterns.Add(("desertVulture","pistol"));
        _weaponPatterns.Add(("smg",         "smg"));
        _weaponPatterns.Add(("ak47",        "ar"));
        _weaponPatterns.Add(("m60",         "ar"));
        _weaponPatterns.Add(("tacticalAR",  "ar"));
        _weaponPatterns.Add(("shotgun",     "shotgun"));
        _weaponPatterns.Add(("bow",         "bow"));
        _weaponPatterns.Add(("crossbow",    "bow"));
        _weaponPatterns.Add(("rocketLauncher","explosive"));
        _weaponPatterns.Add(("pipeBomb",    "explosive"));
        _weaponPatterns.Add(("grenade",     "explosive"));
        _weaponPatterns.Add(("dynamite",    "explosive"));
        _weaponPatterns.Add(("club",        "melee"));
        _weaponPatterns.Add(("bat",         "melee"));
        _weaponPatterns.Add(("sledge",      "melee"));
        _weaponPatterns.Add(("knife",       "melee"));
        _weaponPatterns.Add(("machete",     "melee"));
        _weaponPatterns.Add(("axe",         "melee"));
        _weaponPatterns.Add(("spear",       "melee"));
    }

    public string ResolveWeaponClass(string heldItemName)
    {
        if (string.IsNullOrEmpty(heldItemName)) return DefaultWeaponClass;
        foreach (var (pattern, cls) in _weaponPatterns)
        {
            if (heldItemName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return cls;
        }
        return DefaultWeaponClass;
    }

    public float WeaponClassMultiplier(string cls)
    {
        if (cls != null && _weaponClassMults.TryGetValue(cls, out var m)) return m;
        return _weaponClassMults.TryGetValue(DefaultWeaponClass, out var d) ? d : 1f;
    }

    public float BodyPartMultiplier(string part)
    {
        if (part == null) return BodyPartMultiplier(DefaultBodyPart);
        if (_bodyPartMults.TryGetValue(part, out var m)) return m;
        // 7DTD body parts often come back as "Head"/"LeftLeg"/"RightArm" etc.
        // Normalize by substring match for limb categories.
        // Match against rig-bone names (hitTransformName) which 7DTD 2.0 sends
        // in the damage packet — e.g. "Head", "Hips", "Spine", "LeftUpLeg".
        var p = part.ToLowerInvariant();
        if (p.Contains("head") || p.Contains("neck"))                                                    return Lookup("head");
        if (p.Contains("hip")  || p.Contains("spine") || p.Contains("chest") ||
            p.Contains("torso") || p.Contains("body"))                                                   return Lookup("chest");
        if (p.Contains("arm")  || p.Contains("hand")  || p.Contains("shoulder") || p.Contains("clav"))   return Lookup("arm");
        if (p.Contains("leg")  || p.Contains("foot")  || p.Contains("knee")     || p.Contains("thigh"))  return Lookup("leg");
        return Lookup(DefaultBodyPart);
    }

    private float Lookup(string key) => _bodyPartMults.TryGetValue(key, out var v) ? v : 1f;

    private static PvPBalanceConfig Parse(XDocument doc)
    {
        var cfg = new PvPBalanceConfig();
        var root = doc.Root;
        if (root == null) { cfg.SeedBuiltinDefaults(); return cfg; }

        cfg.PresetName              = (string)root.Attribute("preset") ?? "custom";
        cfg.Enabled                 = ParseBool(root.Element("enabled"),     true);
        cfg.LogEveryHit             = ParseBool(root.Element("logEveryHit"), false);
        cfg.GlobalMultiplier        = ParseFloat(root.Element("globalMultiplier"),       0.6f);
        cfg.PerHitCapFractionMaxHp  = ParseFloat(root.Element("perHitCapFractionMaxHp"), 0.5f);

        var weapons = root.Element("weaponClasses");
        if (weapons != null)
        {
            foreach (var el in weapons.Elements("class"))
            {
                var name = (string)el.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                cfg._weaponClassMults[name] = (float?)el.Attribute("multiplier") ?? 1f;
                foreach (var pat in el.Elements("itemPattern"))
                {
                    var p = pat.Value?.Trim();
                    if (!string.IsNullOrEmpty(p)) cfg._weaponPatterns.Add((p, name));
                }
            }
        }

        var bodyParts = root.Element("bodyParts");
        if (bodyParts != null)
        {
            foreach (var el in bodyParts.Elements("part"))
            {
                var name = (string)el.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                cfg._bodyPartMults[name] = (float?)el.Attribute("multiplier") ?? 1f;
            }
        }

        // Fill any missing categories with built-in defaults so partial configs work.
        var fallback = new PvPBalanceConfig();
        fallback.SeedBuiltinDefaults();
        foreach (var kv in fallback._weaponClassMults) if (!cfg._weaponClassMults.ContainsKey(kv.Key)) cfg._weaponClassMults[kv.Key] = kv.Value;
        foreach (var kv in fallback._bodyPartMults)    if (!cfg._bodyPartMults.ContainsKey(kv.Key))    cfg._bodyPartMults[kv.Key]    = kv.Value;
        if (cfg._weaponPatterns.Count == 0) cfg._weaponPatterns.AddRange(fallback._weaponPatterns);

        return cfg;
    }

    private static bool ParseBool(XElement el, bool fallback) =>
        el != null && bool.TryParse(el.Value, out var b) ? b : fallback;
    private static float ParseFloat(XElement el, float fallback) =>
        el != null && float.TryParse(el.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;

    public string DumpEffective()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"preset: {PresetName}");
        sb.AppendLine($"enabled: {Enabled}");
        sb.AppendLine($"global: {GlobalMultiplier:0.000}");
        sb.AppendLine($"perHitCapFractionMaxHp: {PerHitCapFractionMaxHp:0.000}");
        sb.AppendLine($"logEveryHit: {LogEveryHit}");
        sb.AppendLine("weaponClasses:");
        foreach (var kv in _weaponClassMults.OrderBy(k => k.Key)) sb.AppendLine($"  {kv.Key}: {kv.Value:0.000}");
        sb.AppendLine("bodyParts:");
        foreach (var kv in _bodyPartMults.OrderBy(k => k.Key))    sb.AppendLine($"  {kv.Key}: {kv.Value:0.000}");
        sb.AppendLine($"weaponPatterns: {_weaponPatterns.Count} rules");
        return sb.ToString();
    }
}

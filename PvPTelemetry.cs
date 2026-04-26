using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public static class PvPTelemetry
{
    private static readonly object _lock = new object();
    private static string _logDir;

    private struct Hit
    {
        public DateTime When;
        public int AttackerId, VictimId;
        public string AttackerName, VictimName;
        public string Weapon, WeaponClass, BodyPart;
        public int RawDamage, ScaledDamage;
        public float Multiplier;
        public int VictimHpAfter;
        public bool Killed;
        public float Distance;
    }

    private static readonly List<Hit> _recent = new List<Hit>();
    private const int RecentCap = 1024;

    public static void Initialize(Mod mod)
    {
        var modRoot = mod?.Path ?? Directory.GetCurrentDirectory();
        _logDir = Path.Combine(modRoot, "Logs");
        try { Directory.CreateDirectory(_logDir); }
        catch (Exception ex) { Log.Warning($"[KitsunePvP] could not create log dir: {ex.Message}"); }
    }

    public static void LogHit(EntityPlayer attacker, EntityPlayer victim,
        string weapon, string weaponClass, string bodyPart,
        int rawDamage, int scaledDamage, float multiplier)
    {
        try
        {
            var hit = new Hit
            {
                When = DateTime.UtcNow,
                AttackerId = attacker.entityId,
                AttackerName = attacker.EntityName,
                VictimId = victim.entityId,
                VictimName = victim.EntityName,
                Weapon = weapon ?? "",
                WeaponClass = weaponClass ?? "",
                BodyPart = bodyPart ?? "",
                RawDamage = rawDamage,
                ScaledDamage = scaledDamage,
                Multiplier = multiplier,
                VictimHpAfter = SafeHp(victim) - scaledDamage,
                Killed = (SafeHp(victim) - scaledDamage) <= 0,
                Distance = Vector3.Distance(attacker.position, victim.position),
            };

            lock (_lock)
            {
                _recent.Add(hit);
                if (_recent.Count > RecentCap) _recent.RemoveAt(0);
            }

            AppendCsv(hit);
        }
        catch (Exception ex)
        {
            Log.Warning($"[KitsunePvP] telemetry failed: {ex.Message}");
        }
    }

    private static int SafeHp(EntityPlayer p) { try { return p.Health; } catch { return 0; } }

    private static void AppendCsv(Hit h)
    {
        if (_logDir == null) return;
        var file = Path.Combine(_logDir, $"pvp-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        bool newFile = !File.Exists(file);
        try
        {
            using (var w = new StreamWriter(file, append: true))
            {
                if (newFile)
                {
                    w.WriteLine("ts_utc,attacker_id,attacker_name,victim_id,victim_name,weapon,weapon_class,body_part,raw_dmg,scaled_dmg,multiplier,victim_hp_after,killed,distance_m");
                }
                w.WriteLine(string.Join(",",
                    h.When.ToString("o", CultureInfo.InvariantCulture),
                    h.AttackerId.ToString(CultureInfo.InvariantCulture),
                    Csv(h.AttackerName),
                    h.VictimId.ToString(CultureInfo.InvariantCulture),
                    Csv(h.VictimName),
                    Csv(h.Weapon),
                    Csv(h.WeaponClass),
                    Csv(h.BodyPart),
                    h.RawDamage.ToString(CultureInfo.InvariantCulture),
                    h.ScaledDamage.ToString(CultureInfo.InvariantCulture),
                    h.Multiplier.ToString("0.0000", CultureInfo.InvariantCulture),
                    h.VictimHpAfter.ToString(CultureInfo.InvariantCulture),
                    h.Killed ? "1" : "0",
                    h.Distance.ToString("0.00", CultureInfo.InvariantCulture)));
            }
        }
        catch (Exception ex) { Log.Warning($"[KitsunePvP] csv write failed: {ex.Message}"); }
    }

    private static string Csv(string s) =>
        string.IsNullOrEmpty(s) ? "" : (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);

    public static string SummaryReport(int lookbackMinutes = 60)
    {
        Hit[] snap;
        lock (_lock) snap = _recent.ToArray();
        var cutoff = DateTime.UtcNow.AddMinutes(-lookbackMinutes);
        var window = snap.Where(h => h.When >= cutoff).ToArray();
        if (window.Length == 0) return $"No PvP hits in last {lookbackMinutes}m.";

        int kills = window.Count(h => h.Killed);
        var byClass = window.GroupBy(h => h.WeaponClass)
            .OrderByDescending(g => g.Count())
            .Select(g => $"  {g.Key,-10} hits={g.Count(),3} avgDmg={g.Average(x => x.ScaledDamage):0.0} avgMult={g.Average(x => x.Multiplier):0.00}")
            .ToList();

        // TTK estimate per attacker: avg seconds between first hit and kill in same engagement
        var ttks = new List<double>();
        foreach (var g in window.GroupBy(h => (h.AttackerId, h.VictimId)))
        {
            var killHit = g.FirstOrDefault(h => h.Killed);
            if (killHit.AttackerId == 0 && !killHit.Killed) continue;
            var first = g.OrderBy(h => h.When).First();
            ttks.Add((killHit.When - first.When).TotalSeconds);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== PvP last {lookbackMinutes}m === hits={window.Length} kills={kills}");
        sb.AppendLine("by weapon class:");
        foreach (var line in byClass) sb.AppendLine(line);
        if (ttks.Count > 0)
        {
            ttks.Sort();
            sb.AppendLine($"TTK seconds: n={ttks.Count} median={Median(ttks):0.00} p90={Percentile(ttks, 0.9):0.00} avg={ttks.Average():0.00}");
        }
        return sb.ToString();
    }

    private static double Median(List<double> sorted) =>
        sorted.Count == 0 ? 0 :
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] :
        (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = Mathf.Clamp(Mathf.RoundToInt((float)((sorted.Count - 1) * p)), 0, sorted.Count - 1);
        return sorted[idx];
    }
}

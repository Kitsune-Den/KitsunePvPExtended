using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// What the damage prefix saw on one PvP hit, carried to the postfix via
/// Harmony's __state so the CSV row can include what actually happened.
/// </summary>
public class PvPHitState
{
    public DateTime When;
    public EntityPlayer Attacker, Victim;
    public string Weapon, WeaponClass, BodyPart;
    public int RawDamage, ScaledDamage, HpBefore;
    public float Multiplier;
    public bool FlagsKnown, FatalIn, FatalCleared;
}

public static class PvPTelemetry
{
    private static readonly object _lock = new object();
    private static string _logDir;

    // How long after a hit a reported death still counts as caused by it.
    private const float OutcomeWindowSeconds = 3f;

    private const string Header =
        "ts_utc,attacker_id,attacker_name,victim_id,victim_name,weapon,weapon_class,body_part,raw_dmg,scaled_dmg,multiplier," +
        "victim_hp_after,killed,distance_m,victim_hp_before,fatal_in,fatal_cleared,died_within_3s";

    private struct Hit
    {
        public DateTime When;
        public int AttackerId, VictimId;
        public string AttackerName, VictimName;
        public string Weapon, WeaponClass, BodyPart;
        public int RawDamage, ScaledDamage;
        public float Multiplier;
        public int VictimHpBefore, VictimHpAfter;
        public bool Killed;          // predicted: hp_before - scaled <= 0
        public float Distance;
        public bool FlagsKnown, FatalIn, FatalCleared;
        public bool? Died;           // observed within OutcomeWindowSeconds; null = couldn't check
    }

    private static readonly List<Hit> _recent = new List<Hit>();
    private const int RecentCap = 1024;

    // Server-side death reports per victim entity id (UTC). Fed by the
    // GameMessageServer hook, which fires when a player's own client reports
    // their death, so it covers deaths the server never decided itself.
    private static readonly Dictionary<int, DateTime> _deaths = new Dictionary<int, DateTime>();

    public static void Initialize(Mod mod)
    {
        var modRoot = mod?.Path ?? Directory.GetCurrentDirectory();
        _logDir = Path.Combine(modRoot, "Logs");
        try { Directory.CreateDirectory(_logDir); }
        catch (Exception ex) { Log.Warning($"[KitsunePvP] could not create log dir: {ex.Message}"); }
    }

    public static void RecordDeath(int entityId)
    {
        lock (_lock) _deaths[entityId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Called after the game has processed the hit. Waits briefly to see
    /// whether the victim dies, then writes the row. Falls back to writing
    /// immediately (died unknown) if no coroutine host is available.
    /// </summary>
    public static void LogHitOutcome(PvPHitState s)
    {
        try
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.StartCoroutine(AwaitOutcome(s));
            else Record(s, died: null);
        }
        catch (Exception ex)
        {
            Log.Warning($"[KitsunePvP] telemetry failed: {ex.Message}");
        }
    }

    private static IEnumerator AwaitOutcome(PvPHitState s)
    {
        yield return new WaitForSecondsRealtime(OutcomeWindowSeconds);
        bool died;
        lock (_lock)
        {
            died = _deaths.TryGetValue(s.Victim.entityId, out var at) && at >= s.When.AddSeconds(-0.5);
        }
        if (!died) died = SafeDead(s.Victim);
        Record(s, died);
    }

    private static void Record(PvPHitState s, bool? died)
    {
        try
        {
            var hit = new Hit
            {
                When = s.When,
                AttackerId = s.Attacker.entityId,
                AttackerName = s.Attacker.EntityName,
                VictimId = s.Victim.entityId,
                VictimName = s.Victim.EntityName,
                Weapon = s.Weapon ?? "",
                WeaponClass = s.WeaponClass ?? "",
                BodyPart = s.BodyPart ?? "",
                RawDamage = s.RawDamage,
                ScaledDamage = s.ScaledDamage,
                Multiplier = s.Multiplier,
                VictimHpBefore = s.HpBefore,
                VictimHpAfter = s.HpBefore - s.ScaledDamage,
                Killed = (s.HpBefore - s.ScaledDamage) <= 0,
                Distance = Vector3.Distance(s.Attacker.position, s.Victim.position),
                FlagsKnown = s.FlagsKnown,
                FatalIn = s.FatalIn,
                FatalCleared = s.FatalCleared,
                Died = died,
            };

            lock (_lock)
            {
                _recent.Add(hit);
                if (_recent.Count > RecentCap) _recent.RemoveAt(0);
            }

            // A death after a hit the scaled numbers say wasn't lethal is exactly
            // the bug reports we need to diagnose, so it's always logged.
            if (died == true && !hit.Killed)
            {
                Log.Warning($"[KitsunePvP] {hit.VictimName} died within {OutcomeWindowSeconds:0}s of a non-lethal scaled hit from " +
                            $"{hit.AttackerName} | weapon={hit.Weapon} hit={hit.BodyPart} raw={hit.RawDamage} scaled={hit.ScaledDamage} " +
                            $"hp_before={hit.VictimHpBefore} fatal_in={Flag(hit.FlagsKnown, hit.FatalIn)} fatal_cleared={Flag(hit.FlagsKnown, hit.FatalCleared)}");
            }
            else if (PvPBalanceConfig.Current.LogEveryHit)
            {
                Log.Out($"[KitsunePvP] outcome {hit.AttackerName} -> {hit.VictimName} | died={(died == null ? "?" : died.Value ? "1" : "0")} " +
                        $"hp_before={hit.VictimHpBefore} scaled={hit.ScaledDamage}");
            }

            AppendCsv(hit);
        }
        catch (Exception ex)
        {
            Log.Warning($"[KitsunePvP] telemetry failed: {ex.Message}");
        }
    }

    private static string Flag(bool known, bool value) => known ? (value ? "1" : "0") : "?";

    private static bool SafeDead(EntityPlayer p) { try { return p.IsDead() || p.Health <= 0; } catch { return false; } }

    private static void AppendCsv(Hit h)
    {
        if (_logDir == null) return;
        var file = Path.Combine(_logDir, $"pvp-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        // A file started by an older version has fewer columns; don't mix row shapes.
        if (File.Exists(file) && FirstLine(file) != Header)
            file = Path.Combine(_logDir, $"pvp-{DateTime.UtcNow:yyyy-MM-dd}-v2.csv");
        bool newFile = !File.Exists(file);
        try
        {
            using (var w = new StreamWriter(file, append: true))
            {
                if (newFile)
                {
                    w.WriteLine(Header);
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
                    h.Distance.ToString("0.00", CultureInfo.InvariantCulture),
                    h.VictimHpBefore.ToString(CultureInfo.InvariantCulture),
                    Flag(h.FlagsKnown, h.FatalIn),
                    Flag(h.FlagsKnown, h.FatalCleared),
                    h.Died == null ? "" : h.Died.Value ? "1" : "0"));
            }
        }
        catch (Exception ex) { Log.Warning($"[KitsunePvP] csv write failed: {ex.Message}"); }
    }

    private static string FirstLine(string file)
    {
        try { using (var r = new StreamReader(file)) return r.ReadLine(); }
        catch { return null; }
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

        // Observed deaths where we have them; the prediction only when we don't.
        int kills = window.Count(IsKill);
        int unexpected = window.Count(h => h.Died == true && !h.Killed);
        var byClass = window.GroupBy(h => h.WeaponClass)
            .OrderByDescending(g => g.Count())
            .Select(g => $"  {g.Key,-10} hits={g.Count(),3} avgDmg={g.Average(x => x.ScaledDamage):0.0} avgMult={g.Average(x => x.Multiplier):0.00}")
            .ToList();

        // TTK estimate per attacker: avg seconds between first hit and kill in same engagement
        var ttks = new List<double>();
        foreach (var g in window.GroupBy(h => (h.AttackerId, h.VictimId)))
        {
            var killHit = g.FirstOrDefault(IsKill);
            if (killHit.AttackerId == 0 && !IsKill(killHit)) continue;
            var first = g.OrderBy(h => h.When).First();
            ttks.Add((killHit.When - first.When).TotalSeconds);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== PvP last {lookbackMinutes}m === hits={window.Length} kills={kills}" +
                      (unexpected > 0 ? $" (deaths after non-lethal scaled hits: {unexpected})" : ""));
        sb.AppendLine("by weapon class:");
        foreach (var line in byClass) sb.AppendLine(line);
        if (ttks.Count > 0)
        {
            ttks.Sort();
            sb.AppendLine($"TTK seconds: n={ttks.Count} median={Median(ttks):0.00} p90={Percentile(ttks, 0.9):0.00} avg={ttks.Average():0.00}");
        }
        return sb.ToString();
    }

    private static bool IsKill(Hit h) => h.Died ?? h.Killed;

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

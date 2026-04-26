# KitsunePvPExtended

**Server-side PvP damage rebalance for 7 Days to Die 2.0. PvE stays 100% vanilla.**

Replaces the "one-shot, one-kill" feel of vanilla PvP with longer, tactical engagements — without ever touching damage to zombies, animals, or blocks. Built for the [community bounty BB-001](https://community.thefunpimps.com/resources/mod-release-pvp-balance-mod-help-us-test.118/).

## Highlights

- **Server-side only.** No client install, no client downloads. Players connect with vanilla clients.
- **PvE strictly untouched.** The damage scaling is gated on *both* attacker and victim being players; zombies, animals, environment, fall damage, and traps all flow through unchanged.
- **Per-weapon-class scaling** (sniper / pistol / shotgun / smg / ar / bow / melee / explosive — admin-extensible).
- **Headshot, chest, arm, leg multipliers** keyed off the actual rig bone (`Head`, `Spine2`, `Hips`, `RightUpLeg`, ...).
- **Per-hit damage cap** (% of max HP) — kills the full-HP one-shot edge case regardless of weapon.
- **Hot-reloadable XML config** + **bundled balance presets** (vanilla-light / casual / medium / hardcore) hot-swappable from the console.
- **Per-engagement telemetry** — every PvP hit logged to a daily CSV (TTK histograms, per-class avg damage, etc.).

## Requirements

- 7 Days to Die **2.0+**
- EAC **disabled** on the server (Harmony requirement; clients can keep EAC on)
- Server only — clients **do not** install this mod

## Installation

1. Extract the release zip into your server's `Mods/` folder. The zip contains a single top-level `KitsunePvPExtended/` folder that lands directly into `Mods/`.
2. Restart the server.
3. On first connect, look for these log lines:
   ```
   [KitsunePvP] Patched NetPackageDamageEntity.ProcessPackage
   [KitsunePvP] Loaded v0.1.0 — preset: medium, global: 0.60
   ```

That's the entire install. Players connect normally.

## Presets

Ships with four bundled balance profiles. Hot-swap any time via the console:

```
kpvp preset hardcore
```

| Preset          | Global | Per-hit cap | Feel                                                    |
|-----------------|:------:|:-----------:|---------------------------------------------------------|
| `vanilla-light` | 0.85   | 0.90        | Just shaves the most extreme one-shot spikes.           |
| `casual`        | 0.70   | 0.65        | Longer fights, vanilla flow recognizable.               |
| `medium`        | 0.60   | 0.50        | Default. Meaningful softening without dramatic change.  |
| `hardcore`      | 0.50   | 0.40        | Counter-Strike-feel sustained engagements.              |

Beyond the global multiplier, each preset tunes per-weapon-class and per-body-part multipliers. Open the preset XML files to see exact values, or run `kpvp dump`.

## Console commands

All `kpvp` subcommands run on the server console (or via remote admin tooling).

| Command                 | What it does                                                                  |
|-------------------------|-------------------------------------------------------------------------------|
| `kpvp reload`           | Reload `Config/balance.xml` after a manual edit.                              |
| `kpvp preset <name>`    | Overwrite `balance.xml` from `Config/presets/<name>.xml` and reload.          |
| `kpvp presets`          | List available presets.                                                       |
| `kpvp dump`             | Print the currently effective config.                                         |
| `kpvp stats [minutes]`  | TTK histogram + per-class hit summary from in-memory ring (default 60 min).   |
| `kpvp probe [count]`    | Dump full reflection on the next *N* PvP packets (defaults to 5).             |
| `kpvp probe off`        | Disarm the probe.                                                             |
| `kpvp introspect`       | Static reflection dump of all `*Damage*` methods/types — engine-version aid.  |
| `kpvp trace`            | (diagnostic-only — disabled in release builds).                               |

## Config schema

`Config/balance.xml` — admin-editable, reloaded via `kpvp reload`. Default ships as a copy of `presets/medium.xml`.

```xml
<balance preset="medium">
  <enabled>true</enabled>
  <logEveryHit>false</logEveryHit>
  <globalMultiplier>0.60</globalMultiplier>
  <perHitCapFractionMaxHp>0.50</perHitCapFractionMaxHp>

  <weaponClasses>
    <class name="sniper" multiplier="0.55">
      <itemPattern>sniperRifle</itemPattern>
      <itemPattern>huntingRifle</itemPattern>
    </class>
    <!-- ... more classes ... -->
  </weaponClasses>

  <bodyParts>
    <part name="head"  multiplier="1.60" />
    <part name="chest" multiplier="1.00" />
    <part name="arm"   multiplier="0.70" />
    <part name="leg"   multiplier="0.75" />
  </bodyParts>
</balance>
```

Item-name patterns are substring-matched against the attacking weapon's `ItemClass.Name`. Body parts are matched against the rig bone name (`Head`, `Spine2`, `Hips`, `RightUpLeg`, `LeftLowerArm`, ...) with category-substring fallback (`spine` → chest, `arm` / `hand` / `clav` → arm, `leg` / `foot` / `knee` / `thigh` → leg, etc.).

## Telemetry

Every scaled hit is appended to `Mods/KitsunePvPExtended/Logs/pvp-YYYY-MM-DD.csv`:

```
ts_utc, attacker_id, attacker_name, victim_id, victim_name, weapon, weapon_class,
body_part, raw_dmg, scaled_dmg, multiplier, victim_hp_after, killed, distance_m
```

Pull into your tool of choice for histograms / per-class TTK / engagement tuning. The in-memory ring (default 1024 entries) backs `kpvp stats`.

## How it works

7DTD 2.0's PvP damage flow is **client-authoritative**: the client computes a damage value, sends it to the server in a `NetPackageDamageEntity` packet, and the server applies it without recomputation. This bypasses the public `EntityAlive.DamageEntity` / `damageEntityLocal` API entirely — confirmed empirically via trace patches on every plausible candidate method.

KitsunePvPExtended attaches a **Harmony prefix to `NetPackageDamageEntity.ProcessPackage`**, the server-side network handler. The prefix:

1. Resolves `attackerEntityId` + `entityId` to live entities.
2. Returns immediately if either is not an `EntityPlayer` — preserving 100% vanilla PvE behavior by construction.
3. Reads the packet's `attackingItem.ItemClass.Name` and `hitTransformName` for weapon/body classification.
4. Applies `globalMultiplier × weaponClassMultiplier × bodyPartMultiplier`, then clamps to the per-hit max-HP fraction.
5. Writes the scaled value back into the packet's `strength` field (typed `UInt16` in 2.0 — written via `Convert.ChangeType`).
6. Logs the hit to telemetry.

Because the mutation happens on the inbound packet **before** the response chain (`ProcessDamageResponse → ApplyLocalBodyDamage`) processes it, the scaled value flows naturally to the victim's HP, the kill feed, achievements, and any downstream observers.

## Limitations & caveats

- **Modded weapons** that don't substring-match the bundled `<itemPattern>` rules will fall through to the `default` weapon class (1.0x). Add patterns to `balance.xml` and `kpvp reload`.
- **Custom rig bones** beyond the standard 7DTD humanoid set will fall through to `default` body-part. Use `kpvp probe` to discover any unusual transform names.
- The mod scales damage **after** the client's vanilla calculations (including armor mitigation, stealth bonus, head-shot critical, etc.) — it's a final multiplier. Per-hit damage cap is applied last.
- Client-authoritative damage is itself a security concern (a malicious client could send arbitrary damage values), but that's an upstream 7DTD design choice, not introduced by this mod.

## Credits

Built by **[AdaInTheLab](https://github.com/AdaInTheLab)** for community bounty **BB-001**. Open to contributions and bug reports.

## License

MIT — see [LICENSE](LICENSE).

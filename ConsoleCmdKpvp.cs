using System.Collections.Generic;

public class ConsoleCmdKpvp : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "kpvp" };

    public override string getDescription() =>
        "KitsunePvPExtended: reload | preset <name> | presets | dump | stats [minutes] | probe [count|off] | introspect | trace [on|off]";

    public override bool IsExecuteOnClient => false;
    public override bool AllowedInMainMenu => false;

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        if (_params.Count == 0)
        {
            Log.Out("Usage: kpvp reload | kpvp preset <name> | kpvp presets | kpvp dump | kpvp stats [minutes] | kpvp probe [count|off]");
            return;
        }

        switch (_params[0].ToLowerInvariant())
        {
            case "reload":
                PvPBalanceConfig.Reload();
                Log.Out($"[KitsunePvP] reloaded — preset={PvPBalanceConfig.Current.PresetName} global={PvPBalanceConfig.Current.GlobalMultiplier:0.00}");
                break;

            case "preset":
                if (_params.Count < 2) { Log.Out("Usage: kpvp preset <name>"); return; }
                if (PvPBalanceConfig.ApplyPreset(_params[1]))
                    Log.Out($"[KitsunePvP] preset applied: {_params[1]} (global={PvPBalanceConfig.Current.GlobalMultiplier:0.00})");
                break;

            case "presets":
                Log.Out("[KitsunePvP] available presets:");
                foreach (var p in PvPBalanceConfig.ListPresets()) Log.Out($"  {p}");
                break;

            case "dump":
                Log.Out("[KitsunePvP] effective config:\n" + PvPBalanceConfig.Current.DumpEffective());
                break;

            case "stats":
                int mins = 60;
                if (_params.Count >= 2) int.TryParse(_params[1], out mins);
                Log.Out(PvPTelemetry.SummaryReport(mins));
                break;

            case "probe":
                if (_params.Count >= 2 && _params[1].Equals("off", System.StringComparison.OrdinalIgnoreCase))
                {
                    PvPProbe.Disarm();
                    break;
                }
                int count = 5;
                if (_params.Count >= 2) int.TryParse(_params[1], out count);
                PvPProbe.Arm(count);
                break;

            case "introspect":
                Log.Out(PvPIntrospect.Run());
                break;

            case "trace":
                Log.Out("[KitsunePvP/trace] trace patches not registered in this build (diagnostic-only). " +
                        "To enable, restore PvPTrace.Initialize(harmony) in KitsunePvPExtendedMod.InitMod and rebuild.");
                break;

            default:
                Log.Out($"unknown subcommand: {_params[0]}");
                break;
        }
    }
}

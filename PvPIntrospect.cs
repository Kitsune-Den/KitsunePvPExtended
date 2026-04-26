using System;
using System.Linq;
using System.Reflection;
using System.Text;

/// <summary>
/// Static reflection dump to find the real damage entry point in 7D2D 2.0.
/// `kpvp introspect` lists every method whose name contains "amage" on the
/// likely-damage-bearing types, plus parameter signatures, so we can see
/// what's actually available to patch without decompiling.
/// </summary>
public static class PvPIntrospect
{
    private const BindingFlags MFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("==================== KitsunePvP INTROSPECT ====================");

        // Primary candidates
        DumpType(sb, typeof(EntityAlive),  matchSubstr: "amage");
        DumpType(sb, typeof(EntityPlayer), matchSubstr: "amage");

        // Secondary: try common related types if they exist
        TryDumpByName(sb, "EntityClass",       matchSubstr: "amage");
        TryDumpByName(sb, "EntityPlayerLocal", matchSubstr: "amage");
        TryDumpByName(sb, "EntityPlayerMP",    matchSubstr: "amage");
        TryDumpByName(sb, "Stats",             matchSubstr: "ealth");
        TryDumpByName(sb, "EntityStats",       matchSubstr: "ealth");

        // Look for any type in the assembly with "Damage" in its name (entry-point classes)
        sb.AppendLine("--- Types in Assembly-CSharp containing 'Damage' in their name ---");
        try
        {
            var asm = typeof(EntityAlive).Assembly;
            var damageTypes = asm.GetTypes()
                .Where(t => t.Name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t.FullName)
                .Take(40)
                .ToArray();
            foreach (var t in damageTypes) sb.AppendLine($"  {t.FullName}");
            sb.AppendLine($"  ({damageTypes.Length} shown)");
        }
        catch (Exception ex) { sb.AppendLine($"  <type enum failed: {ex.Message}>"); }

        sb.AppendLine("==================== END INTROSPECT ====================");
        return sb.ToString();
    }

    private static void DumpType(StringBuilder sb, Type t, string matchSubstr)
    {
        sb.AppendLine($"--- {t.FullName}  (methods containing '{matchSubstr}') ---");
        try
        {
            var methods = t.GetMethods(MFlags)
                .Where(m => m.Name.IndexOf(matchSubstr, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.Name);

            foreach (var m in methods)
            {
                string declared = m.DeclaringType == t ? "" : $"  [from {m.DeclaringType?.Name}]";
                string virt = m.IsVirtual ? "virtual " : "";
                string stat = m.IsStatic ? "static " : "";
                var ps = string.Join(", ", m.GetParameters().Select(p =>
                    $"{(p.IsOut ? "out " : (p.ParameterType.IsByRef ? "ref " : ""))}{Strip(p.ParameterType.Name)} {p.Name}"));
                sb.AppendLine($"  {stat}{virt}{Strip(m.ReturnType.Name)} {m.Name}({ps}){declared}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  <reflection failed: {ex.Message}>"); }
    }

    private static void TryDumpByName(StringBuilder sb, string typeName, string matchSubstr)
    {
        var asm = typeof(EntityAlive).Assembly;
        var t = asm.GetType(typeName) ?? Type.GetType(typeName);
        if (t == null) { sb.AppendLine($"--- {typeName}: not found ---"); return; }
        DumpType(sb, t, matchSubstr);
    }

    private static string Strip(string s) => s.Replace("`1", "").Replace("`2", "");
}

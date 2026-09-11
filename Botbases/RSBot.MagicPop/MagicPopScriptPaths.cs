using System.Collections.Generic;
using System.IO;
using RSBot.Core.Components;

namespace RSBot.MagicPop;

internal static class MagicPopScriptPaths
{
    private static string Directory => Path.Combine(ScriptManager.InitialDirectory, "MagicPop");

    public static string TeleportToMachine => Path.Combine(Directory, "HotanTeleportToMagicPop.rbs");
    public static string MachineToPotion => Path.Combine(Directory, "HotanMagicPopToPotion.rbs");
    public static string PotionToMachine => Path.Combine(Directory, "HotanPotionToMagicPop.rbs");

    public static IEnumerable<string> All
    {
        get
        {
            yield return TeleportToMachine;
            yield return MachineToPotion;
            yield return PotionToMachine;
        }
    }
}

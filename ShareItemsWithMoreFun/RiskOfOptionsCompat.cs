using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;

namespace ShareItemsWithMoreFun
{
    /// <summary>Menu de configuration en jeu, seulement si Risk Of Options est installé.</summary>
    internal static class RiskOfOptionsCompat
    {
        public const string Guid = "com.rune580.riskofoptions";

        public static bool Enabled => Chainloader.PluginInfos.ContainsKey(Guid);

        // NoInlining : les types de Risk Of Options ne sont chargés que si le mod est présent.
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Register()
        {
            ModSettingsManager.SetModDescription(
                "Chaque coffre s'ouvre une fois par joueur et donne une orbe à choisir parmi plusieurs items de même rareté.");
            ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.Enabled));
            ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.InstancedChests));
            ModSettingsManager.AddOption(new IntSliderOption(ModConfig.ChoiceCount, new IntSliderConfig { min = 2, max = 6 }));
            ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.ReserveOrbs));
            ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.HideOtherPlayersOrbs));
            ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.GrantToInventory));
            ModSettingsManager.AddOption(new StringInputFieldOption(ModConfig.ChestNames));
            ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.VerboseLogging));
        }
    }
}

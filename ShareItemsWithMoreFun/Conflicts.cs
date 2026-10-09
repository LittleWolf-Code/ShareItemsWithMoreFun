using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;

namespace ShareItemsWithMoreFun
{
    /// <summary>Prévient dans le log quand un mod connu pour se chevaucher avec celui-ci est chargé.</summary>
    internal static class Conflicts
    {
        private static readonly string[] KnownConflicts =
        {
            "ThreeChoiceChests",
            "ItemShare",
            "ShareSuite",
            "InstancedLoot",
            "ArtifactOfPotential",
            "Artifact of Potential",
            "Equilibrium",
            "CatchupLoot",
        };

        public static void WarnAboutKnownConflicts()
        {
            var found = new List<string>();
            foreach (var plugin in Chainloader.PluginInfos.Values)
            {
                string guid = plugin.Metadata.GUID ?? string.Empty;
                string name = plugin.Metadata.Name ?? string.Empty;
                if (guid == Plugin.PluginGUID)
                {
                    continue;
                }
                foreach (string conflict in KnownConflicts)
                {
                    if (name.IndexOf(conflict, StringComparison.OrdinalIgnoreCase) >= 0
                        || guid.IndexOf(conflict, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found.Add($"{name} ({guid})");
                        break;
                    }
                }
            }
            if (found.Count > 0)
            {
                Plugin.Log.LogWarning("Mods qui modifient aussi les coffres ou le partage du butin : "
                    + string.Join(", ", found)
                    + ". Butin en double ou comportements étranges possibles : désactivez-les avec ShareItemsWithMoreFun.");
            }
        }
    }
}

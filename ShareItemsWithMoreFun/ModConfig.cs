using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace ShareItemsWithMoreFun
{
    internal static class ModConfig
    {
        // Noms des prefabs (sans "(Clone)") gérés par défaut. Voir le cahier des charges, section 4.
        private const string DefaultChestNames =
            "Chest1, Chest2, GoldChest, Chest1StealthedVariant, " +
            "CategoryChestDamage, CategoryChestHealing, CategoryChestUtility, " +
            "CategoryChest2Damage Variant, CategoryChest2Healing Variant, CategoryChest2Utility Variant, " +
            "Lockbox, EquipmentBarrel, LunarChest, VoidChest, VoidTriple, LockboxVoid";

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> InstancedChests;
        public static ConfigEntry<int> ChoiceCount;
        public static ConfigEntry<bool> ReserveOrbs;
        public static ConfigEntry<bool> HideOtherPlayersOrbs;
        public static ConfigEntry<bool> GrantToInventory;
        public static ConfigEntry<string> ChestNames;
        public static ConfigEntry<bool> VerboseLogging;

        private static HashSet<string> chestNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Master switch. When off, chests behave exactly like vanilla. Host setting.");

            InstancedChests = config.Bind("General", "InstancedChests", true,
                "Each player can open every chest once, on their side. The chest stays closed for the others. Host setting.");

            ChoiceCount = config.Bind("Orbs", "ChoiceCount", 3,
                new ConfigDescription("Number of items offered by each orb (same rarity as the rolled item). Host setting.",
                    new AcceptableValueRange<int>(2, 6)));

            ReserveOrbs = config.Bind("Orbs", "ReserveForOpener", true,
                "Only the player who opened the chest can open its orb. Host setting.");

            HideOtherPlayersOrbs = config.Bind("Orbs", "HideOtherPlayersOrbs", true,
                "Orbs belonging to another player are invisible. Host setting.");

            GrantToInventory = config.Bind("Orbs", "GrantToInventory", true,
                "The chosen item goes straight into the owner's inventory instead of dropping on the ground. Host setting.");

            ChestNames = config.Bind("Chests", "ChestNames", DefaultChestNames,
                "Comma-separated prefab names of the chests handled by the mod. Host setting.");

            VerboseLogging = config.Bind("Debug", "VerboseLogging", false,
                "Writes every chest and orb decision to the BepInEx log.");

            RebuildChestNames();
            ChestNames.SettingChanged += (_, __) => RebuildChestNames();
        }

        public static bool IsManagedChestName(string objectName)
        {
            return chestNameSet.Contains(NormalizeName(objectName));
        }

        public static string NormalizeName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return string.Empty;
            }
            const string cloneSuffix = "(Clone)";
            string name = objectName.Trim();
            while (name.EndsWith(cloneSuffix, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - cloneSuffix.Length).TrimEnd();
            }
            return name;
        }

        private static void RebuildChestNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (ChestNames.Value ?? string.Empty).Split(','))
            {
                string name = part.Trim();
                if (name.Length > 0)
                {
                    set.Add(name);
                }
            }
            chestNameSet = set;
        }
    }
}

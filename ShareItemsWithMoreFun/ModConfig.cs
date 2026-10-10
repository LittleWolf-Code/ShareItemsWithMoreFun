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
        public static ConfigEntry<bool> RemoteOpCanOpenChests;
        public static ConfigEntry<bool> InstancedMultiShops;
        public static ConfigEntry<bool> PersistentChestMarkers;
        public static ConfigEntry<bool> InstancedBossRewards;
        public static ConfigEntry<bool> InstancedChanceShrines;
        public static ConfigEntry<int> MultiplayerChestScaling;
        public static ConfigEntry<bool> FreezeDifficultyAfterTeleporter;
        public static ConfigEntry<bool> ReviveDeadPlayersAfterTeleporter;
        public static ConfigEntry<int> ChoiceCount;
        public static ConfigEntry<bool> ReserveOrbs;
        public static ConfigEntry<bool> HideOtherPlayersOrbs;
        public static ConfigEntry<bool> GrantToInventory;
        public static ConfigEntry<float> DisconnectGraceSeconds;
        public static ConfigEntry<string> ChestNames;
        public static ConfigEntry<bool> VerboseLogging;

        private static HashSet<string> chestNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Master switch. When off, chests behave exactly like vanilla. Host setting.");

            InstancedChests = config.Bind("General", "InstancedChests", true,
                "Each player can open every chest once, on their side. The chest stays closed for the others. Host setting.");

            RemoteOpCanOpenChests = config.Bind("Chests", "RemoteOpCanOpenChests", true,
                "A dead player piloting a drone (Alloyed Collective remote operation) can open the mod's chests on their side, " +
                "with their own money, and open their orbs. Vanilla drones cannot open chests. Host setting.");

            InstancedMultiShops = config.Bind("Chests", "InstancedMultiShops", true,
                "Multishops (white, green and equipment) work like the chests: each player can buy one terminal per multishop on their side, " +
                "and the multishop stays open for the others. The bought item is reserved to the buyer. Host setting.");

            PersistentChestMarkers = config.Bind("Chests", "PersistentChestMarkers", true,
                "When a player opens or pings a chest handled by the mod, it stays marked for every player who has not opened it yet " +
                "(like a ping, but with no time limit, no chat message and no sound). The marker disappears for a player once they open it. Host setting.");

            InstancedBossRewards = config.Bind("Bosses", "InstancedBossRewards", true,
                "The teleporter boss reward becomes choice orbs: each player gets (1 + Shrines of the Mountain) orbs of their own, " +
                "each with the rolled rarity (green, or yellow boss item at 15%), reserved to and only visible for that player. Host setting.");

            InstancedChanceShrines = config.Bind("Shrines", "InstancedChanceShrines", true,
                "Each player plays every Shrine of Chance on their side: their own price, their own attempts and their own successes " +
                "(2 per player, like vanilla). Each success drops a choice orb with the rolled rarity, reserved to and only visible for that player. " +
                "Off: vanilla shrines. Host setting.");

            MultiplayerChestScaling = config.Bind("Chests", "MultiplayerChestScaling", 100,
                new ConfigDescription(
                    "How many extra standard chests (small, large, category, cloaked, equipment barrels) the game keeps with several players, in %. " +
                    "100 = vanilla (x1.5 chests with 2 players, x2 with 3, x2.5 with 4). 0 = as many as in solo: since every player opens " +
                    "every chest on their side, each player then gets about a solo run's loot. Applied when a stage is generated. Host setting.",
                    new AcceptableValueRange<int>(0, 100)));

            FreezeDifficultyAfterTeleporter = config.Bind("Stage", "FreezeDifficultyAfterTeleporter", true,
                "Once the stage's teleporter is charged, the difficulty stops rising until the next stage (the run timer itself keeps going). Host setting.");

            ReviveDeadPlayersAfterTeleporter = config.Bind("Stage", "ReviveDeadPlayersAfterTeleporter", true,
                "Once the stage's teleporter is charged, dead players (including those piloting a drone) come back near the teleporter. Host setting.");

            ChoiceCount = config.Bind("Orbs", "ChoiceCount", 3,
                new ConfigDescription("Number of items offered by each orb (same rarity as the rolled item). Host setting.",
                    new AcceptableValueRange<int>(2, 6)));

            ReserveOrbs = config.Bind("Orbs", "ReserveForOpener", true,
                "Only the player who opened the chest can open its orb. Host setting.");

            HideOtherPlayersOrbs = config.Bind("Orbs", "HideOtherPlayersOrbs", true,
                "Orbs belonging to another player are invisible. Host setting.");

            GrantToInventory = config.Bind("Orbs", "GrantToInventory", true,
                "The chosen item goes straight into the owner's inventory instead of dropping on the ground. Host setting.");

            DisconnectGraceSeconds = config.Bind("Orbs", "DisconnectGraceSeconds", 180f,
                "When an orb's owner disconnects, it stays reserved this many seconds (time to relaunch and rejoin), " +
                "then becomes free for everyone. 0 = stays reserved until the end of the stage. Host setting.");

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

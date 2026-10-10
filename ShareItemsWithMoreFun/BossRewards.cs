using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Récompense du boss (cahier des charges, section 3 quater). Vanilla : (1 + autels de la
    /// montagne) × joueurs items au sol, tous le même item (vert, ou rouge pour certains boss),
    /// chacun avec 15 % de chance d'être un objet de boss. Avec le mod : les mêmes tirages, mais
    /// chaque joueur reçoit (1 + autels) gouttelettes à son nom, que la chaîne des coffres
    /// transforme en orbes à choix réservées et invisibles pour les autres.
    /// </summary>
    internal static class BossRewards
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo RngField = typeof(BossGroup).GetField("rng", Private);
        private static readonly FieldInfo BossDropsField = typeof(BossGroup).GetField("bossDrops", Private);
        private static readonly FieldInfo BossDropTablesField = typeof(BossGroup).GetField("bossDropTables", Private);

        public static void Install()
        {
            if (RngField == null || BossDropsField == null || BossDropTablesField == null)
            {
                Plugin.Log.LogWarning("Récompense du boss : champs de BossGroup introuvables, la récompense reste vanilla.");
                return;
            }
            On.RoR2.BossGroup.DropRewards += BossGroup_DropRewards;
        }

        private static void BossGroup_DropRewards(On.RoR2.BossGroup.orig_DropRewards orig, BossGroup self)
        {
            Run run = Run.instance;
            if (!NetworkServer.active || !run || !ModConfig.Enabled.Value || !ModConfig.InstancedBossRewards.Value
                || !self.scaleRewardsByPlayerCount || !self.dropPosition || !IsTeleporterBoss(self))
            {
                orig(self);
                return;
            }

            // Une part par joueur, comme vanilla (participatingPlayerCount compte aussi les absents).
            // Owner nul = part libre, visible et ouvrable par tous.
            bool reviveComing = ModConfig.ReviveDeadPlayersAfterTeleporter.Value;
            var players = new List<CharacterMaster>();
            foreach (PlayerCharacterMasterController controller in PlayerCharacterMasterController.instances)
            {
                if (!controller || !controller.master)
                {
                    continue;
                }
                bool canClaim = controller.isConnected && (reviveComing || IsAliveOrPiloting(controller.master));
                players.Add(canClaim ? controller.master : null);
            }
            Xoroshiro128Plus rng = RngField.GetValue(self) as Xoroshiro128Plus;
            if (players.Count == 0 || rng == null)
            {
                orig(self);
                return;
            }

            int dropped = 0;
            try
            {
                DropPerPlayer(self, run, players, rng, ref dropped);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Récompense du boss : {e}");
                if (dropped == 0)
                {
                    // Rien n'est tombé : on rend la récompense vanilla plutôt que rien.
                    orig(self);
                }
            }
        }

        /// <summary>Seulement le boss du téléporteur (y compris les groupes enfants du téléporteur, DLC4).</summary>
        private static bool IsTeleporterBoss(BossGroup boss)
        {
            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            return (teleporter && teleporter.bossGroup == boss) || boss.GetComponentInParent<TeleporterInteraction>();
        }

        private static bool IsAliveOrPiloting(CharacterMaster master)
        {
            CharacterBody body = master.GetBody();
            return (body && body.healthComponent && body.healthComponent.alive) || master.IsExtraLifePendingServer();
        }

        private static void DropPerPlayer(BossGroup boss, Run run, List<CharacterMaster> players, Xoroshiro128Plus rng, ref int dropped)
        {
            var bossDrops = BossDropsField.GetValue(boss) as List<UniquePickup>;
            var bossDropTables = BossDropTablesField.GetValue(boss) as List<PickupDropTable>;
            bool hasBossDrops = bossDrops != null && bossDrops.Count > 0;
            bool hasBossTables = bossDropTables != null && bossDropTables.Count > 0;

            // Même item de base que vanilla.
            UniquePickup basePickup;
            if (boss.dropTable)
            {
                basePickup = boss.dropTable.GeneratePickup(rng);
            }
            else
            {
                List<PickupIndex> list = boss.forceTier3Reward ? run.availableTier3DropList : run.availableTier2DropList;
                basePickup = new UniquePickup(rng.NextElementUniform(list));
            }

            int perPlayer = 1 + Math.Max(0, boss.bonusRewardCount);
            int total = perPlayer * players.Count;
            var choiceRng = new Xoroshiro128Plus(rng.nextUlong);

            // Même anneau de gouttelettes que vanilla.
            Vector3 velocity = Quaternion.AngleAxis(UnityEngine.Random.Range(0, 360), Vector3.up) * (Vector3.up * 40f + Vector3.forward * 5f);
            Quaternion step = Quaternion.AngleAxis(360f / total, Vector3.up);
            Vector3 position = boss.dropPosition.position;

            foreach (CharacterMaster player in players)
            {
                using (DropContext.Begin(player, convertToChoices: true, choiceRng))
                {
                    for (int i = 0; i < perPlayer; i++)
                    {
                        UniquePickup pickup = basePickup;
                        if ((hasBossDrops || hasBossTables) && rng.nextNormalizedFloat <= boss.bossDropChance)
                        {
                            if (hasBossTables)
                            {
                                PickupDropTable table = rng.NextElementUniform(bossDropTables);
                                if (table)
                                {
                                    pickup = table.GeneratePickup(rng);
                                }
                            }
                            else
                            {
                                pickup = rng.NextElementUniform(bossDrops);
                            }
                        }
                        PickupDropletController.CreatePickupDroplet(pickup, position, velocity, false);
                        dropped++;
                        velocity = step * velocity;
                    }
                }
            }
            Plugin.Debug($"Récompense du boss : {perPlayer} orbe(s) pour chacune des {players.Count} part(s), dont {players.FindAll(p => !p).Count} libre(s)");
        }
    }
}

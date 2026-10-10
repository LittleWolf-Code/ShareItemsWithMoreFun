using System;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Fin d'étape (cahier des charges, section 3 bis) : dès que le téléporteur est chargé,
    /// la difficulté est figée jusqu'à l'étape suivante et les joueurs morts reviennent près
    /// du téléporteur, pour finir d'ouvrir leurs coffres.
    /// </summary>
    internal static class StageEndHooks
    {
        private const float ImmunitySeconds = 3f;
        private const float FallbackSpawnRadius = 6f;

        public static void Install()
        {
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterCharged;
            ModConfig.FreezeDifficultyAfterTeleporter.SettingChanged += (_, __) => ReleaseIfDisabled();
            ModConfig.Enabled.SettingChanged += (_, __) => ReleaseIfDisabled();
            On.RoR2.CharacterMaster.SpawnRemoteOperationDrone += CharacterMaster_SpawnRemoteOperationDrone;
            On.RoR2.Run.AdvanceStage += Run_AdvanceStage;
            CharacterBody.onBodyStartGlobal += CloseDronePickerWhenAlive;
        }

        /// <summary>
        /// La difficulté doit repartir AVANT le passage d'étape : le jeu y recalcule la difficulté
        /// (nouveau nombre d'étapes) et la scène suivante s'en sert pour ses premiers monstres.
        /// </summary>
        private static void Run_AdvanceStage(On.RoR2.Run.orig_AdvanceStage orig, Run self, SceneDef nextScene)
        {
            DifficultyFreeze.ReleaseServer();
            orig(self, nextScene);
        }

        /// <summary>Client : un joueur ressuscité ne doit pas garder le sélecteur de drone ouvert à l'écran.</summary>
        private static void CloseDronePickerWhenAlive(CharacterBody body)
        {
            if (!body || body.isRemoteOp || !body.master || !PlayerResolver.IsLocalPlayer(body.master.netId))
            {
                return;
            }
            foreach (RoR2.UI.RemoteOperationDronePickerPanel panel in UnityEngine.Object.FindObjectsOfType<RoR2.UI.RemoteOperationDronePickerPanel>())
            {
                UnityEngine.Object.Destroy(panel.gameObject);
            }
        }

        private static void OnTeleporterCharged(TeleporterInteraction teleporter)
        {
            Run run = Run.instance;
            if (!NetworkServer.active || !run || !ModConfig.Enabled.Value || run.isGameOverServer)
            {
                return;
            }
            try
            {
                if (ModConfig.FreezeDifficultyAfterTeleporter.Value)
                {
                    DifficultyFreeze.FreezeServer();
                }
                if (ModConfig.ReviveDeadPlayersAfterTeleporter.Value)
                {
                    ReviveDeadPlayers(teleporter ? teleporter.transform.position : Vector3.zero);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Fin d'étape : {e}");
            }
        }

        private static void ReleaseIfDisabled()
        {
            if (!ModConfig.Enabled.Value || !ModConfig.FreezeDifficultyAfterTeleporter.Value)
            {
                DifficultyFreeze.ReleaseServer();
            }
        }

        // ---------------------------------------------------------------- Joueurs morts

        private static void ReviveDeadPlayers(Vector3 teleporterPosition)
        {
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                CharacterMaster master = player ? player.master : null;
                if (!master || !player.isConnected || !NeedsRevive(master))
                {
                    continue;
                }
                try
                {
                    Revive(master, teleporterPosition);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Impossible de ressusciter {master.name} : {e}");
                }
            }
        }

        /// <summary>Mort (sans corps vivant) ou aux commandes d'un drone, et sans résurrection déjà en cours.</summary>
        private static bool NeedsRevive(CharacterMaster master)
        {
            if (master.IsExtraLifePendingServer())
            {
                return false;
            }
            CharacterBody body = master.GetBody();
            bool alive = body && body.healthComponent && body.healthComponent.alive;
            return !alive || body.isRemoteOp;
        }

        /// <summary>Même retour que le Sanctuaire du façonnage (CharacterMaster.RespawnExtraLifeShrine), mais près du téléporteur.</summary>
        private static void Revive(CharacterMaster master, Vector3 teleporterPosition)
        {
            Vector3 position = FindSpawnPosition(master, teleporterPosition);
            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            // wasRevivedMidStage : le jeu retire la configuration « drone » et rend le vrai personnage.
            CharacterBody body = master.Respawn(new CharacterMaster.RespawnArgs
            {
                spawnBodyArgs = new CharacterMaster.SpawnBodyArgs
                {
                    spawnPointPosition = position,
                    spawnPointRotation = rotation
                },
                wasRevivedMidStage = true
            });
            if (!body)
            {
                return;
            }

            body.AddTimedBuff(RoR2Content.Buffs.Immune, ImmunitySeconds);
            // Pas d'animation d'apparition : le joueur est jouable tout de suite.
            foreach (EntityStateMachine stateMachine in body.GetComponents<EntityStateMachine>())
            {
                stateMachine.initialStateType = stateMachine.mainStateType;
            }
            GameObject effect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/fxHealAndReviveGold");
            if (effect)
            {
                EffectManager.SpawnEffect(effect, new EffectData { origin = position, rotation = body.transform.rotation }, transmit: true);
            }
            Util.PlaySound("Play_item_use_healAndRevive_activate", body.gameObject);
            Plugin.Debug($"{master.name} ressuscité près du téléporteur");
        }

        private static Vector3 FindSpawnPosition(CharacterMaster master, Vector3 teleporterPosition)
        {
            GameObject prefab = master.originalBodyPrefab ? master.originalBodyPrefab : master.bodyPrefab;
            CharacterBody prefabBody = prefab ? prefab.GetComponent<CharacterBody>() : null;
            if (prefabBody && Run.instance)
            {
                Vector3? safe = TeleportHelper.FindSafeTeleportDestination(teleporterPosition, prefabBody, Run.instance.runRNG);
                if (safe.HasValue)
                {
                    return safe.Value;
                }
            }
            Vector2 offset = UnityEngine.Random.insideUnitCircle * FallbackSpawnRadius;
            return teleporterPosition + new Vector3(offset.x, 1f, offset.y);
        }

        /// <summary>
        /// Le sélecteur de drone peut rester ouvert chez un joueur qu'on vient de ressusciter :
        /// le jeu détruirait alors son personnage vivant pour le remettre dans un drone.
        /// </summary>
        private static void CharacterMaster_SpawnRemoteOperationDrone(On.RoR2.CharacterMaster.orig_SpawnRemoteOperationDrone orig,
            CharacterMaster self, string droneBodyToSpawn, uint moneyToUse)
        {
            CharacterBody body = self.GetBody();
            if (NetworkServer.active && body && body.healthComponent && body.healthComponent.alive && !body.isRemoteOp)
            {
                Plugin.Debug($"Drone refusé pour {self.name} : déjà vivant");
                return;
            }
            orig(self, droneBodyToSpawn, moneyToUse);
        }
    }
}

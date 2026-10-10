using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Équilibrage des coffres en multijoueur (cahier des charges, section 3 ter). Le jeu génère
    /// ses objets interactifs avec des crédits × (0,5 + 0,5 × joueurs). Avec des coffres
    /// instanciés, chaque joueur ouvre déjà chaque coffre : le mod retire donc au hasard une part
    /// des coffres standards générés, pour viser coffres en solo × (1 + p × 0,5 × (joueurs − 1)).
    /// </summary>
    internal static class ChestBalance
    {
        // Coffres « standards » : les seuls que l'équilibrage peut retirer.
        private static readonly HashSet<string> BalanceableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Chest1", "Chest2", "Chest1StealthedVariant",
            "CategoryChestDamage", "CategoryChestHealing", "CategoryChestUtility",
            "CategoryChest2Damage Variant", "CategoryChest2Healing Variant", "CategoryChest2Utility Variant",
            "EquipmentBarrel",
        };

        private static readonly List<GameObject> spawnedChests = new List<GameObject>();
        private static bool collecting;

        public static void Install()
        {
            SceneDirector.onPrePopulateSceneServer += _ =>
            {
                spawnedChests.Clear();
                collecting = true;
            };
            SceneDirector.onSpawnInteractableServer += OnSpawnInteractable;
            SceneDirector.onPostPopulateSceneServer += OnPostPopulate;
        }

        private static void OnSpawnInteractable(SceneDirector director, GameObject spawned, DirectorCard card)
        {
            if (collecting && spawned && IsBalanceable(spawned.name))
            {
                spawnedChests.Add(spawned);
            }
        }

        private static bool IsBalanceable(string objectName)
        {
            string name = ModConfig.NormalizeName(objectName);
            return BalanceableNames.Contains(name) && ModConfig.IsManagedChestName(name);
        }

        private static void OnPostPopulate(SceneDirector director)
        {
            collecting = false;
            try
            {
                RemoveSurplusChests();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Équilibrage des coffres : {e}");
            }
            finally
            {
                spawnedChests.Clear();
            }
        }

        /// <summary>
        /// Part des coffres générés à garder (1 = tous, comme en vanilla). Les crédits vanilla
        /// valent (int)(base × (0,5 + 0,5 × joueurs)) + bonus de l'étape ; le bonus ne dépend pas
        /// du nombre de joueurs, il reste donc entier dans la cible.
        /// </summary>
        public static float KeepFraction(int players, float scaling, float baseCredits, float bonusCredits)
        {
            if (players <= 1 || scaling >= 1f)
            {
                return 1f;
            }
            if (baseCredits <= 0f)
            {
                baseCredits = 1f;
                bonusCredits = 0f;
            }
            float vanilla = Mathf.Floor(baseCredits * (0.5f + 0.5f * players)) + bonusCredits;
            float target = baseCredits * (1f + Mathf.Clamp01(scaling) * 0.5f * (players - 1)) + bonusCredits;
            return vanilla > 0f ? Mathf.Clamp01(target / vanilla) : 1f;
        }

        private static void RemoveSurplusChests()
        {
            Run run = Run.instance;
            if (!NetworkServer.active || !run || !ModConfig.Enabled.Value || !ModConfig.InstancedChests.Value)
            {
                return;
            }
            // Coffres non instanciés (Illusion) ou nombre de coffres qui ne dépend pas des joueurs (Simulacre).
            if (DelusionChestController.isDelusionEnable || run is InfiniteTowerRun)
            {
                return;
            }
            // Les épreuves du DLC4 gardent la liste des objets générés et s'en servent ensuite
            // sans vérifier qu'ils existent encore : on ne retire rien dans ces scènes.
            if (InstanceTracker.GetInstancesList<CacheInteractables>().Count > 0)
            {
                Plugin.Debug("Équilibrage ignoré : scène d'épreuve");
                return;
            }

            int players = run.participatingPlayerCount;
            StageCredits(out float baseCredits, out float bonusCredits);
            float keepFraction = KeepFraction(players, ModConfig.MultiplayerChestScaling.Value / 100f, baseCredits, bonusCredits);
            if (keepFraction >= 1f)
            {
                return;
            }

            var chests = spawnedChests.FindAll(chest => chest);
            int toRemove = chests.Count - Mathf.RoundToInt(chests.Count * keepFraction);
            if (toRemove <= 0)
            {
                return;
            }

            // Tirage sans remise (Fisher-Yates partiel), avec le hasard de l'étape.
            var rng = new Xoroshiro128Plus(run.stageRng.nextUlong);
            for (int i = 0; i < toRemove; i++)
            {
                int j = rng.RangeInt(i, chests.Count);
                (chests[i], chests[j]) = (chests[j], chests[i]);
                NetworkServer.Destroy(chests[i]);
            }
            Plugin.Debug($"Équilibrage : {toRemove} coffre(s) retiré(s) sur {chests.Count} ({players} joueurs, réglage {ModConfig.MultiplayerChestScaling.Value} %)");
        }

        /// <summary>Crédits de base et bonus de l'étape, lus comme SceneDirector.Start les lit.</summary>
        private static void StageCredits(out float baseCredits, out float bonusCredits)
        {
            baseCredits = 0f;
            bonusCredits = 0f;
            ClassicStageInfo stageInfo = SceneInfo.instance ? SceneInfo.instance.GetComponent<ClassicStageInfo>() : null;
            if (!stageInfo)
            {
                return;
            }
            baseCredits = stageInfo.sceneDirectorInteractibleCredits;
            if (stageInfo.bonusInteractibleCreditObjects == null)
            {
                return;
            }
            foreach (ClassicStageInfo.BonusInteractibleCreditObject bonus in stageInfo.bonusInteractibleCreditObjects)
            {
                if (bonus.objectThatGrantsPointsIfEnabled && bonus.objectThatGrantsPointsIfEnabled.activeSelf)
                {
                    bonusCredits += bonus.points;
                }
            }
        }
    }
}

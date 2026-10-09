using RoR2.Artifacts;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ShareItemsWithMoreFun
{
    /// <summary>Prefab de l'orbe « Potentiel du Néant » (OptionPickup).</summary>
    internal static class OptionPickupPrefab
    {
        // GUID publié par RoR2BepInExPack (GameAssetPathsBetter.RoR2_DLC1_OptionPickup), puis les chemins connus.
        private static readonly string[] Keys =
        {
            "f8e3413a378bd7c44aa09bed0020eaf5",
            "RoR2/DLC1/Interactables/OptionPickup/OptionPickup.prefab",
            "RoR2/DLC1/OptionPickup/OptionPickup.prefab",
        };

        private static GameObject prefab;
        private static bool warned;

        public static void Load()
        {
            TryKey(0);
        }

        private static void TryKey(int index)
        {
            if (index >= Keys.Length)
            {
                Plugin.Log.LogWarning("Prefab OptionPickup introuvable : l'orbe de l'Artefact du Commandement sera utilisée à la place.");
                return;
            }
            string key = Keys[index];
            Addressables.LoadResourceLocationsAsync(key, typeof(GameObject)).Completed += locations =>
            {
                if (locations.Result == null || locations.Result.Count == 0)
                {
                    TryKey(index + 1);
                    return;
                }
                Addressables.LoadAssetAsync<GameObject>(key).Completed += handle =>
                {
                    if (handle.Result)
                    {
                        prefab = handle.Result;
                        Plugin.Debug($"Prefab OptionPickup chargé ({key})");
                    }
                    else
                    {
                        TryKey(index + 1);
                    }
                };
            };
        }

        public static GameObject Get()
        {
            if (prefab)
            {
                return prefab;
            }
            if (!warned)
            {
                warned = true;
                Plugin.Log.LogWarning("Prefab OptionPickup pas encore disponible, repli sur l'orbe de commandement.");
            }
            return CommandArtifactManager.commandCubePrefab;
        }
    }
}

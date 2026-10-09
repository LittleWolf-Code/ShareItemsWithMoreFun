using System;
using System.Collections.Generic;
using RoR2;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Transforme un drop en orbe à choix : l'item tiré par le coffre plus N-1 autres de la
    /// même qualité, pris dans les listes que la table du coffre peut réellement donner
    /// (et filtrés par ses tags, pour les coffres de catégorie).
    /// </summary>
    internal static class ChoiceBuilder
    {
        public static bool TryConvert(ref GenericPickupController.CreatePickupInfo info, Xoroshiro128Plus rng)
        {
            UniquePickup rolled = info.pickup;
            PickupDef rolledDef = rolled.isValid ? PickupCatalog.GetPickupDef(rolled.pickupIndex) : null;
            string quality = QualityKey(rolledDef);
            if (quality == null || !Run.instance)
            {
                return false;
            }

            if (rng == null)
            {
                rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            }

            PickupDropTable table = info.chest ? info.chest.dropTable : null;
            List<PickupIndex> candidates = CollectCandidates(rolled, quality, table, onlyTableLists: true);
            if (candidates.Count == 0)
            {
                candidates = CollectCandidates(rolled, quality, table, onlyTableLists: false);
            }

            int wanted = Math.Min(ModConfig.ChoiceCount.Value - 1, candidates.Count);
            if (wanted < 1)
            {
                return false;
            }

            // Tirage sans remise (Fisher-Yates partiel).
            var options = new List<UniquePickup>(wanted + 1) { rolled };
            for (int i = 0; i < wanted; i++)
            {
                int j = rng.RangeInt(i, candidates.Count);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                options.Add(rolled.WithPickupIndex(candidates[i]));
            }
            // L'item tiré ne doit pas toujours être en première position.
            for (int i = options.Count - 1; i > 0; i--)
            {
                int j = rng.RangeInt(0, i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }

            UnityEngine.GameObject prefab = OptionPickupPrefab.Get();
            if (!prefab)
            {
                return false;
            }

            info.pickerOptions = PickupPickerController.GenerateOptionsFromList(options);
            info.maxOptionCount = 1;
            info.prefabOverride = prefab;
            info.pickup = DisplayPickup(rolled, rolledDef);
            Plugin.Debug($"Orbe : {options.Count} choix de qualité {quality}");
            return true;
        }

        /// <summary>Qualité commune : même rareté pour un item, lunaire pour item et équipement lunaires.</summary>
        private static string QualityKey(PickupDef def)
        {
            if (def == null)
            {
                return null;
            }
            if (def.itemIndex != ItemIndex.None)
            {
                ItemDef item = ItemCatalog.GetItemDef(def.itemIndex);
                if (!item)
                {
                    return null;
                }
                return item.tier == ItemTier.Lunar ? "lunar" : "item:" + (int)item.tier;
            }
            if (def.equipmentIndex != EquipmentIndex.None)
            {
                EquipmentDef equipment = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
                if (!equipment)
                {
                    return null;
                }
                if (equipment.isLunar)
                {
                    return "lunar";
                }
                return equipment.isBoss ? "equipment:boss" : "equipment";
            }
            return null;
        }

        private static List<PickupIndex> CollectCandidates(UniquePickup rolled, string quality, PickupDropTable table, bool onlyTableLists)
        {
            var result = new List<PickupIndex>();
            var seen = new HashSet<PickupIndex> { rolled.pickupIndex };
            BasicPickupDropTable basic = table as BasicPickupDropTable;
            bool filter = basic && basic.IsFilterRequired();

            foreach ((List<PickupIndex> list, float weight) in DropLists(Run.instance, basic))
            {
                if (list == null || (onlyTableLists && basic && weight <= 0f))
                {
                    continue;
                }
                foreach (PickupIndex index in list)
                {
                    if (!seen.Add(index))
                    {
                        continue;
                    }
                    PickupDef def = PickupCatalog.GetPickupDef(index);
                    if (QualityKey(def) != quality)
                    {
                        continue;
                    }
                    if (filter && !basic.PassesFilter(index))
                    {
                        continue;
                    }
                    if (!AllowsStorageFlags(def, rolled))
                    {
                        continue;
                    }
                    result.Add(index);
                }
            }
            return result;
        }

        /// <summary>Mêmes listes et mêmes poids que BasicPickupDropTable.GenerateWeightedSelection.</summary>
        private static IEnumerable<(List<PickupIndex>, float)> DropLists(Run run, BasicPickupDropTable t)
        {
            bool b = t;
            yield return (run.availableTier1DropList, b ? t.tier1Weight : 1f);
            yield return (run.availableTier2DropList, b ? t.tier2Weight : 1f);
            yield return (run.availableTier3DropList, b ? t.tier3Weight : 1f);
            yield return (run.availableBossDropList, b ? t.bossWeight : 1f);
            yield return (run.availableLunarItemDropList, b ? t.lunarItemWeight : 1f);
            yield return (run.availableLunarEquipmentDropList, b ? t.lunarEquipmentWeight : 1f);
            yield return (run.availableLunarCombinedDropList, b ? t.lunarCombinedWeight : 1f);
            yield return (run.availableEquipmentDropList, b ? t.equipmentWeight : 1f);
            yield return (run.availableVoidTier1DropList, b ? t.voidTier1Weight : 1f);
            yield return (run.availableVoidTier2DropList, b ? t.voidTier2Weight : 1f);
            yield return (run.availableVoidTier3DropList, b ? t.voidTier3Weight : 1f);
            yield return (run.availableVoidBossDropList, b ? t.voidBossWeight : 1f);
            yield return (run.availableFoodTierDropList, b ? t.foodTierWeight : 1f);
            yield return (run.availablePowerShapeItemsDropList, b ? t.powerShapesWeight : 1f);
            yield return (run.availableCursedTierDropList, b ? t.cursedWeight : 1f);
            yield return (run.availableHexTierDropList, b ? t.hexWeight : 1f);
            yield return (run.availablePageTierDropList, b ? t.pageWeight : 1f);
        }

        /// <summary>Un tirage temporaire ou de location ne propose que des items compatibles.</summary>
        private static bool AllowsStorageFlags(PickupDef def, UniquePickup rolled)
        {
            if (def.itemIndex == ItemIndex.None)
            {
                return !rolled.isTempItem && !rolled.isRental;
            }
            ItemDef item = ItemCatalog.GetItemDef(def.itemIndex);
            if (rolled.isTempItem && !item.ContainsTag(ItemTag.CanBeTemporary))
            {
                return false;
            }
            if (rolled.isRental && !item.ContainsTag(ItemTag.CanBeRental))
            {
                return false;
            }
            return true;
        }

        /// <summary>L'orbe affiche la couleur de la rareté, comme le Potentiel du Néant.</summary>
        private static UniquePickup DisplayPickup(UniquePickup rolled, PickupDef def)
        {
            ItemTier tier;
            if (def.itemIndex != ItemIndex.None)
            {
                tier = ItemCatalog.GetItemDef(def.itemIndex).tier;
            }
            else if (def.equipmentIndex != EquipmentIndex.None && EquipmentCatalog.GetEquipmentDef(def.equipmentIndex).isLunar)
            {
                tier = ItemTier.Lunar;
            }
            else
            {
                return rolled;
            }
            PickupIndex tierPickup = PickupCatalog.FindPickupIndex(tier);
            return tierPickup.isValid ? rolled.WithPickupIndex(tierPickup) : rolled;
        }
    }
}

using System;
using System.Collections.Generic;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Multimagasins instanciés : chaque joueur achète un terminal par multimagasin, de son côté.
    /// En vanilla, l'achat d'un terminal vide et ferme les trois (MultiShopController.OnPurchase)
    /// et marque le terminal acheté (ShopTerminalBehavior.SetHasBeenPurchased) pour tout le monde.
    /// Pendant un achat géré, ces deux effets sont neutralisés ; l'achat lui-même (paiement, item
    /// lâché) reste vanilla et passe par ChestHooks (ManagedChest posé sur chaque terminal).
    /// </summary>
    internal static class MultiShopHooks
    {
        // Noms des prefabs des multimagasins (l'objet qui porte MultiShopController), pas de leurs terminaux.
        private static readonly HashSet<string> MultiShopNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TripleShop", "TripleShopLarge", "TripleShopEquipment",
        };

        public static void Install()
        {
            On.RoR2.MultiShopController.Start += MultiShopController_Start;
            On.RoR2.MultiShopController.OnPurchase += MultiShopController_OnPurchase;
            On.RoR2.MultiShopController.SetCloseOnTerminalPurchase += MultiShopController_SetCloseOnTerminalPurchase;
            On.RoR2.ShopTerminalBehavior.SetHasBeenPurchased += ShopTerminalBehavior_SetHasBeenPurchased;
            On.RoR2.ShopTerminalBehavior.UpdatePickupDisplayAndAnimations += ShopTerminalBehavior_UpdatePickupDisplayAndAnimations;
        }

        private static void MultiShopController_Start(On.RoR2.MultiShopController.orig_Start orig, MultiShopController self)
        {
            orig(self);
            if (!NetworkServer.active || !Run.instance || !ModConfig.Enabled.Value || !ModConfig.InstancedMultiShops.Value
                || self.isTripleDroneVendor)
            {
                return;
            }
            string name = ModConfig.NormalizeName(self.gameObject.name);
            if (!MultiShopNames.Contains(name))
            {
                Plugin.Debug($"Multimagasin non géré : {name}");
                return;
            }
            foreach (GameObject terminal in self.terminalGameObjectsList)
            {
                PurchaseInteraction purchase = terminal ? terminal.GetComponent<PurchaseInteraction>() : null;
                if (!purchase)
                {
                    continue;
                }
                ManagedChest managed = ManagedChest.GetOrAdd(terminal);
                managed.serverManaged = true;
                managed.instanced = true;
                managed.isShopTerminal = true;
                managed.shop = self;
                managed.purchase = purchase;
                // Un drone piloté ne peut pas ramasser d'équipement : pas d'achat au multimagasin d'équipement.
                managed.remoteOpAllowed = ModConfig.RemoteOpCanOpenChests.Value && !self.doEquipmentInstead;
                ChestManagedMessage.For(managed)?.Send(NetworkDestination.Clients);
            }
            Plugin.Debug($"Multimagasin géré : {self.name}");
        }

        private static ManagedChest ManagedPurchase(Component component)
        {
            ManagedChest managed = component ? component.GetComponent<ManagedChest>() : null;
            return NetworkServer.active && managed && managed.isShopTerminal && managed.purchaseInProgress ? managed : null;
        }

        private static void MultiShopController_OnPurchase(On.RoR2.MultiShopController.orig_OnPurchase orig, MultiShopController self,
            CostTypeDef.PayCostContext payCostContext, CostTypeDef.PayCostResults payCostResult)
        {
            if (payCostContext != null && ManagedPurchase(payCostContext.purchaseInteraction))
            {
                return; // Le multimagasin reste ouvert et garde ses items pour les autres joueurs.
            }
            orig(self, payCostContext, payCostResult);
        }

        /// <summary>
        /// Carte de dirigeant : en vanilla, elle garde les autres terminaux ouverts après l'achat.
        /// On retient sa décision pour cet achat, sans toucher au drapeau du jeu (qui resterait
        /// sinon actif pour les acheteurs suivants).
        /// </summary>
        private static void MultiShopController_SetCloseOnTerminalPurchase(On.RoR2.MultiShopController.orig_SetCloseOnTerminalPurchase orig,
            MultiShopController self, PurchaseInteraction terminalPurchaseInteraction, bool doCloseMultiShop)
        {
            ManagedChest managed = ManagedPurchase(terminalPurchaseInteraction);
            if (managed)
            {
                managed.keepShopOpen = !doCloseMultiShop;
                return;
            }
            orig(self, terminalPurchaseInteraction, doCloseMultiShop);
        }

        private static void ShopTerminalBehavior_SetHasBeenPurchased(On.RoR2.ShopTerminalBehavior.orig_SetHasBeenPurchased orig, ShopTerminalBehavior self,
            bool newHasBeenPurchased)
        {
            if (newHasBeenPurchased && ManagedPurchase(self))
            {
                return; // L'état « acheté » est synchronisé à tous : on le garde local à l'acheteur.
            }
            orig(self, newHasBeenPurchased);
        }

        /// <summary>
        /// Client : si l'item d'un terminal change (Tinker, Suppresseur du Néant...), le jeu le
        /// réaffiche ; chez celui qui a déjà acheté, le terminal doit rester fermé.
        /// </summary>
        private static void ShopTerminalBehavior_UpdatePickupDisplayAndAnimations(On.RoR2.ShopTerminalBehavior.orig_UpdatePickupDisplayAndAnimations orig,
            ShopTerminalBehavior self)
        {
            orig(self);
            ManagedChest managed = self.GetComponent<ManagedChest>();
            if (managed && managed.OpenedByAllLocalPlayers())
            {
                ClientVisuals.ShowOpened(self.gameObject);
            }
        }

        /// <summary>
        /// Client : ce joueur ne peut plus rien acheter dans ce multimagasin (tous ses terminaux
        /// gérés sont « ouverts » pour lui). Avec la Carte de dirigeant, seul le terminal acheté l'est.
        /// </summary>
        public static bool ClosedFor(MultiShopController shop, NetworkInstanceId masterId)
        {
            bool anyManaged = false;
            foreach (GameObject terminal in shop.terminalGameObjectsList)
            {
                ManagedChest managed = terminal ? terminal.GetComponent<ManagedChest>() : null;
                if (!managed || !managed.instanced)
                {
                    continue;
                }
                anyManaged = true;
                if (!managed.HasOpened(masterId))
                {
                    return false;
                }
            }
            return anyManaged;
        }

        /// <summary>Client : le multimagasin est fermé pour tous les joueurs locaux (écran partagé).</summary>
        public static bool ClosedForAllLocalPlayers(MultiShopController shop)
        {
            if (LocalUserManager.readOnlyLocalUsersList.Count == 0)
            {
                return false;
            }
            foreach (LocalUser localUser in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterMaster master = localUser.cachedMaster;
                if (!master || !ClosedFor(shop, master.netId))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

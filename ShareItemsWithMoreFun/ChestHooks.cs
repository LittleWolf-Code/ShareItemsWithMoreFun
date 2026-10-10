using System;
using System.Collections;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Logique des coffres côté hôte : repérage, achat par joueur, drop des orbes.
    /// Déroulé d'un achat sur un coffre instancié :
    /// 1. OnInteractionBegin vanilla fait payer et applique l'Étoile des soldes (dropCount) ;
    /// 2. ses écouteurs Open() / SetAvailable(false) sont neutralisés pendant cet achat ;
    /// 3. le joueur est noté « déjà ouvert », les clients sont prévenus (ChestOpenedMessage) ;
    /// 4. après le temps de l'animation, ItemDrop vanilla tourne dans un DropContext
    ///    qui transforme chaque drop en orbe à choix réservée au joueur.
    /// </summary>
    internal static class ChestHooks
    {
        // L'animation vanilla d'ouverture lâche l'item vers 0,8 s.
        private const float DropDelaySeconds = 0.85f;

        public static void Install()
        {
            On.RoR2.ChestBehavior.Start += ChestBehavior_Start;
            On.RoR2.OptionChestBehavior.Start += OptionChestBehavior_Start;

            On.RoR2.PurchaseInteraction.OnInteractionBegin += PurchaseInteraction_OnInteractionBegin;
            On.RoR2.PurchaseInteraction.SetAvailable += PurchaseInteraction_SetAvailable;
            On.RoR2.PurchaseInteraction.GetInteractability += PurchaseInteraction_GetInteractability;
            PurchaseInteraction.onPurchaseGlobalServer += OnPurchaseGlobalServer;

            On.RoR2.ChestBehavior.Open += ChestBehavior_Open;
            On.RoR2.ChestBehavior.OpenAndGrantViaOrb += ChestBehavior_OpenAndGrantViaOrb;
            On.RoR2.ChestBehavior.ItemDrop += ChestBehavior_ItemDrop;
            On.RoR2.OptionChestBehavior.Open += OptionChestBehavior_Open;
            On.RoR2.OptionChestBehavior.ItemDrop += OptionChestBehavior_ItemDrop;
        }

        // ---------------------------------------------------------------- Repérage

        private static void ChestBehavior_Start(On.RoR2.ChestBehavior.orig_Start orig, ChestBehavior self)
        {
            orig(self);
            if (!CanManage(self.gameObject) || self.isCommandChest)
            {
                return;
            }
            ManagedChest managed = Setup(self.gameObject, ModConfig.InstancedChests.Value);
            if (managed)
            {
                managed.chest = self;
                managed.baseDropCount = Math.Max(1, self.dropCount);
            }
        }

        private static void OptionChestBehavior_Start(On.RoR2.OptionChestBehavior.orig_Start orig, OptionChestBehavior self)
        {
            orig(self);
            // Un Potentiel du Néant propose déjà un choix : seule l'instanciation s'applique.
            if (!ModConfig.InstancedChests.Value || !CanManage(self.gameObject))
            {
                return;
            }
            ManagedChest managed = Setup(self.gameObject, instanced: true);
            if (managed)
            {
                managed.optionChest = self;
            }
        }

        private static bool CanManage(GameObject chestObject)
        {
            return NetworkServer.active
                && ModConfig.Enabled.Value
                && !DelusionChestController.isDelusionEnable
                && Run.instance
                && ModConfig.IsManagedChestName(chestObject.name);
        }

        internal static ManagedChest Setup(GameObject chestObject, bool instanced)
        {
            PurchaseInteraction purchase = chestObject.GetComponent<PurchaseInteraction>();
            if (!purchase)
            {
                return null;
            }
            ManagedChest managed = ManagedChest.GetOrAdd(chestObject);
            managed.serverManaged = true;
            managed.instanced = instanced;
            managed.remoteOpAllowed = instanced && ModConfig.RemoteOpCanOpenChests.Value;
            managed.purchase = purchase;
            managed.rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            Plugin.Debug($"Coffre géré : {chestObject.name} (instancié : {instanced})");
            if (instanced)
            {
                // Les clients doivent savoir quels coffres l'hôte instancie (invite du drone piloté).
                ChestManagedMessage.For(managed)?.Send(NetworkDestination.Clients);
            }
            return managed;
        }

        private static bool TryGetServerInstanced(Component component, out ManagedChest managed)
        {
            managed = NetworkServer.active ? component.GetComponent<ManagedChest>() : null;
            return managed && managed.serverManaged && managed.instanced && !managed.globallyOpened;
        }

        // ---------------------------------------------------------------- Achat

        private static void PurchaseInteraction_OnInteractionBegin(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
            if (!TryGetServerInstanced(self, out ManagedChest managed))
            {
                orig(self, activator);
                return;
            }

            CharacterMaster player = PlayerResolver.FromInteractor(activator);
            if (!player || managed.HasOpened(player.netId))
            {
                return;
            }

            if (managed.shrine)
            {
                ShrineHooks.Purchase(managed, player, self, activator, orig);
                return;
            }

            if (managed.chest)
            {
                // L'Étoile des soldes d'un joueur précédent ne doit pas profiter à celui-ci.
                managed.chest.dropCount = managed.baseDropCount;
            }

            managed.purchaseInProgress = true;
            managed.purchaseSucceeded = false;
            managed.keepShopOpen = false;
            try
            {
                if (managed.isShopTerminal)
                {
                    // Le terminal lâche son item pendant l'achat (écouteur du prefab) : il revient à l'acheteur.
                    using (DropContext.Begin(player, convertToChoices: false, rng: null))
                    {
                        orig(self, activator);
                    }
                }
                else
                {
                    orig(self, activator);
                }
            }
            finally
            {
                managed.purchaseInProgress = false;
            }

            if (!managed.purchaseSucceeded)
            {
                return;
            }

            if (managed.isShopTerminal)
            {
                OnShopTerminalPurchased(managed, player);
                return;
            }

            int dropCount = managed.chest ? Math.Max(1, managed.chest.dropCount) : 1;
            if (managed.chest)
            {
                managed.chest.dropCount = managed.baseDropCount;
            }

            managed.MarkOpened(player.netId);
            ChestMarkers.MarkServer(managed);
            Plugin.Debug($"{player.name} a ouvert {self.name} ({dropCount} drop(s))");

            new ChestOpenedMessage(self.netId, player.netId, ChestVisual.PlayOpening).Send(NetworkDestination.Clients);
            managed.StartCoroutine(DropAfterDelay(managed, player, dropCount));
        }

        /// <summary>Un achat ferme tout le multimagasin, mais seulement pour l'acheteur.</summary>
        private static void OnShopTerminalPurchased(ManagedChest boughtTerminal, CharacterMaster player)
        {
            MultiShopController shop = boughtTerminal.shop;
            Plugin.Debug($"{player.name} a acheté dans {(shop ? shop.name : boughtTerminal.name)}");
            ChestMarkers.MarkServer(boughtTerminal);
            // Carte de dirigeant : seul le terminal acheté se ferme, les autres restent achetables.
            if (!shop || boughtTerminal.keepShopOpen)
            {
                boughtTerminal.MarkOpened(player.netId);
                new ChestOpenedMessage(boughtTerminal.purchase.netId, player.netId, ChestVisual.PlayOpening).Send(NetworkDestination.Clients);
                return;
            }
            foreach (GameObject terminal in shop.terminalGameObjectsList)
            {
                ManagedChest managed = terminal ? terminal.GetComponent<ManagedChest>() : null;
                if (!managed || !managed.purchase || managed.HasOpened(player.netId))
                {
                    continue; // Déjà fermé pour ce joueur (Carte de dirigeant) : pas de second volet.
                }
                managed.MarkOpened(player.netId);
                new ChestOpenedMessage(managed.purchase.netId, player.netId, ChestVisual.PlayOpening).Send(NetworkDestination.Clients);
            }
        }

        private static void OnPurchaseGlobalServer(CostTypeDef.PayCostContext context, CostTypeDef.PayCostResults results)
        {
            PurchaseInteraction purchase = context?.purchaseInteraction;
            if (purchase && purchase.TryGetComponent(out ManagedChest managed) && managed.purchaseInProgress)
            {
                managed.purchaseSucceeded = true;
            }
        }

        private static IEnumerator DropAfterDelay(ManagedChest managed, CharacterMaster owner, int dropCount)
        {
            yield return new WaitForSeconds(DropDelaySeconds);
            if (!managed || !NetworkServer.active)
            {
                yield break;
            }

            DropContext context = null;
            try
            {
                if (managed.chest)
                {
                    ChestBehavior chest = managed.chest;
                    if (!chest.currentPickup.isValid)
                    {
                        chest.Roll();
                    }
                    chest.dropCount = dropCount;
                    managed.allowItemDrop = true;
                    using (context = DropContext.Begin(owner, convertToChoices: true, managed.rng))
                    {
                        chest.ItemDrop();
                    }
                }
                else if (managed.optionChest)
                {
                    // Chaque joueur reçoit son propre tirage d'options.
                    managed.optionChest.Roll();
                    managed.allowItemDrop = true;
                    using (context = DropContext.Begin(owner, convertToChoices: false, managed.rng))
                    {
                        managed.optionChest.ItemDrop();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Échec du drop de {managed.name} : {e}");
                int created = context != null ? context.dropletsCreated : 0;
                DropPlainItemsAfterFailure(managed, owner, managed.chest ? dropCount - created : 1 - created);
            }
            finally
            {
                managed.allowItemDrop = false;
                if (managed.chest)
                {
                    // BaseItemDrop vide le tirage et marque le coffre « ouvert » : on le remet en état pour les autres.
                    managed.chest.dropCount = managed.baseDropCount;
                    managed.chest.NetworkisChestOpened = false;
                    if (!managed.chest.currentPickup.isValid)
                    {
                        managed.chest.Roll();
                    }
                }
            }
        }

        /// <summary>
        /// Le joueur a payé : en cas d'erreur, il reçoit ce qui manque sous forme de drops vanilla
        /// (items simples, ou le Potentiel du Néant tel quel), toujours réservés à lui.
        /// </summary>
        private static void DropPlainItemsAfterFailure(ManagedChest managed, CharacterMaster owner, int missing)
        {
            if (missing <= 0)
            {
                return;
            }
            try
            {
                if (managed.optionChest)
                {
                    managed.allowItemDrop = true;
                    using (DropContext.Begin(owner, convertToChoices: false, rng: null))
                    {
                        managed.optionChest.ItemDrop();
                    }
                    return;
                }

                ChestBehavior chest = managed.chest;
                if (!chest || !chest.dropTransform)
                {
                    return;
                }
                Vector3 position = chest.dropTransform.position + Vector3.up * 1.5f;
                Vector3 velocity = Vector3.up * chest.dropUpVelocityStrength + chest.dropTransform.forward * chest.dropForwardVelocityStrength;
                Quaternion step = Quaternion.AngleAxis(360f / missing, Vector3.up);
                using (DropContext.Begin(owner, convertToChoices: false, rng: null))
                {
                    for (int i = 0; i < missing; i++)
                    {
                        if (!chest.currentPickup.isValid)
                        {
                            chest.Roll();
                        }
                        if (!chest.currentPickup.isValid)
                        {
                            return;
                        }
                        PickupDropletController.CreatePickupDroplet(chest.currentPickup, position, velocity, false);
                        velocity = step * velocity;
                        chest.Roll();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Le drop de secours a aussi échoué : {e}");
            }
        }

        // ---------------------------------------------------------------- Neutralisation de l'ouverture globale

        private static void PurchaseInteraction_SetAvailable(On.RoR2.PurchaseInteraction.orig_SetAvailable orig, PurchaseInteraction self, bool newAvailable)
        {
            if (!newAvailable && TryGetServerInstanced(self, out ManagedChest managed) && managed.purchaseInProgress)
            {
                return;
            }
            orig(self, newAvailable);
        }

        private static void ChestBehavior_Open(On.RoR2.ChestBehavior.orig_Open orig, ChestBehavior self)
        {
            if (TryGetServerInstanced(self, out ManagedChest managed))
            {
                if (managed.purchaseInProgress)
                {
                    return;
                }
                managed.globallyOpened = true;
                Plugin.Debug($"{self.name} ouvert hors achat : retour au comportement vanilla");
            }
            orig(self);
        }

        private static void ChestBehavior_OpenAndGrantViaOrb(On.RoR2.ChestBehavior.orig_OpenAndGrantViaOrb orig, ChestBehavior self, Interactor interactor)
        {
            if (TryGetServerInstanced(self, out ManagedChest managed))
            {
                if (managed.purchaseInProgress)
                {
                    return;
                }
                managed.globallyOpened = true;
            }
            orig(self, interactor);
        }

        private static void OptionChestBehavior_Open(On.RoR2.OptionChestBehavior.orig_Open orig, OptionChestBehavior self)
        {
            if (TryGetServerInstanced(self, out ManagedChest managed))
            {
                if (managed.purchaseInProgress)
                {
                    return;
                }
                managed.globallyOpened = true;
            }
            orig(self);
        }

        private static void ChestBehavior_ItemDrop(On.RoR2.ChestBehavior.orig_ItemDrop orig, ChestBehavior self)
        {
            ManagedChest managed = NetworkServer.active ? self.GetComponent<ManagedChest>() : null;
            if (!managed || !managed.serverManaged || managed.globallyOpened)
            {
                orig(self);
                return;
            }

            if (managed.instanced)
            {
                // Seul notre propre drop passe ; l'événement d'animation déclenché par
                // l'ouverture locale chez l'hôte est ignoré.
                if (managed.allowItemDrop)
                {
                    managed.allowItemDrop = false;
                    orig(self);
                }
                return;
            }

            // Mode non instancié : drop vanilla, mais converti en orbe pour l'acheteur.
            CharacterMaster owner = PlayerResolver.FromInteractor(managed.purchase ? managed.purchase.lastActivator : null);
            using (DropContext.Begin(owner, convertToChoices: true, managed.rng))
            {
                orig(self);
            }
        }

        private static void OptionChestBehavior_ItemDrop(On.RoR2.OptionChestBehavior.orig_ItemDrop orig, OptionChestBehavior self)
        {
            ManagedChest managed = NetworkServer.active ? self.GetComponent<ManagedChest>() : null;
            if (managed && managed.serverManaged && managed.instanced && !managed.globallyOpened)
            {
                if (managed.allowItemDrop)
                {
                    managed.allowItemDrop = false;
                    orig(self);
                }
                return;
            }
            orig(self);
        }

        // ---------------------------------------------------------------- Invite (hôte et clients)

        private static Interactability PurchaseInteraction_GetInteractability(On.RoR2.PurchaseInteraction.orig_GetInteractability orig, PurchaseInteraction self, Interactor activator)
        {
            Interactability result = orig(self, activator);
            ManagedChest managed = self.GetComponent<ManagedChest>();
            if (result == Interactability.Disabled && RemoteOperation.CanOpenChest(self, managed, activator))
            {
                result = RemoteOperation.EvaluateAsAllowed(self, activator, orig);
            }
            if (result == Interactability.Disabled)
            {
                return result;
            }
            if (managed && managed.IsInstancedFor(activator, out bool alreadyOpened) && alreadyOpened)
            {
                return Interactability.Disabled;
            }
            if (managed && managed.IsShrineCoolingDown(PlayerResolver.FromInteractor(activator)))
            {
                return Interactability.Disabled;
            }
            return result;
        }
    }
}

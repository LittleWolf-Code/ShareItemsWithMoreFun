using RoR2;
using UnityEngine;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Affichage par joueur local : un coffre déjà ouvert ne montre plus son prix, sa surbrillance
    /// ni son icône de scanner ; les orbes cachées des autres joueurs n'apparaissent pas au scanner.
    /// </summary>
    internal static class ClientHooks
    {
        public static void Install()
        {
            On.RoR2.PurchaseInteraction.ShouldDisplayHologram += PurchaseInteraction_ShouldDisplayHologram;
            On.RoR2.PurchaseInteraction.ShouldShowOnScanner += PurchaseInteraction_ShouldShowOnScanner;
            On.RoR2.PurchaseInteraction.ShouldProximityHighlight += PurchaseInteraction_ShouldProximityHighlight;
            On.RoR2.PickupPickerController.ShouldShowOnScanner += PickupPickerController_ShouldShowOnScanner;
            On.RoR2.GenericPickupController.ShouldShowOnScanner += GenericPickupController_ShouldShowOnScanner;
            On.RoR2.MultiShopController.ShouldDisplayHologram += MultiShopController_ShouldDisplayHologram;
        }

        private static bool MultiShopController_ShouldDisplayHologram(On.RoR2.MultiShopController.orig_ShouldDisplayHologram orig, MultiShopController self, GameObject viewer)
        {
            // Le prix du multimagasin disparaît pour qui ne peut plus rien y acheter.
            if (!orig(self, viewer))
            {
                return false;
            }
            CharacterMaster viewerPlayer = viewer ? PlayerResolver.FromBody(viewer.GetComponent<CharacterBody>()) : null;
            if (viewerPlayer && PlayerResolver.IsLocalPlayer(viewerPlayer.netId))
            {
                return !MultiShopHooks.ClosedFor(self, viewerPlayer.netId);
            }
            return !MultiShopHooks.ClosedForAllLocalPlayers(self);
        }

        private static bool PurchaseInteraction_ShouldDisplayHologram(On.RoR2.PurchaseInteraction.orig_ShouldDisplayHologram orig, PurchaseInteraction self, GameObject viewer)
        {
            return orig(self, viewer) && !OpenedForLocalView(self, viewer) && !ShrineCoolingDownForLocalView(self, viewer);
        }

        /// <summary>Autel de la chance : comme en vanilla, pas de prix affiché pendant les 2 s qui suivent un essai.</summary>
        private static bool ShrineCoolingDownForLocalView(PurchaseInteraction purchase, GameObject viewer)
        {
            ManagedChest managed = purchase.GetComponent<ManagedChest>();
            return managed && managed.IsShrineCoolingDown(ShrineHooks.LocalViewer(viewer ? viewer.transform : null));
        }

        private static bool PurchaseInteraction_ShouldShowOnScanner(On.RoR2.PurchaseInteraction.orig_ShouldShowOnScanner orig, PurchaseInteraction self)
        {
            return orig(self) && !OpenedForLocalView(self, null);
        }

        private static bool PurchaseInteraction_ShouldProximityHighlight(On.RoR2.PurchaseInteraction.orig_ShouldProximityHighlight orig, PurchaseInteraction self)
        {
            return orig(self) && !OpenedForLocalView(self, null);
        }

        private static bool PickupPickerController_ShouldShowOnScanner(On.RoR2.PickupPickerController.orig_ShouldShowOnScanner orig, PickupPickerController self)
        {
            return orig(self) && !HiddenLocally(self);
        }

        private static bool GenericPickupController_ShouldShowOnScanner(On.RoR2.GenericPickupController.orig_ShouldShowOnScanner orig, GenericPickupController self)
        {
            return orig(self) && !HiddenLocally(self);
        }

        private static bool HiddenLocally(Component component)
        {
            OwnedObject owned = component.GetComponent<OwnedObject>();
            return owned && owned.ShouldHideLocally();
        }

        /// <summary>
        /// Le projecteur d'hologramme choisit le joueur le plus proche, quel que soit le client :
        /// on ne tient compte de ce « viewer » que s'il est un joueur local.
        /// </summary>
        private static bool OpenedForLocalView(PurchaseInteraction purchase, GameObject viewer)
        {
            ManagedChest managed = purchase.GetComponent<ManagedChest>();
            if (!managed)
            {
                return false;
            }
            CharacterMaster viewerPlayer = viewer ? PlayerResolver.FromBody(viewer.GetComponent<CharacterBody>()) : null;
            if (viewerPlayer && PlayerResolver.IsLocalPlayer(viewerPlayer.netId))
            {
                return managed.instanced && !managed.globallyOpened && managed.HasOpened(viewerPlayer.netId);
            }
            return managed.OpenedByAnyLocalPlayer();
        }
    }
}

using RoR2;
using UnityEngine;

namespace ShareItemsWithMoreFun
{
    /// <summary>Un coffre déjà ouvert par le joueur local ne montre plus son prix, sa surbrillance ni son icône de scanner.</summary>
    internal static class ClientHooks
    {
        public static void Install()
        {
            On.RoR2.PurchaseInteraction.ShouldDisplayHologram += PurchaseInteraction_ShouldDisplayHologram;
            On.RoR2.PurchaseInteraction.ShouldShowOnScanner += PurchaseInteraction_ShouldShowOnScanner;
            On.RoR2.PurchaseInteraction.ShouldProximityHighlight += PurchaseInteraction_ShouldProximityHighlight;
        }

        private static bool PurchaseInteraction_ShouldDisplayHologram(On.RoR2.PurchaseInteraction.orig_ShouldDisplayHologram orig, PurchaseInteraction self, GameObject viewer)
        {
            return orig(self, viewer) && !OpenedForLocalView(self, viewer);
        }

        private static bool PurchaseInteraction_ShouldShowOnScanner(On.RoR2.PurchaseInteraction.orig_ShouldShowOnScanner orig, PurchaseInteraction self)
        {
            return orig(self) && !OpenedForLocalView(self, null);
        }

        private static bool PurchaseInteraction_ShouldProximityHighlight(On.RoR2.PurchaseInteraction.orig_ShouldProximityHighlight orig, PurchaseInteraction self)
        {
            return orig(self) && !OpenedForLocalView(self, null);
        }

        private static bool OpenedForLocalView(PurchaseInteraction purchase, GameObject viewer)
        {
            ManagedChest managed = purchase.GetComponent<ManagedChest>();
            if (!managed)
            {
                return false;
            }
            CharacterMaster viewerPlayer = viewer ? PlayerResolver.FromBody(viewer.GetComponent<CharacterBody>()) : null;
            if (viewerPlayer)
            {
                return managed.instanced && !managed.globallyOpened && managed.HasOpened(viewerPlayer.netId);
            }
            return managed.OpenedByAnyLocalPlayer();
        }
    }
}

using RoR2;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Un joueur mort qui pilote un drone (opération à distance, DLC Alloyed Collective) ne peut
    /// pas ouvrir de coffre en vanilla. Avec des coffres instanciés, il perdrait sa part de toute
    /// l'étape : on l'autorise sur les coffres du mod et sur ses propres orbes. Son drone utilise
    /// l'inventaire du joueur, donc tout ce qu'il obtient lui reste (les équipements attendent son
    /// retour, voir PendingEquipment). La décision vient de l'hôte : coffre par coffre
    /// (ChestManagedMessage) et orbe par orbe (OwnerMessage).
    /// </summary>
    internal static class RemoteOperation
    {
        public static bool CanOpenChest(PurchaseInteraction purchase, ManagedChest managed, Interactor activator)
        {
            if (!activator || !activator.isRemoteOp || purchase.allowRemoteOpInteraction)
            {
                return false;
            }
            if (!managed || !managed.instanced || managed.globallyOpened || !managed.remoteOpAllowed)
            {
                return false;
            }
            // Hôte : seule sa propre logique fait foi. Client : il suit ce que l'hôte lui a envoyé.
            return !NetworkServer.active || managed.serverManaged;
        }

        public static Interactability EvaluateAsAllowed(PurchaseInteraction purchase, Interactor activator,
            On.RoR2.PurchaseInteraction.orig_GetInteractability orig)
        {
            // Toutes les autres règles vanilla s'appliquent (verrou, prix, disponibilité...).
            purchase.allowRemoteOpInteraction = true;
            try
            {
                return orig(purchase, activator);
            }
            finally
            {
                purchase.allowRemoteOpInteraction = false;
            }
        }

        public static bool CanOpenOrb(PickupPickerController picker, Interactor activator)
        {
            if (!activator || !activator.isRemoteOp || picker.allowRemoteOpInteraction)
            {
                return false;
            }
            OwnedObject owned = picker.GetComponent<OwnedObject>();
            CharacterMaster player = PlayerResolver.FromInteractor(activator);
            return owned && owned.HasOwner && owned.remoteOpAllowed && player && player.netId == owned.ownerId;
        }

        public static Interactability EvaluateAsAllowed(PickupPickerController picker, Interactor activator,
            On.RoR2.PickupPickerController.orig_GetInteractability orig)
        {
            picker.allowRemoteOpInteraction = true;
            try
            {
                return orig(picker, activator);
            }
            finally
            {
                picker.allowRemoteOpInteraction = false;
            }
        }
    }
}

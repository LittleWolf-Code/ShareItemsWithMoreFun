using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// État du mod sur un coffre. Côté hôte : posé au démarrage du coffre, il porte la liste
    /// des joueurs qui l'ont ouvert et l'achat en cours. Côté client : créé à la réception
    /// d'un ChestOpenedMessage, il ne sert qu'à l'affichage et aux invites.
    /// </summary>
    internal class ManagedChest : MonoBehaviour
    {
        public static readonly HashSet<ManagedChest> Instances = new HashSet<ManagedChest>();

        /// <summary>Le coffre est ouvrable une fois par joueur.</summary>
        public bool instanced;

        /// <summary>Posé par l'hôte (logique serveur active sur ce coffre).</summary>
        public bool serverManaged;

        /// <summary>Quelque chose d'autre que le mod a ouvert le coffre pour tout le monde : retour au vanilla.</summary>
        public bool globallyOpened;

        public ChestBehavior chest;
        public OptionChestBehavior optionChest;
        public PurchaseInteraction purchase;
        public int baseDropCount = 1;
        public Xoroshiro128Plus rng;

        // Transaction d'achat en cours (hôte uniquement, toujours sur le thread principal).
        public bool purchaseInProgress;
        public bool purchaseSucceeded;
        public bool allowItemDrop;

        private readonly HashSet<NetworkInstanceId> openedBy = new HashSet<NetworkInstanceId>();

        public IEnumerable<NetworkInstanceId> OpenedBy => openedBy;

        public bool IsInstancedFor(Interactor activator, out bool alreadyOpened)
        {
            alreadyOpened = false;
            if (!instanced || globallyOpened)
            {
                return false;
            }
            CharacterMaster player = PlayerResolver.FromInteractor(activator);
            alreadyOpened = !player || openedBy.Contains(player.netId);
            return true;
        }

        public bool HasOpened(NetworkInstanceId masterId)
        {
            return openedBy.Contains(masterId);
        }

        public void MarkOpened(NetworkInstanceId masterId)
        {
            openedBy.Add(masterId);
        }

        public bool OpenedByAnyLocalPlayer()
        {
            if (!instanced || globallyOpened)
            {
                return false;
            }
            foreach (NetworkInstanceId id in openedBy)
            {
                if (PlayerResolver.IsLocalPlayer(id))
                {
                    return true;
                }
            }
            return false;
        }

        public static ManagedChest GetOrAdd(GameObject chestObject)
        {
            ManagedChest managed = chestObject.GetComponent<ManagedChest>();
            return managed ? managed : chestObject.AddComponent<ManagedChest>();
        }

        private void OnEnable()
        {
            Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }
    }
}

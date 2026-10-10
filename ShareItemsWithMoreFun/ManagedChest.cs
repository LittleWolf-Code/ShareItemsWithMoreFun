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

        /// <summary>Un joueur mort aux commandes d'un drone peut l'ouvrir (réglage de l'hôte, envoyé aux clients).</summary>
        public bool remoteOpAllowed;

        public ChestBehavior chest;
        public OptionChestBehavior optionChest;

        /// <summary>Terminal d'un multimagasin : une seule ouverture par joueur pour tout le multimagasin.</summary>
        public bool isShopTerminal;
        public MultiShopController shop;

        /// <summary>Hôte, pendant un achat : la Carte de dirigeant garde les autres terminaux ouverts pour l'acheteur.</summary>
        public bool keepShopOpen;
        public PurchaseInteraction purchase;
        public int baseDropCount = 1;
        public Xoroshiro128Plus rng;

        // Transaction d'achat en cours (hôte uniquement, toujours sur le thread principal).
        public bool purchaseInProgress;
        public bool purchaseSucceeded;
        public bool allowItemDrop;

        /// <summary>Client : le coffre est déjà affiché ouvert pour le joueur local.</summary>
        public bool localOpenedShown;

        /// <summary>Un joueur l'a ouvert ou pingé : marqué pour ceux qui ne l'ont pas ouvert (section 3 sexies).</summary>
        public bool marked;

        /// <summary>
        /// Autel de la chance (section 3 quinquies) : prix, réussites et délai propres à chaque joueur.
        /// « Ouvert » pour un joueur = il a eu toutes ses réussites.
        /// </summary>
        public ShrineChanceBehavior shrine;

        /// <summary>Hôte, pendant un essai sur l'autel : le joueur qui tente et si l'autel a donné quelque chose.</summary>
        public CharacterMaster shrinePlayer;
        public bool shrineSucceeded;

        private readonly HashSet<NetworkInstanceId> openedBy = new HashSet<NetworkInstanceId>();
        private readonly Dictionary<NetworkInstanceId, ShrinePlayerState> shrineStates = new Dictionary<NetworkInstanceId, ShrinePlayerState>();

        public IEnumerable<NetworkInstanceId> OpenedBy => openedBy;

        public IEnumerable<KeyValuePair<NetworkInstanceId, ShrinePlayerState>> ShrineStates => shrineStates;

        /// <summary>Autel de la chance tenu par joueur (sinon : coffre, ou autel vanilla).</summary>
        public bool IsInstancedShrine => shrine && instanced && !globallyOpened;

        public ShrinePlayerState GetShrineState(NetworkInstanceId masterId, bool create)
        {
            if (!shrineStates.TryGetValue(masterId, out ShrinePlayerState state) && create)
            {
                state = new ShrinePlayerState();
                shrineStates[masterId] = state;
            }
            return state;
        }

        /// <summary>
        /// Un joueur local (écran partagé compris) peut tenter sa chance maintenant : ni fini, ni en attente.
        /// Sans joueur local, l'autel est montré tel quel.
        /// </summary>
        public bool ShrineUsableByAnyLocalPlayer()
        {
            bool anyLocal = false;
            foreach (LocalUser localUser in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterMaster master = localUser.cachedMaster;
                if (!master)
                {
                    continue;
                }
                anyLocal = true;
                if (!openedBy.Contains(master.netId) && !IsShrineCoolingDown(master))
                {
                    return true;
                }
            }
            return !anyLocal;
        }

        /// <summary>Le joueur vient de tenter sa chance : 2 s d'attente, comme l'autel vanilla.</summary>
        public bool IsShrineCoolingDown(CharacterMaster player)
        {
            if (!IsInstancedShrine || !player)
            {
                return false;
            }
            ShrinePlayerState state = GetShrineState(player.netId, create: false);
            return state != null && Time.time < state.readyTime;
        }

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

        /// <summary>Tous les joueurs locaux (écran partagé) l'ont ouvert : on peut l'afficher fermé.</summary>
        public bool OpenedByAllLocalPlayers()
        {
            if (!instanced || globallyOpened || LocalUserManager.readOnlyLocalUsersList.Count == 0)
            {
                return false;
            }
            foreach (LocalUser localUser in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterMaster master = localUser.cachedMaster;
                if (!master || !openedBy.Contains(master.netId))
                {
                    return false;
                }
            }
            return true;
        }

        public static ManagedChest GetOrAdd(GameObject chestObject)
        {
            ManagedChest managed = chestObject.GetComponent<ManagedChest>();
            if (!managed)
            {
                managed = chestObject.AddComponent<ManagedChest>();
                managed.shrine = chestObject.GetComponent<ShrineChanceBehavior>();
            }
            return managed;
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

    /// <summary>Un joueur sur un autel de la chance : nombre d'essais (le prix en découle), réussites, fin du délai.</summary>
    internal sealed class ShrinePlayerState
    {
        public int attempts;
        public int successes;
        public float readyTime;

        /// <summary>Un essai à prix nul (balise de piratage du Capitaine) : comme en vanilla, l'autel reste gratuit, ici pour ce joueur.</summary>
        public bool free;
    }
}

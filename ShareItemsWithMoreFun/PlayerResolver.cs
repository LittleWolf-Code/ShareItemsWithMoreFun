using RoR2;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>Retrouve le joueur (CharacterMaster avec PlayerCharacterMasterController) derrière une interaction.</summary>
    internal static class PlayerResolver
    {
        public static CharacterMaster FromInteractor(Interactor interactor)
        {
            return interactor ? FromBody(interactor.GetComponent<CharacterBody>()) : null;
        }

        public static CharacterMaster FromBody(CharacterBody body)
        {
            return body ? FromMaster(body.master) : null;
        }

        /// <summary>Un drone piloté à distance (opération à distance) remonte à son propriétaire.</summary>
        public static CharacterMaster FromMaster(CharacterMaster master)
        {
            for (int depth = 0; master && depth < 4; depth++)
            {
                if (master.playerCharacterMasterController)
                {
                    return master;
                }
                CharacterMaster owner = master.minionOwnership ? master.minionOwnership.ownerMaster : null;
                if (owner == master)
                {
                    break;
                }
                master = owner;
            }
            return null;
        }

        public static bool IsLocalPlayer(NetworkInstanceId masterId)
        {
            foreach (LocalUser localUser in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterMaster master = localUser.cachedMaster;
                if (master && master.netId == masterId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

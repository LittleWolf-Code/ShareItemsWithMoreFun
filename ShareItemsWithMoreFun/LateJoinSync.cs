using System.Collections;
using System.Collections.Generic;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>Un joueur qui rejoint (ou revient) en cours d'étape reçoit l'état actuel des coffres et des orbes.</summary>
    internal static class LateJoinSync
    {
        private const float DelaySeconds = 2f;

        public static void Install()
        {
            NetworkUser.onPostNetworkUserStart += OnNetworkUserStart;
        }

        private static void OnNetworkUserStart(NetworkUser networkUser)
        {
            if (!NetworkServer.active || !networkUser || networkUser.isLocalPlayer || networkUser.connectionToClient == null)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(SendStateLater(networkUser));
        }

        private static IEnumerator SendStateLater(NetworkUser networkUser)
        {
            yield return new WaitForSeconds(DelaySeconds);
            NetworkConnection connection = networkUser ? networkUser.connectionToClient : null;
            if (connection == null || !NetworkServer.active)
            {
                yield break;
            }

            int sent = 0;
            foreach (ManagedChest managed in new List<ManagedChest>(ManagedChest.Instances))
            {
                if (!managed || !managed.serverManaged || !managed.instanced || !managed.purchase)
                {
                    continue;
                }
                foreach (NetworkInstanceId opener in managed.OpenedBy)
                {
                    new ChestOpenedMessage(managed.purchase.netId, opener, playOpening: false).Send(connection);
                    sent++;
                }
            }
            foreach (OwnedObject owned in new List<OwnedObject>(OwnedObject.Instances))
            {
                OwnerMessage message = owned && owned.HasOwner ? OwnerMessage.For(owned.gameObject) : null;
                if (message != null)
                {
                    message.Send(connection);
                    sent++;
                }
            }
            Plugin.Debug($"État envoyé à {networkUser.userName} ({sent} message(s))");
        }
    }
}

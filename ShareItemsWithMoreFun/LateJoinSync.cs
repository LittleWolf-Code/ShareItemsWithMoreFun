using System.Collections.Generic;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Un joueur qui a fini de charger l'étape (arrivée en cours de partie, reconnexion, chargement
    /// lent) reçoit l'état actuel des coffres et des orbes. L'événement vient du client lui-même,
    /// quand il confirme avoir chargé la scène : les objets existent alors chez lui.
    /// </summary>
    internal static class LateJoinSync
    {
        public static void Install()
        {
            NetworkUser.onNetworkUserLoadedSceneServer += OnNetworkUserLoadedScene;
        }

        private static void OnNetworkUserLoadedScene(NetworkUser networkUser)
        {
            if (!NetworkServer.active || !networkUser || networkUser.isLocalPlayer)
            {
                return;
            }
            NetworkConnection connection = networkUser.connectionToClient;
            if (connection == null || NetworkServer.localConnections.Contains(connection))
            {
                return;
            }

            int sent = 0;
            if (DifficultyFreeze.IsFrozen)
            {
                new DifficultyFreezeMessage(true, DifficultyFreeze.FrozenStopwatch, DifficultyFreeze.FrozenStageClearCount).Send(connection);
                sent++;
            }
            foreach (ManagedChest managed in new List<ManagedChest>(ManagedChest.Instances))
            {
                if (!managed || !managed.serverManaged || !managed.instanced || managed.globallyOpened || !managed.purchase)
                {
                    continue;
                }
                ChestManagedMessage marker = ChestManagedMessage.For(managed);
                if (marker != null)
                {
                    marker.Send(connection);
                    sent++;
                }
                foreach (NetworkInstanceId opener in managed.OpenedBy)
                {
                    new ChestOpenedMessage(managed.purchase.netId, opener, ChestVisual.ShowOpened).Send(connection);
                    sent++;
                }
                foreach (KeyValuePair<NetworkInstanceId, ShrinePlayerState> entry in managed.ShrineStates)
                {
                    new ShrineStateMessage(managed.purchase.netId, entry.Key, entry.Value, cooldown: false).Send(connection);
                    sent++;
                }
                if (managed.marked)
                {
                    new ChestMarkedMessage(managed.purchase.netId).Send(connection);
                    sent++;
                }
            }
            // Y compris les objets libérés, pour corriger un état périmé resté chez le client.
            foreach (OwnedObject owned in new List<OwnedObject>(OwnedObject.Instances))
            {
                OwnerMessage message = owned ? OwnerMessage.For(owned.gameObject) : null;
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

using R2API.Networking;
using R2API.Networking.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>Hôte → clients : tel joueur a ouvert tel coffre.</summary>
    internal class ChestOpenedMessage : INetMessage
    {
        private NetworkInstanceId chestId;
        private NetworkInstanceId playerId;
        private bool playOpening;

        public ChestOpenedMessage()
        {
        }

        public ChestOpenedMessage(NetworkInstanceId chestId, NetworkInstanceId playerId, bool playOpening)
        {
            this.chestId = chestId;
            this.playerId = playerId;
            this.playOpening = playOpening;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(chestId);
            writer.Write(playerId);
            writer.Write(playOpening);
        }

        public void Deserialize(NetworkReader reader)
        {
            chestId = reader.ReadNetworkId();
            playerId = reader.ReadNetworkId();
            playOpening = reader.ReadBoolean();
        }

        public void OnReceived()
        {
            NetworkInstanceId player = playerId;
            bool animate = playOpening;
            NetworkLookups.WhenFound(chestId, chestObject =>
            {
                ManagedChest managed = ManagedChest.GetOrAdd(chestObject);
                managed.instanced = true;
                managed.MarkOpened(player);
                if (animate && PlayerResolver.IsLocalPlayer(player))
                {
                    ClientVisuals.PlayLocalOpening(chestObject);
                }
            });
        }
    }

    /// <summary>Hôte → clients : propriétaire d'une gouttelette, d'une orbe ou d'un ramassable.</summary>
    internal class OwnerMessage : INetMessage
    {
        private NetworkInstanceId objectId;
        private NetworkInstanceId ownerId;
        private bool reserve;
        private bool hide;

        public OwnerMessage()
        {
        }

        private OwnerMessage(NetworkInstanceId objectId, OwnedObject owned)
        {
            this.objectId = objectId;
            ownerId = owned.ownerId;
            reserve = owned.reserve;
            hide = owned.hide;
        }

        public static OwnerMessage For(GameObject gameObject)
        {
            NetworkIdentity identity = gameObject ? gameObject.GetComponent<NetworkIdentity>() : null;
            OwnedObject owned = gameObject ? gameObject.GetComponent<OwnedObject>() : null;
            if (!identity || !owned || identity.netId.IsEmpty())
            {
                return null;
            }
            return new OwnerMessage(identity.netId, owned);
        }

        public static void Broadcast(GameObject gameObject)
        {
            if (NetworkServer.active)
            {
                For(gameObject)?.Send(NetworkDestination.Clients);
            }
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(objectId);
            writer.Write(ownerId);
            writer.Write(reserve);
            writer.Write(hide);
        }

        public void Deserialize(NetworkReader reader)
        {
            objectId = reader.ReadNetworkId();
            ownerId = reader.ReadNetworkId();
            reserve = reader.ReadBoolean();
            hide = reader.ReadBoolean();
        }

        public void OnReceived()
        {
            NetworkInstanceId owner = ownerId;
            bool reserveForOwner = reserve;
            bool hideFromOthers = hide;
            NetworkLookups.WhenFound(objectId, gameObject =>
            {
                OwnedObject.GetOrAdd(gameObject).Apply(owner, reserveForOwner, hideFromOthers);
            });
        }
    }
}

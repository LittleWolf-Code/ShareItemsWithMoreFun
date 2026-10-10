using R2API.Networking;
using R2API.Networking.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>Ce que le client doit montrer pour le joueur local qui a ouvert le coffre.</summary>
    internal enum ChestVisual : byte
    {
        None = 0,
        /// <summary>Ouverture en direct : animation et son.</summary>
        PlayOpening = 1,
        /// <summary>Resynchronisation (reconnexion) : le coffre est directement affiché ouvert, sans son.</summary>
        ShowOpened = 2,
    }

    /// <summary>Hôte → clients : ce coffre est instancié par l'hôte (et ouvrable ou non par un drone piloté).</summary>
    internal class ChestManagedMessage : INetMessage
    {
        private NetworkInstanceId chestId;
        private bool remoteOpAllowed;

        public ChestManagedMessage()
        {
        }

        public static ChestManagedMessage For(ManagedChest managed)
        {
            if (!managed || !managed.purchase || managed.purchase.netId.IsEmpty())
            {
                return null;
            }
            return new ChestManagedMessage { chestId = managed.purchase.netId, remoteOpAllowed = managed.remoteOpAllowed };
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(chestId);
            writer.Write(remoteOpAllowed);
        }

        public void Deserialize(NetworkReader reader)
        {
            chestId = reader.ReadNetworkId();
            remoteOpAllowed = reader.ReadBoolean();
        }

        public void OnReceived()
        {
            if (NetworkServer.active)
            {
                return;
            }
            bool allowed = remoteOpAllowed;
            NetworkLookups.WhenFound(chestId, chestObject =>
            {
                ManagedChest managed = ManagedChest.GetOrAdd(chestObject);
                managed.instanced = true;
                managed.remoteOpAllowed = allowed;
            });
        }
    }

    /// <summary>Hôte → clients : tel joueur a ouvert tel coffre.</summary>
    internal class ChestOpenedMessage : INetMessage
    {
        private NetworkInstanceId chestId;
        private NetworkInstanceId playerId;
        private ChestVisual visual;

        public ChestOpenedMessage()
        {
        }

        public ChestOpenedMessage(NetworkInstanceId chestId, NetworkInstanceId playerId, ChestVisual visual)
        {
            this.chestId = chestId;
            this.playerId = playerId;
            this.visual = visual;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(chestId);
            writer.Write(playerId);
            writer.Write((byte)visual);
        }

        public void Deserialize(NetworkReader reader)
        {
            chestId = reader.ReadNetworkId();
            playerId = reader.ReadNetworkId();
            visual = (ChestVisual)reader.ReadByte();
        }

        public void OnReceived()
        {
            NetworkInstanceId player = playerId;
            ChestVisual chestVisual = visual;
            NetworkLookups.WhenFound(chestId, chestObject =>
            {
                ManagedChest managed = ManagedChest.GetOrAdd(chestObject);
                managed.instanced = true;
                bool firstTime = !managed.HasOpened(player);
                managed.MarkOpened(player);
                if (!PlayerResolver.IsLocalPlayer(player))
                {
                    return;
                }
                if (chestVisual == ChestVisual.PlayOpening)
                {
                    ClientVisuals.PlayLocalOpening(chestObject);
                }
                else if (chestVisual == ChestVisual.ShowOpened && (firstTime || !managed.localOpenedShown))
                {
                    ClientVisuals.ShowOpened(chestObject);
                }
            });
        }
    }

    /// <summary>Hôte → clients : ce coffre est marqué pour tous ceux qui ne l'ont pas encore ouvert.</summary>
    internal class ChestMarkedMessage : INetMessage
    {
        private NetworkInstanceId chestId;

        public ChestMarkedMessage()
        {
        }

        public ChestMarkedMessage(NetworkInstanceId chestId)
        {
            this.chestId = chestId;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(chestId);
        }

        public void Deserialize(NetworkReader reader)
        {
            chestId = reader.ReadNetworkId();
        }

        public void OnReceived()
        {
            NetworkLookups.WhenFound(chestId, chestObject =>
            {
                ManagedChest managed = ManagedChest.GetOrAdd(chestObject);
                managed.instanced = true;
                managed.marked = true;
            });
        }
    }

    /// <summary>Hôte → clients : où en est tel joueur sur tel autel de la chance (son prix en découle).</summary>
    internal class ShrineStateMessage : INetMessage
    {
        private NetworkInstanceId shrineId;
        private NetworkInstanceId playerId;
        private int attempts;
        private int successes;
        private bool free;
        private bool cooldown;

        public ShrineStateMessage()
        {
        }

        /// <param name="cooldown">Essai en direct : l'autel attend 2 s avant le suivant pour ce joueur.</param>
        public ShrineStateMessage(NetworkInstanceId shrineId, NetworkInstanceId playerId, ShrinePlayerState state, bool cooldown)
        {
            this.shrineId = shrineId;
            this.playerId = playerId;
            attempts = state.attempts;
            successes = state.successes;
            free = state.free;
            this.cooldown = cooldown;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(shrineId);
            writer.Write(playerId);
            writer.WritePackedUInt32((uint)attempts);
            writer.WritePackedUInt32((uint)successes);
            writer.Write(free);
            writer.Write(cooldown);
        }

        public void Deserialize(NetworkReader reader)
        {
            shrineId = reader.ReadNetworkId();
            playerId = reader.ReadNetworkId();
            attempts = (int)reader.ReadPackedUInt32();
            successes = (int)reader.ReadPackedUInt32();
            free = reader.ReadBoolean();
            cooldown = reader.ReadBoolean();
        }

        public void OnReceived()
        {
            if (NetworkServer.active)
            {
                return; // L'hôte tient déjà cet état.
            }
            NetworkInstanceId player = playerId;
            int playerAttempts = attempts;
            int playerSuccesses = successes;
            bool playerFree = free;
            bool startCooldown = cooldown;
            NetworkLookups.WhenFound(shrineId, shrineObject =>
            {
                ManagedChest managed = ManagedChest.GetOrAdd(shrineObject);
                managed.instanced = true;
                ShrinePlayerState state = managed.GetShrineState(player, create: true);
                state.attempts = playerAttempts;
                state.successes = playerSuccesses;
                state.free = playerFree;
                if (startCooldown)
                {
                    state.readyTime = Time.time + ShrineHooks.RefreshSeconds;
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
        private bool remoteOpAllowed;

        public OwnerMessage()
        {
        }

        private OwnerMessage(NetworkInstanceId objectId, OwnedObject owned)
        {
            this.objectId = objectId;
            ownerId = owned.ownerId;
            reserve = owned.reserve;
            hide = owned.hide;
            remoteOpAllowed = owned.remoteOpAllowed;
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
            writer.Write(remoteOpAllowed);
        }

        public void Deserialize(NetworkReader reader)
        {
            objectId = reader.ReadNetworkId();
            ownerId = reader.ReadNetworkId();
            reserve = reader.ReadBoolean();
            hide = reader.ReadBoolean();
            remoteOpAllowed = reader.ReadBoolean();
        }

        public void OnReceived()
        {
            NetworkInstanceId owner = ownerId;
            bool reserveForOwner = reserve;
            bool hideFromOthers = hide;
            bool droneAllowed = remoteOpAllowed;
            NetworkLookups.WhenFound(objectId, gameObject =>
            {
                OwnedObject.GetOrAdd(gameObject).Apply(owner, reserveForOwner, hideFromOthers, droneAllowed);
            });
        }
    }
}

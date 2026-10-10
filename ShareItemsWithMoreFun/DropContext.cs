using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Contexte d'un drop côté hôte. Tant qu'il est actif, chaque gouttelette, orbe ou
    /// ramassable créé reçoit le propriétaire du contexte (OwnedObject), et, si demandé,
    /// chaque drop simple est transformé en orbe à choix. Les propriétaires sont envoyés
    /// aux clients à la fermeture du contexte, une fois les objets apparus sur le réseau.
    /// </summary>
    internal sealed class DropContext : IDisposable
    {
        public static DropContext Current { get; private set; }

        public readonly CharacterMaster owner;
        public readonly bool convertToChoices;
        public readonly Xoroshiro128Plus rng;

        /// <summary>Gouttelettes réellement créées dans ce contexte (pour le drop de secours).</summary>
        public int dropletsCreated;

        private readonly DropContext previous;
        private readonly List<GameObject> tagged = new List<GameObject>();

        private DropContext(CharacterMaster owner, bool convertToChoices, Xoroshiro128Plus rng, DropContext previous)
        {
            this.owner = owner;
            this.convertToChoices = convertToChoices;
            this.rng = rng;
            this.previous = previous;
        }

        public static DropContext Begin(CharacterMaster owner, bool convertToChoices, Xoroshiro128Plus rng)
        {
            Current = new DropContext(owner, convertToChoices, rng, Current);
            return Current;
        }

        public void Tag(GameObject gameObject)
        {
            if (!owner || !gameObject)
            {
                return;
            }
            OwnedObject owned = OwnedObject.GetOrAdd(gameObject);
            owned.SetOwnerServer(owner.netId, ModConfig.ReserveOrbs.Value, ModConfig.HideOtherPlayersOrbs.Value, ModConfig.RemoteOpCanOpenChests.Value);
            tagged.Add(gameObject);
        }

        public void Dispose()
        {
            Current = previous;
            foreach (GameObject gameObject in tagged)
            {
                if (!gameObject)
                {
                    continue;
                }
                try
                {
                    OwnerMessage.Broadcast(gameObject);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError(e);
                }
            }
            tagged.Clear();
        }
    }
}

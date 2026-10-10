using System;
using System.Collections.Generic;
using EntityStates.CaptainSupplyDrop;
using RoR2;
using RoR2.Quests.CompulsionQuests;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>Mécaniques vanilla qui supposent qu'un coffre ouvert l'est pour tout le monde.</summary>
    internal static class CompatHooks
    {
        // Joueur propriétaire de la balise de piratage en cours d'évaluation (hôte uniquement).
        private static bool beaconContextActive;
        private static CharacterMaster beaconOwner;
        private static Interactor beaconInteractor;

        public static void Install()
        {
            On.EntityStates.CaptainSupplyDrop.UnlockTargetState.OnEnter += UnlockTargetState_OnEnter;
            On.EntityStates.CaptainSupplyDrop.HackingMainState.FixedUpdate += HackingMainState_FixedUpdate;
            On.EntityStates.CaptainSupplyDrop.HackingInProgressState.FixedUpdate += HackingInProgressState_FixedUpdate;
            On.EntityStates.CaptainSupplyDrop.HackingMainState.PurchaseInteractionIsValidTarget += PurchaseInteractionIsValidTarget;
            On.RoR2.Quests.CompulsionQuests.CompulsionQuestCloakedChest.EnsureCloakedChestExists += EnsureCloakedChestExists;
        }

        private static bool IsServerInstanced(Component component, out ManagedChest managed)
        {
            managed = component ? component.GetComponent<ManagedChest>() : null;
            return NetworkServer.active && managed && managed.serverManaged && managed.instanced && !managed.globallyOpened;
        }

        // ---------------------------------------------------------------- Balise de piratage du Capitaine

        /// <summary>
        /// Vanilla met le coût du coffre à 0 puis l'achète pour le Capitaine. Sur un coffre instancié,
        /// on remet le coût juste après : seul le Capitaine en profite.
        /// </summary>
        private static void UnlockTargetState_OnEnter(On.EntityStates.CaptainSupplyDrop.UnlockTargetState.orig_OnEnter orig, UnlockTargetState self)
        {
            PurchaseInteraction target = self.target;
            int savedCost = target ? target.cost : 0;
            orig(self);
            if (savedCost > 0 && target && target.cost == 0 && IsServerInstanced(target, out _))
            {
                target.Networkcost = savedCost;
            }
        }

        private static void HackingMainState_FixedUpdate(On.EntityStates.CaptainSupplyDrop.HackingMainState.orig_FixedUpdate orig, HackingMainState self)
        {
            using (BeaconContext(self))
            {
                orig(self);
            }
        }

        private static void HackingInProgressState_FixedUpdate(On.EntityStates.CaptainSupplyDrop.HackingInProgressState.orig_FixedUpdate orig, HackingInProgressState self)
        {
            using (BeaconContext(self))
            {
                orig(self);
            }
        }

        /// <summary>
        /// La balise ignore un coffre instancié que son propriétaire ne peut pas ouvrir maintenant
        /// (déjà ouvert, verrouillé pendant le téléporteur, etc.) : sinon, comme le coût est remis
        /// après chaque piratage, elle reviendrait sans fin sur le même coffre.
        /// </summary>
        private static bool PurchaseInteractionIsValidTarget(On.EntityStates.CaptainSupplyDrop.HackingMainState.orig_PurchaseInteractionIsValidTarget orig, PurchaseInteraction purchaseInteraction)
        {
            bool valid = orig(purchaseInteraction);
            if (valid && beaconContextActive && IsServerInstanced(purchaseInteraction, out ManagedChest managed))
            {
                return beaconOwner && beaconInteractor
                    && purchaseInteraction.GetInteractability(beaconInteractor) != Interactability.Disabled
                    && !IsFreeShrineFor(managed, beaconOwner);
            }
            return valid;
        }

        /// <summary>Vanilla ne pirate pas un objet déjà gratuit : un autel déjà piraté pour le Capitaine est ignoré.</summary>
        private static bool IsFreeShrineFor(ManagedChest managed, CharacterMaster player)
        {
            ShrinePlayerState state = managed.IsInstancedShrine ? managed.GetShrineState(player.netId, create: false) : null;
            return state != null && state.free;
        }

        private static IDisposable BeaconContext(EntityStates.EntityState state)
        {
            GameObject beacon = state.outer ? state.outer.gameObject : null;
            GenericOwnership ownership = beacon ? beacon.GetComponent<GenericOwnership>() : null;
            GameObject ownerObject = ownership ? ownership.ownerObject : null;
            CharacterMaster owner = PlayerResolver.FromBody(ownerObject ? ownerObject.GetComponent<CharacterBody>() : null);
            Interactor interactor = ownerObject ? ownerObject.GetComponent<Interactor>() : null;
            return new BeaconScope(owner, interactor);
        }

        private sealed class BeaconScope : IDisposable
        {
            private readonly bool previousActive;
            private readonly CharacterMaster previousOwner;
            private readonly Interactor previousInteractor;

            public BeaconScope(CharacterMaster owner, Interactor interactor)
            {
                previousActive = beaconContextActive;
                previousOwner = beaconOwner;
                previousInteractor = beaconInteractor;
                beaconContextActive = true;
                beaconOwner = owner;
                beaconInteractor = interactor;
            }

            public void Dispose()
            {
                beaconContextActive = previousActive;
                beaconOwner = previousOwner;
                beaconInteractor = previousInteractor;
            }
        }

        // ---------------------------------------------------------------- Quête de Compulsion « coffre camouflé »

        /// <summary>
        /// La quête réutilise un coffre camouflé resté fermé côté serveur. Un coffre instancié déjà
        /// ouvert par tous les joueurs ne compte plus : la quête en fait apparaître un nouveau.
        /// </summary>
        private static void EnsureCloakedChestExists(On.RoR2.Quests.CompulsionQuests.CompulsionQuestCloakedChest.orig_EnsureCloakedChestExists orig, CompulsionQuestCloakedChest self)
        {
            CompulsionQuestTracker tracker = self.runner ? self.runner.GetComponent<CompulsionQuestTracker>() : null;
            TeamIndex team = tracker && tracker.participatingTeamIndex != TeamIndex.None ? tracker.participatingTeamIndex : TeamIndex.Player;
            CharacterMaster questOwner = tracker ? PlayerResolver.FromBody(tracker.questOwnerCharacterBody) : null;

            var exhausted = new List<ChestBehavior>();
            foreach (ChestBehavior chest in InstanceTracker.GetInstancesList<ChestBehavior>())
            {
                if (chest && chest.name.IndexOf("Stealthed", StringComparison.OrdinalIgnoreCase) >= 0
                    && IsServerInstanced(chest, out ManagedChest managed) && !AnyPlayerCanStillOpen(managed, team, questOwner))
                {
                    exhausted.Add(chest);
                }
            }

            var removed = new List<ChestBehavior>();
            try
            {
                foreach (ChestBehavior chest in exhausted)
                {
                    InstanceTracker.Remove(chest);
                    removed.Add(chest);
                }
                orig(self);
            }
            finally
            {
                foreach (ChestBehavior chest in removed)
                {
                    if (chest)
                    {
                        InstanceTracker.Add(chest);
                    }
                }
            }
        }

        /// <summary>
        /// Un joueur connecté et vivant peut-il encore ouvrir ce coffre ? Celui qui a acheté la quête
        /// doit toujours avoir un coffre ouvrable : s'il a déjà ouvert celui-ci, il ne compte plus.
        /// Au pire, un coffre camouflé de plus apparaît, comme en vanilla.
        /// </summary>
        private static bool AnyPlayerCanStillOpen(ManagedChest managed, TeamIndex team, CharacterMaster questOwner)
        {
            if (questOwner && managed.HasOpened(questOwner.netId))
            {
                return false;
            }
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                if (!player || !player.isConnected)
                {
                    continue;
                }
                CharacterMaster master = player.master;
                CharacterBody body = master ? master.GetBody() : null;
                bool alive = body && body.healthComponent && body.healthComponent.alive;
                if (master && master.teamIndex == team && alive && !managed.HasOpened(master.netId))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

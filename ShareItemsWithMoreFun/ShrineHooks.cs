using System;
using System.Reflection;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Autels de la chance instanciés (cahier des charges, section 3 quinquies). Vanilla : un seul
    /// état partagé (le prix monte à chaque essai, 2 s d'attente, l'autel s'éteint pour tous après
    /// ses réussites). Avec le mod, l'hôte tient cet état par joueur et l'autel réel reste
    /// disponible. Le tirage vanilla tourne dans un DropContext au nom du joueur : sa réussite
    /// devient une orbe à choix réservée et invisible pour les autres.
    /// Le prix d'un joueur se déduit du prix partagé et de son nombre d'essais, sur l'hôte comme
    /// sur les clients : seul ce nombre circule (ShrineStateMessage).
    /// </summary>
    internal static class ShrineHooks
    {
        /// <summary>Délai vanilla entre deux essais (ShrineChanceBehavior.refreshDuration).</summary>
        public const float RefreshSeconds = 2f;

        // Compteur partagé posé pendant un essai : l'autel vanilla ne doit jamais atteindre son maximum.
        private const int CountSentinel = -1000000;

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo SuccessCountField = typeof(ShrineChanceBehavior).GetField("successfulPurchaseCount", Private);
        private static readonly FieldInfo WaitingField = typeof(ShrineChanceBehavior).GetField("waitingForRefresh", Private);

        public static void Install()
        {
            if (SuccessCountField == null || WaitingField == null)
            {
                Plugin.Log.LogWarning("Autels de la chance : champs de ShrineChanceBehavior introuvables, les autels restent vanilla.");
                return;
            }
            On.RoR2.ShrineChanceBehavior.Start += ShrineChanceBehavior_Start;
            On.RoR2.ShrineChanceBehavior.AddShrineStack += ShrineChanceBehavior_AddShrineStack;
            On.RoR2.PurchaseInteraction.CanBeAffordedByInteractor += PurchaseInteraction_CanBeAffordedByInteractor;
            On.RoR2.PurchaseInteraction.GetContextString += PurchaseInteraction_GetContextString;
            On.RoR2.PurchaseInteraction.UpdateHologramContent += PurchaseInteraction_UpdateHologramContent;
            On.RoR2.UI.PingIndicator.RebuildPing += PingIndicator_RebuildPing;
            On.RoR2.PurchaseAvailabilityIndicator.FixedUpdate += PurchaseAvailabilityIndicator_FixedUpdate;
        }

        // ---------------------------------------------------------------- Repérage (hôte)

        private static void ShrineChanceBehavior_Start(On.RoR2.ShrineChanceBehavior.orig_Start orig, ShrineChanceBehavior self)
        {
            orig(self);
            if (!NetworkServer.active || !ModConfig.Enabled.Value || !ModConfig.InstancedChanceShrines.Value || !Run.instance)
            {
                return;
            }
            ManagedChest managed = ChestHooks.Setup(self.gameObject, instanced: true);
            if (managed)
            {
                managed.shrine = self;
            }
        }

        // ---------------------------------------------------------------- Essai (hôte)

        /// <summary>
        /// Appelé par ChestHooks pour un autel instancié, une fois le joueur trouvé et s'il n'a
        /// pas encore eu toutes ses réussites. Le paiement vanilla se fait au prix du joueur.
        /// </summary>
        public static void Purchase(ManagedChest managed, CharacterMaster player, PurchaseInteraction purchase, Interactor activator,
            On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig)
        {
            ShrinePlayerState state = managed.GetShrineState(player.netId, create: true);
            if (Time.time < state.readyTime)
            {
                return;
            }

            int sharedCost = purchase.cost;
            int playerCost = state.free ? 0 : CostFor(sharedCost, managed.shrine, state.attempts);
            managed.purchaseInProgress = true;
            managed.purchaseSucceeded = false;
            managed.shrinePlayer = player;
            managed.shrineSucceeded = false;
            // Le champ seul, sans SetSyncVar : rien n'est envoyé aux clients, et il est remis tout de suite.
            purchase.cost = playerCost;
            try
            {
                orig(purchase, activator);
            }
            finally
            {
                purchase.cost = sharedCost;
                managed.purchaseInProgress = false;
                managed.shrinePlayer = null;
            }

            if (!managed.purchaseSucceeded)
            {
                return;
            }

            state.attempts++;
            if (playerCost == 0)
            {
                // Vanilla : (int)(0 × 1,4) = 0, un autel piraté reste gratuit.
                state.free = true;
            }
            if (managed.shrineSucceeded)
            {
                state.successes++;
            }
            state.readyTime = Time.time + RefreshSeconds;
            Plugin.Debug($"{player.name} tente {purchase.name} : {(managed.shrineSucceeded ? "réussite" : "échec")} " +
                $"({state.successes}/{MaxSuccesses(managed.shrine)}, essai {state.attempts})");

            new ShrineStateMessage(purchase.netId, player.netId, state, cooldown: true).Send(NetworkDestination.Clients);
            if (state.successes >= MaxSuccesses(managed.shrine))
            {
                managed.MarkOpened(player.netId);
                new ChestOpenedMessage(purchase.netId, player.netId, ChestVisual.PlayOpening).Send(NetworkDestination.Clients);
            }
        }

        private static void ShrineChanceBehavior_AddShrineStack(On.RoR2.ShrineChanceBehavior.orig_AddShrineStack orig, ShrineChanceBehavior self, Interactor activator)
        {
            ManagedChest managed = NetworkServer.active ? self.GetComponent<ManagedChest>() : null;
            CharacterMaster player = managed && managed.purchaseInProgress ? managed.shrinePlayer : null;
            if (!player)
            {
                orig(self, activator);
                return;
            }

            // Le compteur et l'attente vanilla sont partagés : on les neutralise pour cet essai,
            // sinon l'autel s'éteindrait pour tous et FixedUpdate monterait le prix de tous.
            object savedCount = SuccessCountField.GetValue(self);
            SuccessCountField.SetValue(self, CountSentinel);
            try
            {
                using (DropContext.Begin(player, convertToChoices: true, managed.rng))
                {
                    orig(self, activator);
                }
                managed.shrineSucceeded = (int)SuccessCountField.GetValue(self) != CountSentinel;
            }
            finally
            {
                SuccessCountField.SetValue(self, savedCount);
                WaitingField.SetValue(self, false);
            }
        }

        // ---------------------------------------------------------------- Prix par joueur (hôte et clients)

        public static int MaxSuccesses(ShrineChanceBehavior shrine)
        {
            return Math.Max(1, shrine.maxPurchaseCount);
        }

        /// <summary>Même calcul que l'autel vanilla, répété pour chaque essai déjà fait.</summary>
        public static int CostFor(int sharedCost, ShrineChanceBehavior shrine, int attempts)
        {
            int cost = sharedCost;
            for (int i = 0; i < attempts; i++)
            {
                cost = (int)((float)cost * shrine.costMultiplierPerPurchase);
            }
            return cost;
        }

        public static bool TryGetPlayerCost(PurchaseInteraction purchase, CharacterMaster player, out int cost)
        {
            cost = 0;
            ManagedChest managed = player ? purchase.GetComponent<ManagedChest>() : null;
            // Pendant un essai, Purchase a déjà posé le prix du joueur : on ne le recalcule pas dessus.
            if (!managed || !managed.IsInstancedShrine || managed.purchaseInProgress)
            {
                return false;
            }
            ShrinePlayerState state = managed.GetShrineState(player.netId, create: false);
            if (state == null || (state.attempts == 0 && !state.free))
            {
                return false;
            }
            cost = state.free ? 0 : CostFor(purchase.cost, managed.shrine, state.attempts);
            return true;
        }

        private static bool PurchaseInteraction_CanBeAffordedByInteractor(On.RoR2.PurchaseInteraction.orig_CanBeAffordedByInteractor orig, PurchaseInteraction self, Interactor activator)
        {
            if (!TryGetPlayerCost(self, PlayerResolver.FromInteractor(activator), out int cost))
            {
                return orig(self, activator);
            }
            int saved = self.cost;
            self.cost = cost;
            try
            {
                return orig(self, activator);
            }
            finally
            {
                self.cost = saved;
            }
        }

        private static string PurchaseInteraction_GetContextString(On.RoR2.PurchaseInteraction.orig_GetContextString orig, PurchaseInteraction self, Interactor activator)
        {
            if (!TryGetPlayerCost(self, PlayerResolver.FromInteractor(activator), out int cost))
            {
                return orig(self, activator);
            }
            int saved = self.cost;
            self.cost = cost;
            try
            {
                return orig(self, activator);
            }
            finally
            {
                self.cost = saved;
            }
        }

        private static void PurchaseInteraction_UpdateHologramContent(On.RoR2.PurchaseInteraction.orig_UpdateHologramContent orig, PurchaseInteraction self,
            GameObject hologramContentObject, Transform viewer)
        {
            orig(self, hologramContentObject, viewer);
            if (hologramContentObject && TryGetPlayerCost(self, LocalViewer(viewer), out int cost)
                && hologramContentObject.TryGetComponent(out CostHologramContent content))
            {
                content.displayValue = TeamManager.AdjustCostForLongstandingSolitude(self.costType, cost);
            }
        }

        /// <summary>Le ping d'un autel annonce le prix du joueur local, comme son hologramme.</summary>
        private static void PingIndicator_RebuildPing(On.RoR2.UI.PingIndicator.orig_RebuildPing orig, RoR2.UI.PingIndicator self)
        {
            GameObject target = self.pingTarget;
            PurchaseInteraction purchase = target ? target.GetComponent<PurchaseInteraction>() : null;
            if (!purchase || !TryGetPlayerCost(purchase, LocalViewer(null), out int cost))
            {
                orig(self);
                return;
            }
            int saved = purchase.cost;
            purchase.cost = cost;
            try
            {
                orig(self);
            }
            finally
            {
                purchase.cost = saved;
            }
        }

        /// <summary>
        /// Le symbole de l'autel suit sa disponibilité à chaque image (PurchaseAvailabilityIndicator),
        /// et l'autel instancié reste disponible : on l'éteint pour le joueur local qui a eu toutes
        /// ses réussites, et pendant ses 2 s d'attente, comme l'autel vanilla (écran partagé : quand
        /// aucun joueur local ne peut tenter sa chance).
        /// </summary>
        private static void PurchaseAvailabilityIndicator_FixedUpdate(On.RoR2.PurchaseAvailabilityIndicator.orig_FixedUpdate orig, PurchaseAvailabilityIndicator self)
        {
            orig(self);
            if (!self.indicatorObject)
            {
                return;
            }
            ManagedChest managed = self.GetComponent<ManagedChest>();
            if (managed && managed.IsInstancedShrine && !managed.ShrineUsableByAnyLocalPlayer())
            {
                self.indicatorObject.SetActive(false);
            }
        }

        /// <summary>
        /// Le projecteur d'hologramme choisit le joueur le plus proche, quel que soit le client :
        /// on affiche le prix d'un joueur local (celui qui regarde s'il est local, sinon le premier).
        /// </summary>
        public static CharacterMaster LocalViewer(Transform viewer)
        {
            CharacterMaster viewerPlayer = viewer ? PlayerResolver.FromBody(viewer.GetComponent<CharacterBody>()) : null;
            if (viewerPlayer && PlayerResolver.IsLocalPlayer(viewerPlayer.netId))
            {
                return viewerPlayer;
            }
            foreach (LocalUser localUser in LocalUserManager.readOnlyLocalUsersList)
            {
                if (localUser.cachedMaster)
                {
                    return localUser.cachedMaster;
                }
            }
            return null;
        }
    }
}

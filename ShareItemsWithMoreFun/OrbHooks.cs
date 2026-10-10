using RoR2;
using RoR2.Artifacts;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Orbes et ramassables : conversion des drops en orbe à choix, propagation du
    /// propriétaire (gouttelette → orbe), droits d'interaction et remise de l'item choisi.
    /// </summary>
    internal static class OrbHooks
    {
        public static void Install()
        {
            On.RoR2.PickupDropletController.CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 += CreatePickupDroplet;
            On.RoR2.PickupDropletController.OnEnable += PickupDropletController_OnEnable;
            On.RoR2.PickupDropletController.CreatePickup += PickupDropletController_CreatePickup;
            On.RoR2.PickupPickerController.Awake += PickupPickerController_Awake;
            On.RoR2.GenericPickupController.Awake += GenericPickupController_Awake;

            On.RoR2.PickupPickerController.GetInteractability += PickupPickerController_GetInteractability;
            On.RoR2.PickupPickerController.HandlePickupSelected += PickupPickerController_HandlePickupSelected;
            On.RoR2.GenericPickupController.GetInteractability += GenericPickupController_GetInteractability;
            On.RoR2.GenericPickupController.OnTriggerStay += GenericPickupController_OnTriggerStay;
        }

        // ---------------------------------------------------------------- Création (hôte)

        private static void CreatePickupDroplet(On.RoR2.PickupDropletController.orig_CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 orig,
            GenericPickupController.CreatePickupInfo pickupInfo, Vector3 position, Vector3 velocity)
        {
            DropContext context = DropContext.Current;
            if (context != null && context.convertToChoices && NetworkServer.active
                && pickupInfo.pickerOptions == null
                && !CommandArtifactManager.IsCommandArtifactEnabled)
            {
                // Avec l'Artefact du Commandement, on laisse le jeu créer son essence (toujours réservée).
                // En cas d'erreur, ce drop reste un item simple (toujours réservé) au lieu d'être perdu.
                GenericPickupController.CreatePickupInfo converted = pickupInfo;
                try
                {
                    if (ChoiceBuilder.TryConvert(ref converted, context.rng))
                    {
                        pickupInfo = converted;
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Conversion en orbe impossible, item simple à la place : {e}");
                }
            }
            orig(pickupInfo, position, velocity);
            if (context != null)
            {
                context.dropletsCreated++;
            }
        }

        private static void PickupDropletController_OnEnable(On.RoR2.PickupDropletController.orig_OnEnable orig, PickupDropletController self)
        {
            orig(self);
            if (NetworkServer.active)
            {
                DropContext.Current?.Tag(self.gameObject);
            }
        }

        private static void PickupDropletController_CreatePickup(On.RoR2.PickupDropletController.orig_CreatePickup orig, PickupDropletController self)
        {
            OwnedObject owned = NetworkServer.active ? self.GetComponent<OwnedObject>() : null;
            if (!owned || !owned.HasOwner)
            {
                orig(self);
                return;
            }
            // La gouttelette touche le sol : l'orbe (ou le ramassable) hérite de son propriétaire.
            using (DropContext.Begin(owned.OwnerMaster, convertToChoices: false, rng: null))
            {
                orig(self);
            }
        }

        private static void PickupPickerController_Awake(On.RoR2.PickupPickerController.orig_Awake orig, PickupPickerController self)
        {
            orig(self);
            if (NetworkServer.active)
            {
                DropContext.Current?.Tag(self.gameObject);
            }
        }

        private static void GenericPickupController_Awake(On.RoR2.GenericPickupController.orig_Awake orig, GenericPickupController self)
        {
            orig(self);
            if (NetworkServer.active)
            {
                DropContext.Current?.Tag(self.gameObject);
            }
        }

        // ---------------------------------------------------------------- Droits (hôte et clients)

        private static Interactability PickupPickerController_GetInteractability(On.RoR2.PickupPickerController.orig_GetInteractability orig, PickupPickerController self, Interactor activator)
        {
            Interactability result = orig(self, activator);
            if (result == Interactability.Disabled && RemoteOperation.CanOpenOrb(self, activator))
            {
                // Le joueur mort qui pilote un drone peut ouvrir sa propre orbe.
                result = RemoteOperation.EvaluateAsAllowed(self, activator, orig);
            }
            if (result != Interactability.Disabled && !OwnedObject.Allows(self.gameObject, PlayerResolver.FromInteractor(activator)))
            {
                return Interactability.Disabled;
            }
            return result;
        }

        private static Interactability GenericPickupController_GetInteractability(On.RoR2.GenericPickupController.orig_GetInteractability orig, GenericPickupController self, Interactor activator)
        {
            Interactability result = orig(self, activator);
            if (result != Interactability.Disabled && !OwnedObject.Allows(self.gameObject, PlayerResolver.FromInteractor(activator)))
            {
                return Interactability.Disabled;
            }
            return result;
        }

        private static void GenericPickupController_OnTriggerStay(On.RoR2.GenericPickupController.orig_OnTriggerStay orig, GenericPickupController self, Collider other)
        {
            // Ramassage automatique en marchant dessus : réservé au propriétaire.
            if (NetworkServer.active && other && !OwnedObject.Allows(self.gameObject, PlayerResolver.FromBody(other.GetComponent<CharacterBody>())))
            {
                return;
            }
            orig(self, other);
        }

        // ---------------------------------------------------------------- Choix (hôte)

        private static void PickupPickerController_HandlePickupSelected(On.RoR2.PickupPickerController.orig_HandlePickupSelected orig, PickupPickerController self, int choiceIndex)
        {
            OwnedObject owned = NetworkServer.active ? self.GetComponent<OwnedObject>() : null;
            if (!owned || !owned.HasOwner)
            {
                orig(self, choiceIndex);
                return;
            }
            NetworkUIPromptController prompt = self.GetComponent<NetworkUIPromptController>();
            CharacterMaster participant = prompt ? prompt.currentParticipantMaster : null;
            CharacterBody participantBody = participant ? participant.GetBody() : null;

            // Un joueur mort aux commandes d'un drone ne peut pas tout ramasser au sol : il reçoit toujours en direct.
            if (!ModConfig.GrantToInventory.Value && !(participantBody && participantBody.isRemoteOp))
            {
                // L'item choisi tombe au sol, mais reste à son propriétaire (réservé, caché aux autres).
                OrigWithOwner(orig, self, choiceIndex, owned.OwnerMaster);
                return;
            }

            if ((uint)choiceIndex >= (uint)self.options.Length)
            {
                return;
            }
            PickupPickerController.Option option = self.options[choiceIndex];
            if (!option.available)
            {
                return;
            }

            if (!participant || !OwnedObject.Allows(self.gameObject, PlayerResolver.FromMaster(participant)))
            {
                return;
            }

            if (!Grant.ToMaster(participant, option.pickup, self.transform.position))
            {
                OrigWithOwner(orig, self, choiceIndex, participant);
                return;
            }

            Plugin.Debug($"{participant.name} choisit {option.pickup.pickupIndex}");
            prompt.SetParticipantMaster(null);
            NetworkServer.Destroy(self.gameObject);
        }

        /// <summary>Choix vanilla (item posé au sol), dans un contexte qui le réserve au joueur.</summary>
        private static void OrigWithOwner(On.RoR2.PickupPickerController.orig_HandlePickupSelected orig, PickupPickerController self, int choiceIndex, CharacterMaster owner)
        {
            using (DropContext.Begin(owner, convertToChoices: false, rng: null))
            {
                orig(self, choiceIndex);
            }
        }
    }
}

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
                ChoiceBuilder.TryConvert(ref pickupInfo, context.rng);
            }
            orig(pickupInfo, position, velocity);
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
            if (!owned || !owned.HasOwner || !ModConfig.GrantToInventory.Value)
            {
                orig(self, choiceIndex);
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

            NetworkUIPromptController prompt = self.GetComponent<NetworkUIPromptController>();
            CharacterMaster participant = prompt ? prompt.currentParticipantMaster : null;
            if (!participant || !OwnedObject.Allows(self.gameObject, PlayerResolver.FromMaster(participant)))
            {
                return;
            }

            CharacterMaster dropOwner = owned.OwnerMaster ? owned.OwnerMaster : participant;
            if (!Grant.ToMaster(participant, option.pickup, self.transform.position, dropOwner))
            {
                orig(self, choiceIndex);
                return;
            }

            Plugin.Debug($"{participant.name} choisit {option.pickup.pickupIndex}");
            prompt.SetParticipantMaster(null);
            NetworkServer.Destroy(self.gameObject);
        }
    }
}

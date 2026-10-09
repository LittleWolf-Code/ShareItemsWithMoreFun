using RoR2;
using RoR2.Orbs;
using UnityEngine;

namespace ShareItemsWithMoreFun
{
    /// <summary>Donne directement l'item choisi au joueur, comme PickupPickerController.AttemptGrantPickupDirectly.</summary>
    internal static class Grant
    {
        /// <returns>false si le type de ramassable n'est pas géré : l'appelant retombe sur le vanilla.</returns>
        public static bool ToMaster(CharacterMaster master, UniquePickup pickup, Vector3 origin, CharacterMaster dropOwner)
        {
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            CharacterBody body = master ? master.GetBody() : null;
            if (def == null || !body || !master.inventory)
            {
                return false;
            }

            if (def.itemIndex != ItemIndex.None)
            {
                ItemTransferOrb.DispatchItemTransferOrb(new ItemTransferOrb.DispatchArgs
                {
                    inventoryToGrantTo = master.inventory,
                    itemIndex = def.itemIndex,
                    origin = origin,
                    permanentCount = (!pickup.isTempItem && !pickup.isRental) ? 1 : 0,
                    temporaryCount = pickup.isTempItem ? 1 : 0,
                    rentalCount = pickup.isRental ? 1 : 0,
                    travelDuration = 1f,
                    isPickup = true,
                    orbDestinationOverride = body.networkIdentity
                });
                return true;
            }

            if (def.equipmentIndex != EquipmentIndex.None)
            {
                var context = new PickupDef.GrantContext
                {
                    body = body,
                    controller = null,
                    pickup = pickup,
                    position = origin,
                    rotation = Quaternion.identity
                };
                def.attemptGrant?.Invoke(ref context);
                body.OnPickup(CharacterBody.PickupClass.Item);
                if (context.shouldNotify)
                {
                    GenericPickupController.SendPickupMessage(master, pickup);
                }
                // L'équipement remplacé n'est pas perdu : il retombe au sol, réservé au même joueur.
                UniquePickup previous = context.pickup;
                if (!context.shouldDestroy && previous.isValid && previous.pickupIndex != pickup.pickupIndex)
                {
                    using (DropContext.Begin(dropOwner, convertToChoices: false, rng: null))
                    {
                        PickupDropletController.CreatePickupDroplet(previous, origin + Vector3.up, Vector3.up * 15f, false);
                    }
                }
                return true;
            }

            return false;
        }
    }
}

using System;
using System.Reflection;
using RoR2;
using RoR2.Orbs;
using UnityEngine;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Donne directement l'item choisi au joueur, par le même chemin qu'un ramassage au sol
    /// (PickupDef.attemptGrant) : Duplicator, groupes mutuellement exclusifs, karma, etc.
    /// Une orbe de transfert purement visuelle montre l'item voler vers le joueur.
    /// </summary>
    internal static class Grant
    {
        private static readonly FieldInfo PickupConsumedField =
            typeof(GenericPickupController).GetField("onPickupConsumed", BindingFlags.Static | BindingFlags.NonPublic);

        /// <returns>false si le type de ramassable n'est pas géré : l'appelant retombe sur le vanilla.</returns>
        public static bool ToMaster(CharacterMaster master, UniquePickup pickup, Vector3 origin)
        {
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            CharacterBody body = master ? master.GetBody() : null;
            if (def == null || def.attemptGrant == null || !body || !master.inventory)
            {
                return false;
            }
            bool isItem = def.itemIndex != ItemIndex.None;
            bool isEquipment = def.equipmentIndex != EquipmentIndex.None;
            if (!isItem && !isEquipment)
            {
                return false;
            }
            if (isEquipment && body.isRemoteOp)
            {
                // Joueur mort aux commandes d'un drone : l'équipement attend son retour.
                PendingEquipment.Add(master, pickup);
                return true;
            }

            var context = new PickupDef.GrantContext
            {
                body = body,
                controller = null,
                pickup = pickup,
                position = origin,
                rotation = Quaternion.identity
            };
            def.attemptGrant(ref context);
            body.OnPickup(pickup.isTempItem ? CharacterBody.PickupClass.TempItem : CharacterBody.PickupClass.Item);
            if (context.shouldNotify)
            {
                GenericPickupController.SendPickupMessage(master, pickup);
            }
            if (context.shouldDestroy)
            {
                RaisePickupConsumed(body, pickup);
            }

            if (isItem)
            {
                // Visuel seulement : l'item est déjà dans l'inventaire.
                ItemTransferOrb.DispatchItemTransferOrb(new ItemTransferOrb.DispatchArgs
                {
                    inventoryToGrantTo = master.inventory,
                    itemIndex = def.itemIndex,
                    origin = origin,
                    permanentCount = 0,
                    temporaryCount = 0,
                    rentalCount = 0,
                    travelDuration = 1f,
                    isPickup = false,
                    onArrivalBehavior = _ => { },
                    orbDestinationOverride = body.networkIdentity
                });
            }
            else if (!context.shouldDestroy && context.pickup.isValid)
            {
                // L'équipement remplacé n'est pas perdu : il retombe au sol, réservé à ce même joueur,
                // et n'est jamais transformé en essence de commandement.
                UniquePickup previous = context.pickup;
                previous.ignoreArtifactBasedPickupReplacements = true;
                using (DropContext.Begin(master, convertToChoices: false, rng: null))
                {
                    PickupDropletController.CreatePickupDroplet(previous, origin + Vector3.up, Vector3.up * 15f, false);
                }
            }
            return true;
        }

        private static void RaisePickupConsumed(CharacterBody body, UniquePickup pickup)
        {
            try
            {
                (PickupConsumedField?.GetValue(null) as Action<CharacterBody, UniquePickup>)?.Invoke(body, pickup);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }
}

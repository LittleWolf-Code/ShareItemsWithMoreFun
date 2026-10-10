using System.Collections.Generic;
using System.Reflection;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Coffres marqués (cahier des charges, section 3 sexies). L'hôte marque un coffre instancié à sa
    /// première ouverture ou quand un joueur le pinge, puis prévient les clients. Chaque client montre
    /// un marqueur local sur les coffres marqués que son joueur n'a pas encore ouverts : l'indicateur
    /// de ping vanilla (contour et icône), sans minuteur, sans message dans le chat et sans son.
    /// Chaque joueur a ainsi sa propre liste, quel que soit le nombre de joueurs.
    /// </summary>
    internal static class ChestMarkers
    {
        private const float RefreshSeconds = 0.25f;

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo FixedTimerField = typeof(PingIndicator).GetField("fixedTimer", Private);

        private sealed class Marker
        {
            public ManagedChest chest;
            public PingIndicator indicator;
        }

        private static readonly List<Marker> markers = new List<Marker>();
        private static GameObject prefab;
        private static bool buildingMarker;
        private static float nextRefresh;

        public static void Install()
        {
            if (FixedTimerField == null)
            {
                Plugin.Log.LogWarning("Coffres marqués : minuteur du ping introuvable, le marqueur sera recréé à chaque fin de ping.");
            }
            On.RoR2.PingerController.CmdPing += PingerController_CmdPing;
            On.RoR2.Chat.AddMessage_string += Chat_AddMessage_string;
        }

        // ---------------------------------------------------------------- Marquage (hôte)

        public static void MarkServer(ManagedChest managed)
        {
            if (!NetworkServer.active || !ModConfig.Enabled.Value || !ModConfig.PersistentChestMarkers.Value)
            {
                return;
            }
            if (!managed || managed.marked || !managed.serverManaged || !managed.instanced || managed.globallyOpened
                || managed.shrine || !managed.purchase)
            {
                return;
            }
            managed.marked = true;
            Plugin.Debug($"Coffre marqué : {managed.name}");
            new ChestMarkedMessage(managed.purchase.netId).Send(NetworkDestination.Clients);
        }

        /// <summary>Le serveur reçoit ici le ping de chaque joueur, hôte compris.</summary>
        private static void PingerController_CmdPing(On.RoR2.PingerController.orig_CmdPing orig, PingerController self, PingerController.PingInfo incomingPing)
        {
            orig(self, incomingPing);
            if (!NetworkServer.active || !incomingPing.active)
            {
                return;
            }
            GameObject target = incomingPing.targetGameObject;
            ManagedChest managed = target ? target.GetComponent<ManagedChest>() : null;
            if (managed)
            {
                MarkServer(managed);
            }
        }

        // ---------------------------------------------------------------- Affichage (hôte et clients)

        /// <summary>Appelé à chaque image : crée ou retire les marqueurs selon ce que le joueur local a ouvert.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + RefreshSeconds;

            for (int i = markers.Count - 1; i >= 0; i--)
            {
                Marker marker = markers[i];
                if (marker.chest && marker.indicator && ShouldShow(marker.chest))
                {
                    continue;
                }
                if (marker.indicator)
                {
                    Object.Destroy(marker.indicator.gameObject);
                }
                markers.RemoveAt(i);
            }

            foreach (ManagedChest managed in ManagedChest.Instances)
            {
                if (managed && managed.marked && !HasMarker(managed) && ShouldShow(managed))
                {
                    Create(managed);
                }
            }
        }

        private static bool ShouldShow(ManagedChest managed)
        {
            if (!managed.marked || !managed.instanced || managed.globallyOpened || managed.shrine)
            {
                return false;
            }
            // Un coffre redevenu vanilla et ouvert n'est plus disponible : plus de marqueur.
            PurchaseInteraction purchase = managed.GetComponent<PurchaseInteraction>();
            if (!purchase || !purchase.available)
            {
                return false;
            }
            // Coffre (ou multimagasin) dans le sac d'un Drifter : caché, et vanilla y efface les pings.
            if (IsCarried(managed.gameObject))
            {
                return false;
            }
            MultiShopController shop = managed.GetComponentInParent<MultiShopController>();
            if (shop && IsCarried(shop.gameObject))
            {
                return false;
            }
            // Écran partagé : tant qu'un joueur local ne l'a pas ouvert.
            return LocalUserManager.readOnlyLocalUsersList.Count > 0 && !managed.OpenedByAllLocalPlayers();
        }

        private static bool IsCarried(GameObject gameObject)
        {
            return VehicleSeat.FindVehicleSeatWithPassenger(gameObject);
        }

        private static bool HasMarker(ManagedChest managed)
        {
            foreach (Marker marker in markers)
            {
                if (marker.chest == managed)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Create(ManagedChest managed)
        {
            if (!prefab)
            {
                prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/PingIndicator");
                if (!prefab)
                {
                    return;
                }
            }
            // Comme PingerController : le prefab ne joue aucun son, seul le chat est à couper.
            GameObject instance = Object.Instantiate(prefab);
            PingIndicator indicator = instance.GetComponent<PingIndicator>();
            if (!indicator)
            {
                Object.Destroy(instance);
                return;
            }
            indicator.pingOwner = null;
            indicator.pingOrigin = managed.transform.position;
            indicator.pingNormal = Vector3.up;
            indicator.pingTarget = managed.gameObject;
            buildingMarker = true;
            try
            {
                indicator.RebuildPing();
            }
            finally
            {
                buildingMarker = false;
            }
            if (indicator.pingText)
            {
                indicator.pingText.enabled = false;
            }
            FixedTimerField?.SetValue(indicator, float.PositiveInfinity);
            markers.Add(new Marker { chest = managed, indicator = indicator });
        }

        /// <summary>RebuildPing écrit « X a pingé... » dans le chat local : pas pour un marqueur.</summary>
        private static void Chat_AddMessage_string(On.RoR2.Chat.orig_AddMessage_string orig, string message)
        {
            if (buildingMarker)
            {
                return;
            }
            orig(message);
        }
    }
}

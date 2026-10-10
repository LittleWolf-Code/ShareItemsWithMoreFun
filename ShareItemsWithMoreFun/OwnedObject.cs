using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Propriétaire d'une gouttelette, d'une orbe ou d'un ramassable. L'hôte le pose et le
    /// diffuse (OwnerMessage) ; chaque client cache localement les objets des autres joueurs.
    /// </summary>
    internal class OwnedObject : MonoBehaviour
    {
        public static readonly HashSet<OwnedObject> Instances = new HashSet<OwnedObject>();

        private const float HideRefreshSeconds = 0.25f;
        // Le modèle d'une orbe apparaît quelques images après elle : passage à chaque image au début.
        private const float FastHideSeconds = 3f;
        private const float OwnerCheckSeconds = 1f;

        public NetworkInstanceId ownerId = NetworkInstanceId.Invalid;
        public bool reserve;
        public bool hide;
        /// <summary>Le propriétaire peut l'ouvrir depuis un drone piloté (réglage de l'hôte).</summary>
        public bool remoteOpAllowed;

        private CharacterMaster cachedOwner;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Light> hiddenLights = new List<Light>();
        private Highlight hiddenHighlight;
        private float nextHideRefresh;
        private float fastHideUntil;
        private float nextOwnerCheck;
        private float ownerMissingSince = -1f;

        public bool HasOwner => ownerId != NetworkInstanceId.Invalid;

        public CharacterMaster OwnerMaster
        {
            get
            {
                if (!cachedOwner && HasOwner)
                {
                    GameObject ownerObject = Util.FindNetworkObject(ownerId);
                    cachedOwner = ownerObject ? ownerObject.GetComponent<CharacterMaster>() : null;
                }
                return cachedOwner;
            }
        }

        public static OwnedObject GetOrAdd(GameObject gameObject)
        {
            OwnedObject owned = gameObject.GetComponent<OwnedObject>();
            return owned ? owned : gameObject.AddComponent<OwnedObject>();
        }

        /// <summary>Le joueur derrière cet interacteur a-t-il le droit d'utiliser l'objet ?</summary>
        public static bool Allows(GameObject gameObject, CharacterMaster player)
        {
            OwnedObject owned = gameObject.GetComponent<OwnedObject>();
            if (!owned || !owned.HasOwner || !owned.reserve)
            {
                return true;
            }
            return player && player.netId == owned.ownerId;
        }

        public void SetOwnerServer(NetworkInstanceId owner, bool reserveForOwner, bool hideFromOthers, bool droneAllowed)
        {
            Apply(owner, reserveForOwner, hideFromOthers, droneAllowed);
        }

        public void Apply(NetworkInstanceId owner, bool reserveForOwner, bool hideFromOthers, bool droneAllowed)
        {
            if (owner != ownerId)
            {
                cachedOwner = null;
            }
            ownerId = owner;
            reserve = HasOwner && reserveForOwner;
            hide = HasOwner && hideFromOthers;
            remoteOpAllowed = HasOwner && droneAllowed;
            nextHideRefresh = 0f;
            fastHideUntil = Time.unscaledTime + FastHideSeconds;
            ownerMissingSince = -1f;
            if (ShouldHideLocally())
            {
                HideNow();
            }
            else
            {
                ShowAgain();
            }
        }

        public bool ShouldHideLocally()
        {
            return hide && HasOwner && LocalUserManager.readOnlyLocalUsersList.Count > 0 && !PlayerResolver.IsLocalPlayer(ownerId);
        }

        private void OnEnable()
        {
            Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        private void Update()
        {
            // Hôte : une orbe dont le propriétaire a quitté la partie devient libre pour tous.
            if (NetworkServer.active && HasOwner && Time.unscaledTime >= nextOwnerCheck)
            {
                nextOwnerCheck = Time.unscaledTime + OwnerCheckSeconds;
                CharacterMaster owner = OwnerMaster;
                PlayerCharacterMasterController controller = owner ? owner.playerCharacterMasterController : null;
                bool gone = !owner;
                bool disconnected = controller && !controller.isConnected;
                if (!gone && !disconnected)
                {
                    ownerMissingSince = -1f;
                }
                else
                {
                    if (ownerMissingSince < 0f)
                    {
                        ownerMissingSince = Time.unscaledTime;
                    }
                    // Un joueur qui se reconnecte retrouve le même CharacterMaster : on lui laisse le temps de revenir.
                    float grace = ModConfig.DisconnectGraceSeconds.Value;
                    if (gone || (grace > 0f && Time.unscaledTime - ownerMissingSince >= grace))
                    {
                        Plugin.Debug($"{name} libéré : propriétaire absent");
                        Apply(NetworkInstanceId.Invalid, false, false, false);
                        OwnerMessage.Broadcast(gameObject);
                    }
                }
            }
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if ((now < nextHideRefresh && now >= fastHideUntil) || !ShouldHideLocally())
            {
                return;
            }
            // Le modèle de l'orbe est chargé de façon asynchrone : on repasse régulièrement.
            nextHideRefresh = now + HideRefreshSeconds;
            HideNow();
        }

        private void HideNow()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }
            foreach (Light light in GetComponentsInChildren<Light>(true))
            {
                if (light.enabled)
                {
                    light.enabled = false;
                    hiddenLights.Add(light);
                }
            }
            Highlight highlight = GetComponent<Highlight>();
            if (highlight && highlight.enabled)
            {
                highlight.enabled = false;
                hiddenHighlight = highlight;
            }
        }

        private void ShowAgain()
        {
            foreach (Renderer renderer in hiddenRenderers)
            {
                if (renderer)
                {
                    renderer.enabled = true;
                }
            }
            foreach (Light light in hiddenLights)
            {
                if (light)
                {
                    light.enabled = true;
                }
            }
            if (hiddenHighlight)
            {
                hiddenHighlight.enabled = true;
            }
            hiddenRenderers.Clear();
            hiddenLights.Clear();
            hiddenHighlight = null;
        }
    }
}

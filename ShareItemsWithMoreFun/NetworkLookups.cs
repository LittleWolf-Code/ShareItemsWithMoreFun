using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Nos messages passent par un autre canal que l'apparition des objets : un message peut
    /// arriver avant l'objet qu'il désigne. On le garde alors en attente, et les messages en
    /// attente sont rejoués dans leur ordre d'arrivée (une libération ne doit pas être écrasée
    /// par une attribution plus ancienne).
    /// </summary>
    internal static class NetworkLookups
    {
        private const float TimeoutSeconds = 10f;

        private struct Pending
        {
            public NetworkInstanceId id;
            public Action<GameObject> action;
            public float expiresAt;
        }

        private static readonly List<Pending> pending = new List<Pending>();

        public static void WhenFound(NetworkInstanceId id, Action<GameObject> action)
        {
            GameObject found = Util.FindNetworkObject(id);
            // Un message plus ancien pour le même objet attend encore : on passe derrière lui.
            if (found && !pending.Exists(p => p.id == id))
            {
                Run(action, found);
                return;
            }
            pending.Add(new Pending { id = id, action = action, expiresAt = Time.unscaledTime + TimeoutSeconds });
        }

        public static void Tick()
        {
            if (pending.Count == 0)
            {
                return;
            }
            if (NetworkClient.active && !ClientScene.ready)
            {
                // Chargement d'étape en cours : le délai ne court qu'une fois le client prêt.
                for (int i = 0; i < pending.Count; i++)
                {
                    Pending entry = pending[i];
                    entry.expiresAt = Time.unscaledTime + TimeoutSeconds;
                    pending[i] = entry;
                }
                return;
            }

            List<KeyValuePair<Pending, GameObject>> due = null;
            int write = 0;
            for (int read = 0; read < pending.Count; read++)
            {
                Pending entry = pending[read];
                GameObject found = Util.FindNetworkObject(entry.id);
                if (found)
                {
                    (due ??= new List<KeyValuePair<Pending, GameObject>>()).Add(new KeyValuePair<Pending, GameObject>(entry, found));
                }
                else if (Time.unscaledTime >= entry.expiresAt)
                {
                    Plugin.Debug($"Objet réseau {entry.id} jamais trouvé, message ignoré");
                }
                else
                {
                    pending[write++] = entry;
                }
            }
            pending.RemoveRange(write, pending.Count - write);

            if (due != null)
            {
                foreach (KeyValuePair<Pending, GameObject> pair in due)
                {
                    Run(pair.Key.action, pair.Value);
                }
            }
        }

        private static void Run(Action<GameObject> action, GameObject target)
        {
            try
            {
                action(target);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }
}

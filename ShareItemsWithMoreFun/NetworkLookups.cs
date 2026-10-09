using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Nos messages passent par un autre canal que l'apparition des objets : un message peut
    /// arriver avant l'objet qu'il désigne. On le garde alors en attente quelques secondes.
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
            if (found)
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
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending entry = pending[i];
                GameObject found = Util.FindNetworkObject(entry.id);
                if (found)
                {
                    pending.RemoveAt(i);
                    Run(entry.action, found);
                }
                else if (Time.unscaledTime >= entry.expiresAt)
                {
                    pending.RemoveAt(i);
                    Plugin.Debug($"Objet réseau {entry.id} jamais trouvé, message ignoré");
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

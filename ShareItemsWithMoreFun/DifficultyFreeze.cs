using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Fige la difficulté (barre de difficulté, niveau des monstres, coûts) sans arrêter le chrono
    /// de la partie : le coffre minuté, les succès chronométrés et le temps de la partie restent
    /// vanilla. On passe par l'override que le jeu prévoit déjà (OverrideDifficultyArgs) : un
    /// composant de scène qui fixe le temps utilisé pour calculer la difficulté. Il est détruit
    /// avec la scène, donc la difficulté repart toute seule à l'étape suivante. Chaque machine
    /// calcule la difficulté localement : l'hôte envoie donc l'état aux clients.
    /// </summary>
    internal static class DifficultyFreeze
    {
        private static GameObject freezeObject;

        public static bool IsFrozen => freezeObject;
        public static float FrozenStopwatch { get; private set; }
        public static int FrozenStageClearCount { get; private set; }

        /// <summary>Hôte : fige la difficulté à la valeur actuelle et prévient les clients.</summary>
        public static void FreezeServer()
        {
            if (!NetworkServer.active || !Run.instance || IsFrozen)
            {
                return;
            }
            if (Apply(Run.instance.GetRunStopwatch(), Run.instance.stageClearCount))
            {
                new DifficultyFreezeMessage(true, FrozenStopwatch, FrozenStageClearCount).Send(NetworkDestination.Clients);
                Plugin.Debug($"Téléporteur chargé : difficulté figée à {FrozenStopwatch:0} s");
            }
        }

        /// <summary>Hôte : relâche la difficulté (option désactivée en cours d'étape).</summary>
        public static void ReleaseServer()
        {
            if (!NetworkServer.active || !IsFrozen)
            {
                return;
            }
            Release();
            new DifficultyFreezeMessage(false, 0f, 0).Send(NetworkDestination.Clients);
            Plugin.Debug("Difficulté relâchée");
        }

        public static bool Apply(float stopwatch, int stageClearCount)
        {
            Release();
            // Une scène qui impose déjà sa propre difficulté garde la main.
            if (InstanceTracker.GetInstancesList<OverrideDifficultyArgs>().Count > 0)
            {
                return false;
            }
            FrozenStopwatch = stopwatch;
            FrozenStageClearCount = stageClearCount;
            // Créé inactif : OnEnable lit les valeurs, il faut donc les poser avant l'activation.
            freezeObject = new GameObject("ShareItemsWithMoreFun_DifficultyFreeze");
            freezeObject.SetActive(false);
            OverrideDifficultyArgs args = freezeObject.AddComponent<OverrideDifficultyArgs>();
            args.timeOverrideValue = Mathf.Max(0f, stopwatch);
            args.stageClearCountOverrideValue = Mathf.Max(0, stageClearCount);
            freezeObject.SetActive(true);
            return true;
        }

        public static void Release()
        {
            if (freezeObject)
            {
                // Désactivé tout de suite (OnDisable remet l'override à zéro), détruit en fin d'image.
                freezeObject.SetActive(false);
                Object.Destroy(freezeObject);
            }
            freezeObject = null;
        }
    }

    /// <summary>Hôte → clients : la difficulté est figée (ou relâchée).</summary>
    internal class DifficultyFreezeMessage : INetMessage
    {
        private bool frozen;
        private float stopwatch;
        private int stageClearCount;

        public DifficultyFreezeMessage()
        {
        }

        public DifficultyFreezeMessage(bool frozen, float stopwatch, int stageClearCount)
        {
            this.frozen = frozen;
            this.stopwatch = stopwatch;
            this.stageClearCount = stageClearCount;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(frozen);
            writer.Write(stopwatch);
            writer.Write(stageClearCount);
        }

        public void Deserialize(NetworkReader reader)
        {
            frozen = reader.ReadBoolean();
            stopwatch = reader.ReadSingle();
            stageClearCount = reader.ReadInt32();
        }

        public void OnReceived()
        {
            if (NetworkServer.active)
            {
                return; // L'hôte a déjà appliqué l'état lui-même.
            }
            if (frozen)
            {
                DifficultyFreeze.Apply(stopwatch, stageClearCount);
            }
            else
            {
                DifficultyFreeze.Release();
            }
        }
    }
}

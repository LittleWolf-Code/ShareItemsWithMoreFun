using System.Collections;
using EntityStates;
using RoR2;
using UnityEngine;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Affichage local d'un coffre instancié : seul le joueur qui l'a ouvert le voit s'ouvrir.
    /// Le serveur ne change pas l'état du coffre, donc rien n'est envoyé aux autres clients.
    /// </summary>
    internal static class ClientVisuals
    {
        private const float OpeningSeconds = 1f;

        public static void PlayLocalOpening(GameObject chestObject)
        {
            ModelLocator modelLocator = chestObject.GetComponent<ModelLocator>();
            Transform model = modelLocator ? modelLocator.modelTransform : null;
            Animator animator = model ? model.GetComponent<Animator>() : null;
            if (animator && AnimatorBridge.Play(animator, "Body", "Opening", "Opening.playbackRate", OpeningSeconds))
            {
                ManagedChest managed = chestObject.GetComponent<ManagedChest>();
                if (managed)
                {
                    managed.StartCoroutine(FinishOpening(animator));
                }
            }

            SfxLocator sfx = chestObject.GetComponent<SfxLocator>();
            if (sfx && !string.IsNullOrEmpty(sfx.openSound))
            {
                Util.PlaySound(sfx.openSound, chestObject);
            }
        }

        private static IEnumerator FinishOpening(Animator animator)
        {
            yield return new WaitForSeconds(OpeningSeconds);
            if (animator)
            {
                AnimatorBridge.Play(animator, "Body", "Opened");
            }
        }

        /// <summary>Accès aux helpers d'animation protégés d'EntityState, sans créer d'état.</summary>
        private sealed class AnimatorBridge : EntityState
        {
            public static bool Play(Animator animator, string layer, string state, string playbackRateParam, float duration)
            {
                if (!HasState(animator, layer, state))
                {
                    return false;
                }
                PlayAnimationOnAnimator(animator, layer, state, playbackRateParam, duration);
                return true;
            }

            public static bool Play(Animator animator, string layer, string state)
            {
                if (!HasState(animator, layer, state))
                {
                    return false;
                }
                PlayAnimationOnAnimator(animator, layer, state);
                return true;
            }

            private static bool HasState(Animator animator, string layer, string state)
            {
                int layerIndex = animator.GetLayerIndex(layer);
                return layerIndex >= 0 && animator.HasState(layerIndex, Animator.StringToHash(state));
            }
        }
    }
}

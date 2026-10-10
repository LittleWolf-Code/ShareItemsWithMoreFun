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
            ManagedChest managed = chestObject.GetComponent<ManagedChest>();
            if (managed)
            {
                managed.localOpenedShown = true;
            }
            if (TryShowShrineDone(chestObject, managed))
            {
                return;
            }
            ShopTerminalBehavior terminal = chestObject.GetComponent<ShopTerminalBehavior>();
            if (terminal)
            {
                // Écran partagé : on ne ferme que si plus aucun joueur local ne peut l'acheter.
                if (!managed || managed.OpenedByAllLocalPlayers())
                {
                    CloseShopTerminal(terminal, playSound: true);
                }
                return;
            }
            Animator animator = FindAnimator(chestObject);
            if (animator && AnimatorBridge.Play(animator, "Body", "Opening", "Opening.playbackRate", OpeningSeconds) && managed)
            {
                managed.StartCoroutine(FinishOpening(animator));
            }

            SfxLocator sfx = chestObject.GetComponent<SfxLocator>();
            if (sfx && !string.IsNullOrEmpty(sfx.openSound))
            {
                Util.PlaySound(sfx.openSound, chestObject);
            }
        }

        /// <summary>Affiche directement le coffre ouvert (joueur qui se reconnecte), sans son.</summary>
        public static void ShowOpened(GameObject chestObject)
        {
            ManagedChest managed = chestObject.GetComponent<ManagedChest>();
            if (managed)
            {
                managed.localOpenedShown = true;
            }
            if (TryShowShrineDone(chestObject, managed))
            {
                return;
            }
            ShopTerminalBehavior terminal = chestObject.GetComponent<ShopTerminalBehavior>();
            if (terminal)
            {
                if (!managed || managed.OpenedByAllLocalPlayers())
                {
                    CloseShopTerminal(terminal, playSound: false);
                }
                return;
            }
            Animator animator = FindAnimator(chestObject);
            if (animator)
            {
                AnimatorBridge.Play(animator, "Body", "Opened");
            }
        }

        /// <summary>
        /// Autel de la chance dont le joueur local a eu toutes ses réussites : son symbole s'éteint,
        /// comme l'autel vanilla épuisé (écran partagé : quand tous les joueurs locaux ont fini).
        /// ShrineHooks le garde ensuite éteint à chaque image.
        /// </summary>
        private static bool TryShowShrineDone(GameObject chestObject, ManagedChest managed)
        {
            ShrineChanceBehavior shrine = chestObject.GetComponent<ShrineChanceBehavior>();
            if (!shrine)
            {
                return false;
            }
            if ((!managed || managed.OpenedByAllLocalPlayers()) && shrine.symbolTransform)
            {
                shrine.symbolTransform.gameObject.SetActive(false);
            }
            return true;
        }

        /// <summary>Terminal de multimagasin : item caché et volet fermé, comme après un achat vanilla.</summary>
        private static void CloseShopTerminal(ShopTerminalBehavior terminal, bool playSound)
        {
            // Tout l'affichage de l'item (modèle, particules, modèle chargé plus tard) : on coupe son objet.
            GameObject display = terminal.pickupDisplay ? terminal.pickupDisplay.gameObject : null;
            if (display && display != terminal.gameObject)
            {
                display.SetActive(false);
            }
            else
            {
                terminal.HideItemDisplay(true);
            }
            Animator animator = terminal.animator;
            if (animator)
            {
                int layer = animator.GetLayerIndex("Body");
                if (layer >= 0 && animator.HasState(layer, Animator.StringToHash("Closing")))
                {
                    animator.PlayInFixedTime("Closing", layer);
                }
            }
            if (playSound)
            {
                Util.PlaySound("Play_UI_tripleChestShutter", terminal.gameObject);
            }
        }

        private static Animator FindAnimator(GameObject chestObject)
        {
            ModelLocator modelLocator = chestObject.GetComponent<ModelLocator>();
            Transform model = modelLocator ? modelLocator.modelTransform : null;
            return model ? model.GetComponent<Animator>() : null;
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

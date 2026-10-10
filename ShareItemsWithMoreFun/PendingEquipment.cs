using System.Collections.Generic;
using RoR2;
using UnityEngine.Networking;

namespace ShareItemsWithMoreFun
{
    /// <summary>
    /// Un joueur mort qui pilote un drone ne peut ni utiliser ni ramasser d'équipement (règle vanilla).
    /// Un équipement choisi dans une orbe à ce moment-là est mis de côté et donné au retour de son
    /// vrai corps (étape suivante ou résurrection) : l'équipement qu'il tenait n'est jamais perdu.
    /// </summary>
    internal static class PendingEquipment
    {
        private static readonly Dictionary<CharacterMaster, List<UniquePickup>> pending = new Dictionary<CharacterMaster, List<UniquePickup>>();

        public static void Install()
        {
            CharacterBody.onBodyStartGlobal += OnBodyStart;
            Run.onRunDestroyGlobal += _ => pending.Clear();
        }

        public static void Add(CharacterMaster master, UniquePickup pickup)
        {
            if (!pending.TryGetValue(master, out List<UniquePickup> list))
            {
                list = new List<UniquePickup>();
                pending[master] = list;
            }
            list.Add(pickup);
            Plugin.Debug($"Équipement mis de côté pour {master.name} jusqu'à son retour : {pickup.pickupIndex}");
        }

        private static void OnBodyStart(CharacterBody body)
        {
            if (!NetworkServer.active || !body || body.isRemoteOp)
            {
                return;
            }
            CharacterMaster master = body.master;
            if (!master || !pending.TryGetValue(master, out List<UniquePickup> list))
            {
                return;
            }
            pending.Remove(master);
            foreach (UniquePickup pickup in list)
            {
                Grant.ToMaster(master, pickup, body.corePosition);
            }
        }
    }
}

# ShareItemsWithMoreFun

**Stop racing your friends to the chests.** Every chest opens once *per player*, and instead of a random item it drops a choice orb: pick 1 of 3 items of the same rarity.

## What it does

- **One chest, one opening per player.** When you buy a chest, it opens for you only. Your friends still see it closed and can buy it with their own money.
- **A choice instead of a roll.** The chest still rolls its rarity like vanilla, then drops a *Void Potential*-style orb with 3 items of that rarity. Category chests stay in their category, equipment barrels offer equipment, lunar pods offer lunar items.
- **Sale Star:** each extra drop becomes an extra orb, each with its own rarity.
- **Never miss a chest.** Once someone opens or pings a chest, it stays marked (like a ping, with no time limit) for every player who has not opened it yet.
- **Your loot is yours.** Your orb is invisible to the other players and only you can open it. The item you pick goes straight into your inventory.
- **Dead, not out.** While dead and piloting a drone (Alloyed Collective), you can still open your side of the chests with your own money.
- **Shrines of Chance for everyone.** Each player plays every Shrine of Chance on their side, with their own price and their own 2 successes. Each success drops a choice orb reserved to that player.
- **Boss loot for everyone.** The teleporter boss drops one choice orb per player (plus one per Shrine of the Mountain), green or yellow like vanilla, each reserved to its player.
- **Balance it your way.** Since everyone opens every chest, you can reduce the extra chests the game adds in multiplayer, down to the solo amount (`Chests.MultiplayerChestScaling`).
- **Loot in peace.** Once the teleporter is charged, difficulty stops rising until the next stage (the timer itself keeps going) and dead players come back near the teleporter, so everyone can finish opening their chests.
- **Artifact of Command:** vanilla Command essences are kept, and stay reserved to the player who opened the chest.

### Chests handled

Small, Large and Legendary chests, Cloaked chests, Damage / Healing / Utility chests (small and large), Rusty Lockboxes, Equipment Barrels, Lunar Pods, Void Cradles. Void Potentials and Encrusted Lockboxes are instanced per player (they already offer a choice). Multishops (white, green, equipment) are instanced too: one terminal per player per multishop.

Shrines of Chance are instanced per player too (see above).

Not handled (vanilla): Adaptive Chests, printers, scrappers, other shrines, Scavenger bags, the new Hallowed Concepts chests. You can add prefab names in the config.

## Multiplayer

**Every player needs the mod, same version.** A player without it is refused when joining. The host decides everything; the config that matters is the host's.

## Config

`BepInEx/config/com.shareitemswithmorefun.cfg` (or in game with Risk Of Options).

| Key | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | Master switch. |
| `General.InstancedChests` | `true` | One opening per player. Off: chests open once for everyone, but still drop choice orbs. |
| `Chests.RemoteOpCanOpenChests` | `true` | A dead player piloting a drone can still open the mod's chests on their side and their own orbs. |
| `Chests.PersistentChestMarkers` | `true` | A chest opened or pinged by someone stays marked for every player who has not opened it yet. |
| `Chests.InstancedMultiShops` | `true` | Multishops work like chests: one terminal per player per multishop, the multishop stays open for the others. |
| `Bosses.InstancedBossRewards` | `true` | The teleporter boss gives each player (1 + Shrines of the Mountain) choice orbs of their own. |
| `Shrines.InstancedChanceShrines` | `true` | Each player plays every Shrine of Chance on their side (own price, own 2 successes); each success drops a choice orb reserved to them. |
| `Chests.MultiplayerChestScaling` | `100` | % of the extra standard chests the game keeps with several players. 100 = vanilla (x1.5 chests at 2 players, x2 at 3, x2.5 at 4); 0 = as many as in solo. |
| `Stage.FreezeDifficultyAfterTeleporter` | `true` | Once the teleporter is charged, difficulty stops rising until the next stage. |
| `Stage.ReviveDeadPlayersAfterTeleporter` | `true` | Once the teleporter is charged, dead players come back near it. |
| `Orbs.ChoiceCount` | `3` | Items per orb (2 to 6). |
| `Orbs.ReserveForOpener` | `true` | Only the opener can open the orb. |
| `Orbs.HideOtherPlayersOrbs` | `true` | Other players' orbs are invisible. |
| `Orbs.GrantToInventory` | `true` | The chosen item goes straight into the inventory. |
| `Orbs.DisconnectGraceSeconds` | `180` | How long a disconnected player's orb stays reserved for them (0 = until the end of the stage). |
| `Chests.ChestNames` | see file | Prefab names of the handled chests. |
| `Debug.VerboseLogging` | `false` | Logs every decision. |

## Compatibility

Do not combine with mods that also change chest loot or loot sharing: ThreeChoiceChests, ItemShare, ShareSuite, InstancedLoot, Artifact of Potential. The mod warns in the log when it finds one of them.

---

## Français

**Fini la course aux coffres entre amis.** Chaque coffre s'ouvre une fois *par joueur* et, au lieu d'un item au hasard, lâche une orbe de choix : on prend 1 item parmi 3 de la même rareté.

- **Un coffre, une ouverture par joueur.** Le coffre s'ouvre pour toi seul ; les autres le voient toujours fermé et peuvent l'acheter avec leur argent.
- **Un choix au lieu d'un tirage.** La rareté est tirée comme en vanilla, puis le coffre lâche une orbe de type *Potentiel du Néant* avec 3 items de cette rareté. Les coffres de catégorie restent dans leur catégorie.
- **Étoile des soldes :** chaque drop bonus devient une orbe en plus.
- **Aucun coffre oublié.** Dès qu'un joueur ouvre ou pinge un coffre, il reste marqué (comme un ping, sans limite de temps) pour chaque joueur qui ne l'a pas encore ouvert.
- **Ton butin est à toi.** Ton orbe est invisible pour les autres et toi seul peux l'ouvrir. L'item choisi va directement dans ton inventaire.
- **Mort mais pas privé de butin.** Mort et aux commandes d'un drone (Alloyed Collective), tu peux encore ouvrir tes coffres avec ton argent.
- **Autels de la chance pour chacun.** Chaque joueur tente sa chance de son côté sur chaque autel, avec son propre prix et ses 2 réussites. Chaque réussite lâche une orbe à choix réservée à ce joueur.
- **Butin du boss pour chacun.** Le boss du téléporteur lâche une orbe à choix par joueur (plus une par autel de la montagne), verte ou jaune comme en vanilla, réservée à son joueur.
- **Équilibrage réglable.** Comme chacun ouvre chaque coffre, tu peux réduire les coffres que le jeu ajoute en multijoueur, jusqu'à la quantité du solo (`Chests.MultiplayerChestScaling`, 100 % par défaut = vanilla).
- **Finir l'étape tranquille.** Dès que le téléporteur est chargé, la difficulté ne monte plus jusqu'à l'étape suivante (le chrono, lui, continue) et les joueurs morts reviennent près du téléporteur.
- **Multijoueur :** tous les joueurs doivent avoir le mod, dans la même version. C'est la config de l'hôte qui compte.
- À ne pas combiner avec ThreeChoiceChests, ItemShare, ShareSuite, InstancedLoot ou Artifact of Potential.

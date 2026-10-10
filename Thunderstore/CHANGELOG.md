# Changelog

## 0.2.2

- New setting `Shrines.InstancedChanceShrines` (default on): every Shrine of Chance works per player. Each player has their own price (it only rises after their own attempts), their own successes (2, like vanilla) and their own 2 s wait between attempts. The shrine turns off only for the player who got all their successes; the others still see it active, with their own price.
- Each success drops a choice orb with 3 items of the rolled rarity, reserved to and only visible for that player. A failure gives nothing, like vanilla. The Chance Doll uses the inventory of the player trying.
- A dead player piloting a drone can play the shrines if `Chests.RemoteOpCanOpenChests` is on.
- The Captain's hacking beacon makes a shrine free for the Captain only (vanilla: free for everyone).
- New setting `Chests.PersistentChestMarkers` (default on): when a player opens or pings a chest, it stays marked for every player who has not opened it yet, like a ping with no time limit (no chat message, no sound). The marker disappears for a player once they open it, and stays for the others, whatever the number of players.

## 0.2.1

- New setting `Chests.MultiplayerChestScaling` (0 to 100 %, default 100 = vanilla): how many extra standard chests the game keeps with several players. At 0 %, there are as many chests as in solo, so each player gets about a solo run's loot (every player already opens every chest on their side). Only small, large, category and cloaked chests and equipment barrels are affected; shrines, drones, printers and rare chests are untouched.
- New setting `Chests.InstancedMultiShops` (default on): multishops (white, green, equipment) work like the chests. Each player can buy one terminal per multishop on their side, with their own money; the multishop stays open, with the same items, for the others. The bought item is reserved to the buyer.
- New setting `Bosses.InstancedBossRewards` (default on): the teleporter boss reward becomes choice orbs. Each player gets (1 + Shrines of the Mountain) orbs of their own, each with the rolled rarity (green, or a yellow boss item at 15%, including the killed boss's item), reserved to and only visible for that player.
- Fixed the package description: once the teleporter is charged, difficulty stops rising; the run timer itself keeps going.

## 0.2.0

- Once the stage's teleporter is charged, difficulty stops rising until the next stage starts (monster level, chest prices, difficulty bar). The run timer keeps going, so timed chests and timed unlocks stay vanilla. Config: `Stage.FreezeDifficultyAfterTeleporter`.
- At the same moment, dead players come back near the teleporter (3 s of immunity), including those piloting a drone, who get their survivor back. Config: `Stage.ReviveDeadPlayersAfterTeleporter`.

## 0.1.1

- A dead player piloting a drone (Alloyed Collective remote operation) can now open the mod's chests on their side, with their own money, and open their own orbs. Vanilla drones cannot open chests, so without this a dead player lost their share of every chest of the stage. Config: `Chests.RemoteOpCanOpenChests`.
- Equipment chosen while piloting a drone is kept aside and given back when the player's own body returns (drones cannot hold or pick up equipment), so the equipment they were holding is never lost.
- The host now tells clients which chests it instances and whether drones may open them, instead of each client guessing from its own config.

## 0.1.0

- First version.
- Chests open once per player; the chest stays closed and buyable for the others.
- Each drop becomes a choice orb with 3 items of the rolled rarity (drop table and category respected).
- Sale Star: one orb per bonus drop.
- Orbs are reserved to, and only visible for, the player who opened the chest; the chosen item goes straight into their inventory.
- Lunar Pods and Void Cradles give choice orbs too; Void Potentials and Encrusted Lockboxes are instanced per player.
- Late joiners receive the current state of chests and orbs.

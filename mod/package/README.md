# Megabonk Archipelago

Connects Megabonk to an [Archipelago](https://archipelago.gg) multiworld: the mod watches the game and sends your checks by itself. It goes with the `manual_megabonk_bbush` world.

## Setting it up

1. Start the game once through the mod manager and close it. This creates the settings file.
2. In the mod manager open the config editor and pick `bbush.megabonk.archipelago.cfg`. Set `Server` to your room's address and port, and `Slot` to your slot name. Nothing is sent while `Slot` is empty.
3. Start the game with the room running. Lines from the mod in `BepInEx/LogOutput.log` start with `[Info :MegabonkAP]`.

## Settings

| Setting | Default | Does |
|---|---|---|
| `Server` | `archipelago.gg:38281` | Address and port of the room |
| `Slot` | empty | Your slot name |
| `Password` | empty | The room's password, if any |
| `Strict` | on | A check only counts when you hold what the world's rules ask for |
| `DeathLink` | on | Follows the `death_link` option of your YAML. Off: never |
| `Probe` | on | Writes `BepInEx/MegabonkAP-probe.txt` at every start: the names in the game's code |

Megabonk has online leaderboards, and modded play can get scores flagged. Check the game's setting for uploading scores before playing modded.

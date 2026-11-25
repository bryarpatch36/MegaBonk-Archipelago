# Megabonk Archipelago

Connects Megabonk to an [Archipelago](https://archipelago.gg) multiworld: the mod watches the game and sends your checks by itself, locks what you do not hold yet, and shows a tracker on screen (`F8`, or clicking both sticks of a controller, changes its view). It goes with the `manual_megabonk_ap` world, on the same [Releases page](https://github.com/bryarpatch36/MegaBonk-Archipelago/releases).

This is an unofficial fan project. It is not affiliated with or endorsed by vedinad or the Archipelago project.

## Setting it up

1. Start the game once through the mod manager and close it. This creates the settings file.
2. In the mod manager open the config editor and pick `megabonk.archipelago.cfg`. Set `Server` to your room's address and port, and `Slot` to your slot name. Nothing is sent while `Slot` is empty.
3. Start the game with the room running. Lines from the mod in `BepInEx/LogOutput.log` start with `[Info :MegabonkAP]`.

## Settings

| Setting | Default | Does |
|---|---|---|
| `Server` | `archipelago.gg:38281` | Address and port of the room |
| `Slot` | empty | Your slot name |
| `Password` | empty | The room's password, if any |
| `Strict` | on | A check only counts when you hold what the world's rules ask for |
| `Lock` | on | With `Strict` on: a character, map or tier you do not hold cannot be started, and weapons and tomes you do not hold cannot be picked. A level-up with only locked offers can be skipped for free |
| `DeathLink` | on | Follows the `death_link` option of your YAML. Off: never |
| `Mode` | `Compact` | The on-screen tracker: `Compact`, `Full` or `Hidden` |
| `Key` | `F8` | Steps through the tracker's views. `None`: no key |
| `ControllerButtons` | `JoystickButton8+JoystickButton9` | Controller buttons that do the same when held together: by default both sticks clicked at once |
| `Position` | `MiddleLeft` | `TopLeft`, `MiddleLeft`, `BottomLeft`, `TopRight`, `MiddleRight` or `BottomRight` |
| `FontSize`, `Width` | 20, 520 | Size of the tracker, as on a 1080p screen |
| `Probe` | on | Writes `BepInEx/MegabonkAP-probe.txt` at every start: the names in the game's code |

Megabonk has online leaderboards, and modded play can get scores flagged. Check the game's setting for uploading scores before playing modded.

## Problems

Open an issue on the [Issues page](https://github.com/bryarpatch36/MegaBonk-Archipelago/issues) and attach `BepInEx/LogOutput.log`. The log names your server address and slot name: take out anything you would rather not post.

MIT License; see `LICENSE` and `THIRD-PARTY-NOTICES.md` in this package.

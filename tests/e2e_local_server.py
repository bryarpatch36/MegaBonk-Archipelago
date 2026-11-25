"""End-to-end check: host a generated seed on a local Archipelago server and play the slot.

Needs an Archipelago source checkout with this world's .apworld in its custom_worlds
folder, a Python that has Archipelago's core requirements, and a seed with a Megabonk slot.

    python e2e_local_server.py <archipelago source folder> <seed zip> [slot name]

No game and no Manual Client are involved: a small client connects as the slot the way the
Manual Client does, sends every check, and expects every item back and the goal accepted.
"""
import asyncio
import json
import os
import socket
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

import websockets

GAME = "Manual_Megabonk_AP"


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


async def play(port: int, slot: str) -> None:
    for _ in range(100):                      # the server needs a moment to come up
        try:
            ws = await websockets.connect(f"ws://127.0.0.1:{port}", max_size=None)
            break
        except OSError:
            await asyncio.sleep(0.2)
    else:
        raise AssertionError("the server never accepted a connection")

    waiting: list[dict] = []                  # the server sends several packets in one message

    async def receive(command: str) -> dict:
        while True:
            while waiting:
                packet = waiting.pop(0)
                if packet["cmd"] == "ConnectionRefused":
                    raise AssertionError(f"connection refused: {packet.get('errors')}")
                if packet["cmd"] == command:
                    return packet
            try:
                waiting.extend(json.loads(await asyncio.wait_for(ws.recv(), 20)))
            except asyncio.TimeoutError:
                raise AssertionError(f"the server sent no {command} packet within 20 seconds") from None

    async def send(packet: dict) -> None:
        await ws.send(json.dumps([packet]))

    try:
        room = await receive("RoomInfo")
        assert GAME in room["games"], room["games"]
        await send({
            "cmd": "Connect", "game": GAME, "name": slot, "password": None, "uuid": "megabonk-e2e",
            "version": {"major": 0, "minor": 6, "build": 8, "class": "Version"},
            "items_handling": 0b111, "tags": [], "slot_data": True,
        })
        connected = await receive("Connected")
        missing = connected["missing_locations"]
        assert missing and not connected["checked_locations"], "the slot is not fresh"
        start = await receive("ReceivedItems")
        assert len(start["items"]) == 4, f"{len(start['items'])} starting items, expected 4"

        await send({"cmd": "LocationChecks", "locations": missing})
        got = len(start["items"])
        while got < 4 + len(missing):         # a solo seed: every check holds one of our own items
            got += len((await receive("ReceivedItems"))["items"])
        assert got == 4 + len(missing), got

        await send({"cmd": "StatusUpdate", "status": 30})   # ClientStatus.CLIENT_GOAL
        while True:
            text = await receive("PrintJSON")
            if text.get("type") == "Goal":
                break
        print(f"ok    connected as {slot}, sent {len(missing)} checks, "
              f"received {got} items, goal accepted")
    finally:
        await ws.close()


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    source = Path(sys.argv[1]).resolve()
    seed = Path(sys.argv[2]).resolve()
    slot = sys.argv[3] if len(sys.argv) > 3 else "Bonker"

    with tempfile.TemporaryDirectory() as work:
        with zipfile.ZipFile(seed) as zf:
            name = next(n for n in zf.namelist() if n.endswith(".archipelago"))
            zf.extract(name, work)
        port = free_port()
        server = subprocess.Popen(
            [sys.executable, "MultiServer.py", "--port", str(port), "--host", "127.0.0.1",
             str(Path(work) / name)],
            cwd=source, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"),
        )
        try:
            asyncio.run(play(port, slot))
        except AssertionError as problem:
            print(f"FAIL  {problem}")
            return 1
        finally:
            server.terminate()
            server.wait(10)
    return 0


if __name__ == "__main__":
    sys.exit(main())

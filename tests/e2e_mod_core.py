"""End-to-end check of the mod's core: its own connection and check logic against a real server.

Generates a seed from Bonker.yaml, hosts it on a local Archipelago server, and runs
mod/tests/MegabonkAP.E2E against it. No game is involved: the events a run would produce are
given by hand. Needs the .NET SDK, an Archipelago source checkout with this world's .apworld in
its custom_worlds folder, and a Python that has Archipelago's core requirements.

    python e2e_mod_core.py <archipelago source folder>
"""
import os
import socket
import subprocess
import sys
import tempfile
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
E2E = ROOT / "mod" / "tests" / "MegabonkAP.E2E"


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    source = Path(sys.argv[1]).resolve()
    env = dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1", DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")

    build = subprocess.run(["dotnet", "build", str(E2E), "-c", "Release", "-v", "q"],
                           env=env, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-3000:])
        print("FAIL  the end-to-end program did not build")
        return 1

    with tempfile.TemporaryDirectory() as work:
        players, out = Path(work) / "players", Path(work) / "out"
        players.mkdir()
        out.mkdir()
        (players / "Bonker.yaml").write_text((ROOT / "Bonker.yaml").read_text(encoding="utf-8"), encoding="utf-8")
        generate = subprocess.run(
            [sys.executable, "Generate.py", "--player_files_path", str(players), "--outputpath", str(out)],
            cwd=source, env=env, capture_output=True, text=True)
        if generate.returncode != 0:
            print(generate.stderr[-3000:])
            print("FAIL  generation failed")
            return 1
        with zipfile.ZipFile(next(out.glob("AP_*.zip"))) as zf:
            name = next(n for n in zf.namelist() if n.endswith(".archipelago"))
            zf.extract(name, work)

        port = free_port()
        log_path = Path(work) / "server.log"
        with open(log_path, "w", encoding="utf-8") as server_log:
            server = subprocess.Popen(
                [sys.executable, "MultiServer.py", "--port", str(port), "--host", "127.0.0.1", str(Path(work) / name)],
                cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=server_log, stderr=subprocess.STDOUT)
            try:
                for _ in range(100):                    # wait for the server to listen
                    with socket.socket() as probe:
                        if probe.connect_ex(("127.0.0.1", port)) == 0:
                            break
                    time.sleep(0.2)
                run = subprocess.run(["dotnet", "run", "--project", str(E2E), "-c", "Release", "--no-build",
                                      "--", f"127.0.0.1:{port}", "Bonker"], env=env)
                time.sleep(1)
            finally:
                server.terminate()
                server.wait(10)

        said = log_path.read_text(encoding="utf-8", errors="replace")
        goal = "has completed their goal" in said or "completed their goal" in said
        print(("ok    " if goal else "FAIL  ") + "the server recorded the goal")
        if not goal:
            print(said[-2000:])
        return 0 if run.returncode == 0 and goal else 1


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Check JSONL management and immutable fixture builds without any game install.

Run after `setup`: python scripts/management-smoke.py <eu4indexer>.
Uses a temporary home and the calling home's downloaded configuration rules.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    executable = str(Path(sys.argv[1]).resolve())
    root = Path(__file__).resolve().parent.parent
    rules = Path(os.environ.get("EU4INDEXER_HOME", str(Path.home() / ".eu4indexer"))) / "config"
    with tempfile.TemporaryDirectory(prefix="management-smoke-") as home:
        env = {**os.environ, "EU4INDEXER_HOME": home}

        def request(payload, success=True):
            process = subprocess.run([executable, "manage"], input=json.dumps(payload),
                                     capture_output=True, text=True, env=env, timeout=120)
            events = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
            assert events and all(event["apiVersion"] == 1 for event in events), process.stderr
            assert sum(event["type"] in ("result", "error") for event in events) == 1
            assert events[-1]["type"] == ("result" if success else "error"), events[-1]
            assert (process.returncode == 0) == success
            return events[-1]["data"]

        assert request({"operation": "capabilities"})["apiVersion"] == 1
        built = {}
        for game in ("eu4", "hoi4"):
            config = os.environ.get(game.upper() + "_CONFIG_DIR", str(rules / game))
            result = request({"operation": "index", "game": game, "name": game,
                              "gameDir": str(root / "Eu4Indexer.Tests" / "fixtures" / game / "example-game"),
                              "configDir": config, "languages": ["english"]})
            built[game] = result["index"]["path"]
            assert result["report"]["entityCount"] > 0
            assert result["report"]["foreignKeyViolations"] == 0
        before = {db["name"]: db for db in request({"operation": "list"})["databases"]}
        for game, path in built.items():
            assert before[game]["path"] == path, (before[game]["path"], path)
        refreshed = request({"operation": "refresh", "name": "eu4"})["index"]
        assert refreshed["path"] != before["eu4"]["path"]
        assert Path(before["eu4"]["path"]).is_file()
        request({"operation": "index", "name": "eu4", "gameDir": str(Path(home) / "missing")}, success=False)
        after = {db["name"]: db for db in request({"operation": "list"})["databases"]}
        assert after["eu4"]["path"] == refreshed["path"]
        assert request({"operation": "inspect", "name": "hoi4"})["meta"]["game_id"] == "hoi4"
        copied = request({"operation": "register", "name": "copy", "path": before["eu4"]["path"]})
        assert copied["path"] == before["eu4"]["path"]
        request({"operation": "remove", "name": "copy"})
        assert Path(before["eu4"]["path"]).is_file()
    print("management smoke passed (EU4 + HOI4)")


if __name__ == "__main__":
    main()

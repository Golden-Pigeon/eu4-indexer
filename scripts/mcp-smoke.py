#!/usr/bin/env python3
"""Drive the eu4indexer MCP server over stdio and assert the core handshake and
multi-database tools work. Used by the CI smoke matrix and runnable locally.

Usage: python3 scripts/mcp-smoke.py <path-to-eu4indexer-exe> [expect-db-name]

Exits non-zero with a message on any failure.
"""
import json
import queue
import subprocess
import sys
import threading


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: mcp-smoke.py <exe> [expect-db-name]", file=sys.stderr)
        return 2

    exe = sys.argv[1]
    expect = sys.argv[2] if len(sys.argv) > 2 else None

    proc = subprocess.Popen(
        [exe, "serve"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        text=True,
        bufsize=1,
    )

    responses: "queue.Queue[str]" = queue.Queue()

    def reader():
        assert proc.stdout is not None
        for line in proc.stdout:
            responses.put(line)

    threading.Thread(target=reader, daemon=True).start()

    def send(obj):
        assert proc.stdin is not None
        proc.stdin.write(json.dumps(obj) + "\n")
        proc.stdin.flush()

    def recv(timeout=30):
        return json.loads(responses.get(timeout=timeout))

    def call(rid, name, arguments=None):
        send({"jsonrpc": "2.0", "id": rid, "method": "tools/call",
              "params": {"name": name, "arguments": arguments or {}}})
        return recv()["result"]["content"][0]["text"]

    try:
        send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "smoke", "version": "0"}}})
        info = recv()["result"]["serverInfo"]
        print("initialize ok:", info)

        send({"jsonrpc": "2.0", "method": "notifications/initialized"})

        dbs = json.loads(call(2, "list_databases"))
        print("list_databases:", dbs)
        assert len(dbs) >= 1, "expected at least one registered database"

        name = expect or dbs[0]["name"]
        sel = call(3, "select_database", {"name": name})
        print("select_database:", sel)
        assert sel.startswith("Selected"), f"select_database failed: {sel}"

        sources = json.loads(call(4, "list_sources"))
        print("list_sources:", sources)
        assert len(sources) >= 1, "expected at least one source"

        print("MCP smoke: OK")
        return 0
    except Exception as ex:  # noqa: BLE001
        print(f"MCP smoke FAILED: {ex}", file=sys.stderr)
        return 1
    finally:
        try:
            if proc.stdin:
                proc.stdin.close()
        except Exception:
            pass
        proc.terminate()


if __name__ == "__main__":
    sys.exit(main())

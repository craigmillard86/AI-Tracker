#!/usr/bin/env python3
"""Session-close cost hook (CLAUDE.md 12).

Invoked by the Claude Code SessionEnd hook with the hook payload on stdin.
Appends one row per session to .claude/cost-log.csv, tagged to the story via
the current git branch (HAP-<n> extracted; otherwise 'untagged').
The CSV is gitignored and never hand-edited. This script must never block
session end: every failure path exits 0 silently.
"""
import csv
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timezone


def main() -> None:
    payload = json.load(sys.stdin)
    cwd = payload.get("cwd") or os.getcwd()
    session_id = payload.get("session_id", "unknown")
    transcript = payload.get("transcript_path", "")

    try:
        branch = subprocess.run(
            ["git", "-C", cwd, "rev-parse", "--abbrev-ref", "HEAD"],
            capture_output=True, text=True, timeout=10,
        ).stdout.strip() or "unknown"
    except Exception:
        branch = "unknown"
    m = re.match(r"(HAP-\d+)", branch)
    story = m.group(1) if m else "untagged"

    tokens_in = tokens_out = 0
    cost_usd = 0.0
    if transcript and os.path.exists(transcript):
        with open(transcript, encoding="utf-8") as fh:
            for line in fh:
                try:
                    rec = json.loads(line)
                except json.JSONDecodeError:
                    continue
                usage = (rec.get("message") or {}).get("usage") or {}
                tokens_in += usage.get("input_tokens", 0) or 0
                tokens_in += usage.get("cache_creation_input_tokens", 0) or 0
                tokens_out += usage.get("output_tokens", 0) or 0
                cost_usd += rec.get("costUSD", 0) or 0

    log_path = os.path.join(cwd, ".claude", "cost-log.csv")
    os.makedirs(os.path.dirname(log_path), exist_ok=True)
    new_file = not os.path.exists(log_path)
    with open(log_path, "a", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        if new_file:
            w.writerow(["timestamp", "session_id", "branch", "story",
                        "input_tokens", "output_tokens", "cost_usd"])
        w.writerow([datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                    session_id, branch, story, tokens_in, tokens_out,
                    f"{cost_usd:.4f}"])


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass  # never block session end
    sys.exit(0)

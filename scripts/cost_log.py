#!/usr/bin/env python3
"""Cost hook (CLAUDE.md 12, DR-0007).

Wired to two Claude Code hook events:
  - SubagentStop: one row per teammate/subagent completion, tagged to that
    agent's story (HAP-<n>, derived from its worktree branch / transcript).
  - SessionEnd:   one row for the session lead (main loop), story 'session-lead'.

The hook payload arrives on stdin. Tokens are summed from the stopped
context's transcript; the story is tagged via the HAP-<n> branch key. The CSV
is gitignored and never hand-edited. This script must never block a stop or
session end: every failure path exits 0 silently.
"""
import csv
import json
import os
import re
import subprocess
import sys
from collections import Counter
from datetime import datetime, timezone

# Full worktree-branch form (HAP-7-fr-002-cycle-management) — the most specific
# per-agent signal; and the bare story key (HAP-7) as a fallback.
HAP_BRANCH = re.compile(r"HAP-\d+-fr-[a-z0-9][a-z0-9-]*")
HAP_KEY = re.compile(r"HAP-\d+")


def git_branch(cwd: str) -> str:
    try:
        return subprocess.run(
            ["git", "-C", cwd, "rev-parse", "--abbrev-ref", "HEAD"],
            capture_output=True, text=True, timeout=10,
        ).stdout.strip() or "unknown"
    except Exception:
        return "unknown"


def scan_transcript(path: str):
    """Sum usage from a transcript and collect story signals from its text."""
    tokens_in = tokens_out = 0
    cost_usd = 0.0
    branches: Counter = Counter()
    keys: Counter = Counter()
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as fh:
            for line in fh:
                for b in HAP_BRANCH.findall(line):
                    branches[b] += 1
                for k in HAP_KEY.findall(line):
                    keys[k] += 1
                try:
                    rec = json.loads(line)
                except json.JSONDecodeError:
                    continue
                usage = (rec.get("message") or {}).get("usage") or {}
                tokens_in += usage.get("input_tokens", 0) or 0
                tokens_in += usage.get("cache_creation_input_tokens", 0) or 0
                tokens_out += usage.get("output_tokens", 0) or 0
                cost_usd += rec.get("costUSD", 0) or 0
    return tokens_in, tokens_out, cost_usd, branches, keys


def main() -> None:
    payload = json.load(sys.stdin)
    cwd = payload.get("cwd") or os.getcwd()
    session_id = payload.get("session_id", "unknown")
    event = payload.get("hook_event_name", "")
    transcript = payload.get("transcript_path", "")

    tokens_in, tokens_out, cost_usd, branches, keys = scan_transcript(transcript)

    if event == "SessionEnd":
        # The session lead runs on main; its spend is the orchestration cost,
        # not one story's. Tag it 'session-lead' so telemetry can separate it.
        branch = git_branch(cwd)
        story = "session-lead"
    else:
        # Agent completion (SubagentStop). Prefer the worktree-branch pattern
        # dominating the agent's transcript, else the dominant bare story key,
        # else the cwd branch, else untagged.
        if branches:
            branch = branches.most_common(1)[0][0]
            story = HAP_KEY.search(branch).group(0)
        elif keys:
            story = keys.most_common(1)[0][0]
            branch = story
        else:
            branch = git_branch(cwd)
            m = HAP_KEY.match(branch)
            story = m.group(0) if m else "untagged"

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
        pass  # never block a stop or session end
    sys.exit(0)

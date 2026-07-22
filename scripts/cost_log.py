#!/usr/bin/env python3
"""Cost hook (CLAUDE.md §12, DR-0007 as revised by DR-0008).

Wired to a SINGLE Claude Code hook event:
  - SessionEnd: a batch pass over the session's per-agent transcripts.
    Emits one row per story (summing every subagent whose transcript is
    dominated by that HAP-<n> key) plus one `session-lead` row for the
    main orchestration loop. Written once, at session end.

Why a SessionEnd batch and not a per-`SubagentStop` write (DR-0008):
the `SubagentStop` hook only ever receives the *shared main* transcript and
a main-repo `cwd`, so it cannot see a single agent's isolated spend — the
original per-stop design (DR-0007) re-summed the whole session on every stop,
producing cumulative duplicate rows, all mis-tagged to one "dominant" story,
with zero cost. Per-agent attribution IS available at rest: Claude Code writes
each subagent's transcript to
`<project>/<session_id>/subagents/agent-<id>.jsonl`. This hook reads those at
session end, sums usage per agent, tags each by the HAP-<n> key dominating its
own transcript, and prices tokens from the maintained rate table below.

The CSV is gitignored, written only at SessionEnd, and never hand-edited. This
hook must never block session end: every failure path exits 0 silently.
The 7-column schema is unchanged so `scripts/telemetry.sh` keeps joining on
`story` (column 4).
"""
import csv
import json
import os
import re
import sys
from collections import defaultdict

# Full worktree-branch form (HAP-7-fr-002-cycle-management) and the bare story
# key (HAP-7). A subagent's transcript is tagged by the story key that
# dominates it; the branch column takes the dominant full branch when present.
HAP_BRANCH = re.compile(r"HAP-\d+-fr-[a-z0-9][a-z0-9-]*")
HAP_KEY = re.compile(r"HAP-\d+")

# Maintained rate table — $/MTok by (input, output, cache_write, cache_read),
# matched by model-id substring. These are ESTIMATES for internal telemetry
# (calibration data, never billed); update when published rates change. Cost
# is priced per usage record by that record's own model, so a mixed-model
# transcript (the lead runs fable + opus) is costed correctly.
MODEL_RATES = {
    "opus":   (15.0, 75.0, 18.75, 1.50),
    "sonnet": (3.0,  15.0,  3.75, 0.30),
    "haiku":  (0.80,  4.0,  1.00, 0.08),
    "fable":  (3.0,  15.0,  3.75, 0.30),  # estimate — no public rate; sonnet-tier
}
_DEFAULT_RATE = MODEL_RATES["sonnet"]


def rate_for(model: str):
    m = (model or "").lower()
    for key, rate in MODEL_RATES.items():
        if key in m:
            return rate
    return _DEFAULT_RATE


def scan_transcript(path: str):
    """Sum usage from one transcript and collect its story/branch signals.

    Returns (tokens_in, tokens_out, cost_usd, branches, keys). tokens_in is
    input + cache-creation (new context); cache-read is excluded from the token
    columns but PRICED into cost (it is real spend)."""
    tokens_in = tokens_out = 0
    cost = 0.0
    branches: defaultdict = defaultdict(int)
    keys: defaultdict = defaultdict(int)
    if not (path and os.path.exists(path)):
        return 0, 0, 0.0, branches, keys
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
            msg = rec.get("message") or {}
            usage = msg.get("usage") or {}
            if not usage:
                continue
            ti = usage.get("input_tokens", 0) or 0
            cw = usage.get("cache_creation_input_tokens", 0) or 0
            cr = usage.get("cache_read_input_tokens", 0) or 0
            to = usage.get("output_tokens", 0) or 0
            r_in, r_out, r_cw, r_cr = rate_for(msg.get("model"))
            cost += (ti * r_in + cw * r_cw + cr * r_cr + to * r_out) / 1_000_000
            tokens_in += ti + cw
            tokens_out += to
    return tokens_in, tokens_out, cost, branches, keys


def story_of(branches, keys):
    """Story = dominant bare HAP key; branch = dominant full branch or the key."""
    if keys:
        story = max(keys.items(), key=lambda kv: kv[1])[0]
    else:
        return None, None
    if branches:
        branch = max(branches.items(), key=lambda kv: kv[1])[0]
    else:
        branch = story
    return branch, story


def subagents_dir(transcript: str, session_id: str) -> str:
    """`<project>/<session_id>/subagents` — derived from the main transcript."""
    if transcript:
        base = os.path.dirname(transcript)
        for cand in (
            os.path.join(base, session_id, "subagents"),
            os.path.join(transcript[:-6] if transcript.endswith(".jsonl") else transcript, "subagents"),
        ):
            if os.path.isdir(cand):
                return cand
    return ""


def main() -> None:
    payload = json.load(sys.stdin)
    if payload.get("hook_event_name", "") != "SessionEnd":
        return  # DR-0008: attribution is a SessionEnd batch; ignore all else.

    cwd = payload.get("cwd") or os.getcwd()
    session_id = payload.get("session_id", "unknown")
    transcript = payload.get("transcript_path", "")
    ts = __import__("datetime").datetime.now(
        __import__("datetime").timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    rows = []  # (branch, story, tin, tout, cost)

    # Per-story rows: sum each subagent transcript, group by its dominant story.
    agg = defaultdict(lambda: [None, 0, 0, 0.0])  # story -> [branch, tin, tout, cost]
    sdir = subagents_dir(transcript, session_id)
    if sdir:
        for name in os.listdir(sdir):
            if not (name.startswith("agent-") and name.endswith(".jsonl")):
                continue
            tin, tout, cost, branches, keys = scan_transcript(os.path.join(sdir, name))
            if tin == 0 and tout == 0 and cost == 0.0:
                continue
            branch, story = story_of(branches, keys)
            story = story or "untagged"
            cell = agg[story]
            if cell[0] is None:
                cell[0] = branch or story
            cell[1] += tin
            cell[2] += tout
            cell[3] += cost
    for story, (branch, tin, tout, cost) in sorted(agg.items()):
        rows.append((branch, story, tin, tout, cost))

    # Session-lead row: the main orchestration loop's own transcript.
    tin, tout, cost, _b, _k = scan_transcript(transcript)
    rows.append(("main", "session-lead", tin, tout, cost))

    log_path = os.path.join(cwd, ".claude", "cost-log.csv")
    os.makedirs(os.path.dirname(log_path), exist_ok=True)
    new_file = not os.path.exists(log_path)
    with open(log_path, "a", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        if new_file:
            w.writerow(["timestamp", "session_id", "branch", "story",
                        "input_tokens", "output_tokens", "cost_usd"])
        for branch, story, tin, tout, cost in rows:
            w.writerow([ts, session_id, branch, story, tin, tout, f"{cost:.4f}"])


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass  # never block session end
    sys.exit(0)

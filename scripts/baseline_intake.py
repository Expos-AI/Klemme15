#!/usr/bin/env python3
# Klemme15: open-source BMW diagnostics
# Copyright (C) 2026 Kindric Castro
# SPDX-License-Identifier: GPL-3.0-or-later
"""
Community baseline pipeline.

    intake: read a "Share a healthy baseline" issue, download the attached contribution file, check it, and store it
            in data/baselines/contrib/<ENGINE>/issue-<n>.csv. Writes a Markdown report (for the issue comment and the
            pull request) to --report.
    build:  turn every stored contribution into one community profile per engine in src/Klemme15.Data/db/baselines.json,
            using the same binning and percentiles as Klemme15 (BaselineAligner in src/Klemme15.Core/Baselines.cs).

Everything in the issue is untrusted: only attachment links on github.com are followed, downloads are size-capped,
the file must be plain numbers in known columns, and nothing in it is ever executed. Standard library only.
"""
import argparse
import csv
import io
import json
import math
import os
import re
import sys
import urllib.request

MARKER = "# klemme15-baseline-contribution v1"
COLUMNS = ["rpm", "load", "coolant", "maf", "stft1", "ltft1", "stft2", "ltft2", "iat", "throttle"]
ENGINE = re.compile(r"^[A-Z][0-9]{2}[A-Z0-9]{0,6}$")
ATTACHMENT = re.compile(r"https://github\.com/user-attachments/files/\d+/[A-Za-z0-9._-]+\.csv")
MAX_BYTES = 5_000_000
MAX_ROWS = 100_000
MIN_WARM, MIN_IDLE, MIN_TOP_RPM, MIN_BINS = 120, 30, 3000, 6   # the same rules as BaselineContribution.Check
BIN = 250.0
MIN_SAMPLES = 20


class Problem(Exception):
    pass


# ----------------------------------------------------------------------------------------------- reading and checking

def parse(text):
    """Returns (meta, columns, rows). Raises Problem with a sentence an owner can act on."""
    lines = text.replace("\r\n", "\n").split("\n")
    if not lines or lines[0].strip() != MARKER:
        raise Problem("This isn't a Klemme15 baseline file. Make it with **Share as a healthy baseline** on the Analyse page.")
    meta, body = {}, []
    for line in lines[1:]:
        if line.startswith("#"):
            key, _, value = line[1:].partition(":")
            meta[key.strip().lower()] = value.strip()[:40]
        elif line.strip():
            body.append(line)
    if not body:
        raise Problem("The file has no readings.")
    reader = csv.reader(io.StringIO("\n".join(body)))
    header = next(reader)
    if not header or header[0] != "time" or len(header) < 3:
        raise Problem("The file's columns aren't what Klemme15 writes.")
    cols = header[1:]
    if any(c not in COLUMNS for c in cols) or len(set(cols)) != len(cols):
        raise Problem("The file has columns Klemme15 doesn't write: " + ", ".join(c for c in cols if c not in COLUMNS))
    rows = []
    for n, cells in enumerate(reader):
        if n >= MAX_ROWS:
            raise Problem(f"The file has more than {MAX_ROWS} readings. Share a shorter drive (10 to 20 minutes is plenty).")
        if len(cells) != len(header):
            raise Problem(f"Reading {n + 1} has {len(cells)} values instead of {len(header)}.")
        try:
            t = float(cells[0])
            values = {c: (float(v) if v.strip() else None) for c, v in zip(cols, cells[1:])}
        except ValueError:
            raise Problem(f"Reading {n + 1} contains something that isn't a number.")
        if not all(math.isfinite(x) for x in [t, *[v for v in values.values() if v is not None]]):
            raise Problem(f"Reading {n + 1} contains a value that isn't a real number.")
        rows.append((t, values))
    engine = meta.get("engine", "").upper()
    if not ENGINE.match(engine):
        raise Problem("The file doesn't name the engine (for example N52B30).")
    meta["engine"] = engine
    return meta, cols, rows


def total_trim(v, bank="1"):
    s, l = v.get("stft" + bank), v.get("ltft" + bank)
    return None if s is None or l is None else s + l


def check(cols, rows):
    """What's missing for a useful baseline (the same rules as Klemme15 checks before sharing)."""
    problems = []
    for need in ("rpm", "coolant"):
        if need not in cols:
            problems.append(f"The log has no {need}.")
    if not ({"stft1", "ltft1"} <= set(cols) or "maf" in cols):
        problems.append("The log needs fuel trims (short- and long-term, bank 1) or air mass.")
    if problems:
        return problems
    duration = rows[-1][0] - rows[0][0] if rows else 0
    if duration < 60:
        problems.append(f"The log is only {duration:.0f} s long; at least a minute is needed.")
    warm = [v["rpm"] for _, v in rows if (v.get("rpm") or 0) > 300 and (v.get("coolant") or 0) > 70]
    if len(warm) < MIN_WARM:
        problems.append(f"Only {len(warm)} readings with a warm engine; at least {MIN_WARM} are needed.")
    else:
        if sum(1 for r in warm if r < 1000) < MIN_IDLE:
            problems.append("Not enough warm idle in the log.")
        if max(warm) < MIN_TOP_RPM:
            problems.append(f"The log only reaches {max(warm):.0f} rpm warm; it needs to pass {MIN_TOP_RPM} rpm.")
        if len({bin_of(r) for r in warm}) < MIN_BINS:
            problems.append(f"The log covers fewer than {MIN_BINS} engine speed steps of 250 rpm.")
    return problems


def coverage(rows):
    warm = [v["rpm"] for _, v in rows if (v.get("rpm") or 0) > 300 and (v.get("coolant") or 0) > 70]
    return (min(warm), max(warm), len({bin_of(r) for r in warm})) if warm else (0, 0, 0)


# ----------------------------------------------------------------------------------------------- building profiles

def bin_of(rpm):
    # Python's round() rounds halves to even, like .NET's Math.Round: bins match Klemme15's exactly.
    return round(rpm / BIN) * BIN


def pct(sorted_values, q):
    if len(sorted_values) == 1:
        return sorted_values[0]
    pos = q * (len(sorted_values) - 1)
    lo = math.floor(pos)
    hi = min(lo + 1, len(sorted_values) - 1)
    return sorted_values[lo] + (sorted_values[hi] - sorted_values[lo]) * (pos - lo)


CURVES = [
    # name, y, filter text (Klemme15's expression language), row filter, filter required
    ("Total trim bank 1 across engine speed", "stft1+ltft1", "coolant > 70",
     lambda v: (v.get("coolant") or 0) > 70, False),
    ("Air mass at idle and no load", "maf", "coolant > 70 and load < 40",
     lambda v: (v.get("coolant") or 0) > 70 and v.get("load") is not None and v["load"] < 40, True),
    ("Air mass at full throttle", "maf", "coolant > 70 and load > 80",
     lambda v: (v.get("coolant") or 0) > 70 and v.get("load") is not None and v["load"] > 80, True),
]


def build_curve(logs, name, y, filter_text, keep, required):
    groups = {}
    for rows in logs:
        for _, v in rows:
            rpm = v.get("rpm")
            value = total_trim(v) if y == "stft1+ltft1" else v.get(y)
            if rpm is None or value is None or not keep(v):
                continue
            groups.setdefault(bin_of(rpm), []).append(value)
    points = []
    for x in sorted(groups):
        s = sorted(groups[x])
        if len(s) >= MIN_SAMPLES:
            points.append({"x": x, "p10": round(pct(s, 0.10), 2), "median": round(pct(s, 0.50), 2),
                           "p90": round(pct(s, 0.90), 2), "samples": len(s)})
    if len(points) < 2:
        return None
    curve = {"name": name, "y": y, "x": "rpm", "filter": filter_text, "binWidth": BIN, "points": points}
    if required:
        curve["filterRequired"] = True
    return curve


def build(contrib_dir, db_path):
    profiles = []
    for engine in sorted(os.listdir(contrib_dir)) if os.path.isdir(contrib_dir) else []:
        folder = os.path.join(contrib_dir, engine)
        logs, contributors = [], []
        for f in sorted(os.listdir(folder)):
            if not f.endswith(".csv"):
                continue
            with open(os.path.join(folder, f), encoding="utf-8") as fh:
                meta, _, rows = parse(fh.read())
            logs.append(rows)
            if meta.get("contributor") and meta["contributor"] not in contributors:
                contributors.append(meta["contributor"])
        curves = [c for c in (build_curve(logs, *spec) for spec in CURVES) if c]
        if not curves:
            continue
        cars = len(logs)
        profiles.append({
            "id": f"community-{engine.lower()}",
            "title": f"{engine} community baseline",
            "engines": [engine],
            "conditions": f"Built from {cars} owner{'s' if cars != 1 else ''}' logs of healthy cars, warm engine only. "
                          "Shared through Klemme15 and checked by the intake pipeline.",
            "cars": cars,
            "contributors": contributors,
            "sources": [f"data/baselines/contrib/{engine}"],
            "curves": curves,
        })
    with open(db_path, encoding="utf-8") as fh:
        existing = json.load(fh)
    kept = [p for p in existing if not str(p.get("id", "")).startswith("community-")]
    with open(db_path, "w", encoding="utf-8") as fh:
        json.dump(kept + profiles, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    return profiles


# ----------------------------------------------------------------------------------------------- intake from an issue

class _OnlyHttps(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not newurl.startswith("https://"):
            raise Problem("The attachment link redirected somewhere unexpected.")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def download(url):
    opener = urllib.request.build_opener(_OnlyHttps)
    req = urllib.request.Request(url, headers={"User-Agent": "klemme15-baseline-intake"})
    with opener.open(req, timeout=30) as resp:
        data = resp.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        raise Problem("The attached file is larger than 5 MB. Share a shorter drive.")
    return data.decode("utf-8", errors="strict")


def intake(body, issue, login, out_dir, report_path):
    lines = [f"### Baseline check for #{issue}", ""]
    ok = False
    try:
        links = ATTACHMENT.findall(body or "")
        if not links:
            raise Problem("No baseline file is attached. Drag the `.csv` that Klemme15 saved into the issue.")
        meta, cols, rows = parse(download(links[0]))
        problems = check(cols, rows)
        if problems:
            raise Problem(" ".join(problems))
        low, high, bins = coverage(rows)
        engine = meta["engine"]
        os.makedirs(os.path.join(out_dir, engine), exist_ok=True)
        path = os.path.join(out_dir, engine, f"issue-{int(issue)}.csv")
        login = re.sub(r"[^A-Za-z0-9-]", "", login)[:39]
        header = [MARKER, f"# engine: {engine}"] + [f"# {k}: {meta[k]}" for k in ("chassis", "model", "klemme15") if k in meta]
        header.append(f"# contributor: {login}")
        with open(path, "w", encoding="utf-8", newline="") as fh:
            fh.write("\n".join(header) + "\n")
            w = csv.writer(fh, lineterminator="\n")
            w.writerow(["time"] + cols)
            for t, v in rows:
                w.writerow([f"{t:g}"] + ["" if v[c] is None else f"{v[c]:g}" for c in cols])
        lines += [f"✅ **Accepted:** {engine}, {len(rows)} readings, warm from {low:.0f} to {high:.0f} rpm ({bins} steps of 250 rpm).",
                  "", "A pull request with the updated baseline is being prepared. Thank you!"]
        ok = True
        with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as out:
            out.write(f"engine={engine}\npath={path}\n")
    except Problem as p:
        lines += [f"❌ **Not accepted yet:** {p}", "",
                  "Fix it in Klemme15 (Analyse page → **Share as a healthy baseline**), then edit this issue and attach the new file: the check runs again."]
    except (OSError, UnicodeDecodeError) as e:
        lines += [f"❌ **Couldn't read the attachment** ({type(e).__name__}). Please attach the file again."]
    with open(report_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    return ok


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("intake")
    a.add_argument("--body-file", required=True)
    a.add_argument("--issue", required=True, type=int)
    a.add_argument("--login", required=True)
    a.add_argument("--out-dir", default="data/baselines/contrib")
    a.add_argument("--report", required=True)
    b = sub.add_parser("build")
    b.add_argument("--contrib", default="data/baselines/contrib")
    b.add_argument("--db", default="src/Klemme15.Data/db/baselines.json")
    c = sub.add_parser("check")
    c.add_argument("file")
    args = ap.parse_args()
    if args.cmd == "intake":
        with open(args.body_file, encoding="utf-8") as fh:
            body = fh.read()
        sys.exit(0 if intake(body, args.issue, args.login, args.out_dir, args.report) else 1)
    if args.cmd == "build":
        for p in build(args.contrib, args.db):
            print(f"{p['id']}: {p['cars']} car(s), {len(p['curves'])} curve(s)")
    if args.cmd == "check":
        with open(args.file, encoding="utf-8") as fh:
            _, cols, rows = parse(fh.read())
        problems = check(cols, rows)
        print("\n".join(problems) or "OK")
        sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()

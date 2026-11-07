#!/usr/bin/env python3
# Klemme15: open-source BMW diagnostics
# Copyright (C) 2026 Kindric Castro
# SPDX-License-Identifier: GPL-3.0-or-later
"""
Turns the raw scene recordings (Program.cs) into vertical 1080x1920 clips for TikTok, Reels and Shorts:
a camera that eases between the parts of the screen that matter, a cursor that travels to each button and
clicks, and a caption bar. Writes, per scene, a captioned clip and a clean one (same picture, no text), a
still of each, a landscape copy of the raw recording for your own edits, and reel.mp4: every captioned
scene in a row.

    python3 compose.py <raw dir> <out dir>
"""
import json
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

RAW, OUT = sys.argv[1], sys.argv[2]
HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, "..", "..", "src", "Klemme15.App", "Assets", "Fonts")

W, H = 1080, 1920
APP_Y, APP_W, APP_H = 290, 1080, 1350          # the app area: 4:5, between the caption bars
BG = (18, 26, 35)                               # the app's dark canvas, so letterboxing is invisible
INK, MUTED, LIT = (220, 227, 234), (135, 150, 165), (140, 203, 234)
EASE = 20                                       # frames for a camera move (2/3 s)
TRAVEL = 20                                     # frames for the cursor to reach a button
MIN_W = 760                                     # never zoom closer than this many source pixels across


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def lerp(a, b, k):
    return tuple(x + (y - x) * k for x, y in zip(a, b))


def fit(r, fw, fh):
    """A crop around r: 4:5 where it can be, never closer than MIN_W, inside the frame."""
    x, y, w, h = r
    if w < MIN_W:
        x -= (MIN_W - w) / 2
        w = MIN_W
    aspect = APP_W / APP_H
    if w / h < aspect:
        nw = h * aspect
        x -= (nw - w) / 2
        w = nw
    else:
        nh = min(fh, w / aspect)
        y -= (nh - h) / 2
        h = nh
    w, h = min(w, fw), min(h, fh)
    x = min(max(0, x), fw - w)
    y = min(max(0, y), fh - h)
    return (x, y, w, h)


def union(a, b):
    x, y = min(a[0], b[0]), min(a[1], b[1])
    return (x, y, max(a[0] + a[2], b[0] + b[2]) - x, max(a[1] + a[3], b[1] + b[3]) - y)


def inside(r, outer):
    return r[0] >= outer[0] and r[1] >= outer[1] and r[0] + r[2] <= outer[0] + outer[2] and r[1] + r[3] <= outer[1] + outer[3]


def plan(scene):
    """Per frame: the crop, the cursor position (or None) and the click ripple (0..1 or None)."""
    fw, fh, n = scene["width"], scene["height"], scene["frames"]
    events = sorted(scene["events"], key=lambda e: e["f"])
    full = (0, 0, fw, fh)
    goal_raw = full
    shown, start_from, goal, start = fit(full, fw, fh), None, fit(full, fw, fh), -10_000
    cursor_from = cursor_to = None
    cursor_start = -10_000
    rest_at, rest_to = None, None
    clicks = []
    crops, cursors, ripples = [], [], []
    i = 0
    for f in range(n):
        while i < len(events) and events[i]["f"] <= f:
            e = events[i]
            i += 1
            r = (e["x"], e["y"], e["w"], e["h"])
            if e["t"] == "focus":
                goal_raw = r
            elif e["t"] == "move":
                # Make sure the button is in the picture before the cursor gets there.
                target = (r[0] - 40, r[1] - 40, r[2] + 80, r[3] + 80)
                if not inside(target, goal):
                    goal_raw = union(goal_raw, target)
                centre = (r[0] + r[2] / 2, r[1] + r[3] / 2)
                here = cursors[-1] if cursors and cursors[-1] else (centre[0] + 260, centre[1] + 220)
                cursor_from, cursor_to, cursor_start = here, centre, f
                rest_at = None
            elif e["t"] == "click":
                centre = (r[0] + r[2] / 2, r[1] + r[3] / 2)
                clicks.append((f, centre))
                rest_at = f + 8                 # then drift off the button, so its text shows
                rest_to = (centre[0] + 70, centre[1] + 90)
            new_goal = fit(goal_raw, fw, fh)
            if new_goal != goal:
                start_from, goal, start = shown, new_goal, f
        if rest_at is not None and f >= rest_at and cursor_to is not None:
            cursor_from, cursor_to, cursor_start = cursor_to, rest_to, f
            rest_at = None
        shown = lerp(start_from, goal, smooth((f - start) / EASE)) if start_from else goal
        crops.append(shown)
        if cursor_to is None:
            cursors.append(None)
        else:
            cursors.append(lerp(cursor_from, cursor_to, smooth((f - cursor_start) / TRAVEL)))
        ripple = None
        for cf, at in clicks:
            if 0 <= f - cf < 16:
                ripple = ((f - cf) / 16, at)
        ripples.append(ripple)
    return crops, cursors, ripples


def cursor_sprite():
    s = 4  # draw big, shrink: smooth edges
    pts = [(0, 0), (0, 34), (9, 26), (15, 40), (21, 37), (15, 24), (27, 24)]
    img = Image.new("RGBA", (34 * s, 46 * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(x * s + 3 * s, y * s + 3 * s) for x, y in pts], fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=3 * s)
    return img.resize((34 * 2, 46 * 2), Image.LANCZOS)  # 2x: readable on a phone


def wrap(draw, text, fnt, width):
    words, lines, line = text.split(), [], ""
    for w in words:
        trial = (line + " " + w).strip()
        if draw.textlength(trial, font=fnt) <= width:
            line = trial
        else:
            lines.append(line)
            line = w
    lines.append(line)
    return lines


def overlay(scene, number, total):
    """The caption bars: what this scene shows, and the small print."""
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    brand = font("BarlowSemiCondensed_600SemiBold.ttf", 40)
    d.text((W / 2, 70), f"KLEMME15  ·  {number}/{total}", font=brand, fill=LIT, anchor="mm")
    title = font("BarlowSemiCondensed_700Bold.ttf", 82)
    lines = wrap(d, scene["title"], title, W - 120)
    y = 175 if len(lines) == 1 else 140
    for line in lines:
        d.text((W / 2, y), line, font=title, fill=(255, 255, 255), anchor="mm", stroke_width=2, stroke_fill=BG)
        y += 90
    small = font("Barlow_500Medium.ttf", 36)
    d.text((W / 2, APP_Y + APP_H + 70), "Free · open source · works offline", font=small, fill=INK, anchor="mm")
    if scene.get("note"):
        d.text((W / 2, APP_Y + APP_H + 125), scene["note"], font=font("Barlow_400Regular.ttf", 32), fill=MUTED, anchor="mm")
    return img


def encoder(path):
    return subprocess.Popen(
        ["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", "30", "-i", "-",
         "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", path],
        stdin=subprocess.PIPE)


def compose(scene, number, total):
    sid = scene["id"]
    fw, fh, n = scene["width"], scene["height"], scene["frames"]
    crops, cursors, ripples = plan(scene)
    sprite = cursor_sprite()
    text = overlay(scene, number, total)
    decoder = subprocess.Popen(["ffmpeg", "-loglevel", "error", "-i", os.path.join(RAW, sid + ".mp4"), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                               stdout=subprocess.PIPE)
    clean = encoder(os.path.join(OUT, "clean", sid + ".mp4"))
    captioned = encoder(os.path.join(OUT, "captioned", sid + ".mp4"))
    size = fw * fh * 3
    still_at = int(n * 0.6)
    for f in range(n):
        raw = decoder.stdout.read(size)
        if len(raw) < size:
            break
        frame = Image.frombytes("RGB", (fw, fh), raw)
        x, y, w, h = crops[f]
        k = min(APP_W / w, APP_H / h)
        ow, oh = round(w * k), round(h * k)
        ox, oy = (APP_W - ow) // 2, APP_Y + (APP_H - oh) // 2
        canvas = Image.new("RGB", (W, H), BG)
        canvas.paste(frame.resize((ow, oh), Image.LANCZOS, box=(x, y, x + w, y + h)), (ox, oy))
        d = ImageDraw.Draw(canvas)
        if ripples[f]:
            t, (rx, ry) = ripples[f]
            cx, cy = ox + (rx - x) * k, oy + (ry - y) * k
            rad = 18 + 52 * t
            shade = tuple(int(c + (b - c) * t) for c, b in zip(LIT, BG))
            d.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), outline=shade, width=6)
        if cursors[f]:
            cx, cy = cursors[f]
            px, py = ox + (cx - x) * k, oy + (cy - y) * k
            if APP_Y - 20 <= py <= APP_Y + APP_H:
                canvas.paste(sprite, (int(px - 6), int(py - 6)), sprite)
        # Keep the picture inside its area: a cursor near the edge mustn't spill into the caption bars.
        if oy > APP_Y or oy + oh < APP_Y + APP_H:
            d.rectangle((0, 0, W, APP_Y - 1), fill=BG)
            d.rectangle((0, APP_Y + APP_H, W, H), fill=BG)
        clean.stdin.write(canvas.tobytes())
        with_text = canvas.convert("RGBA")
        with_text.alpha_composite(text)
        with_text = with_text.convert("RGB")
        captioned.stdin.write(with_text.tobytes())
        if f == still_at:
            with_text.save(os.path.join(OUT, "stills", sid + ".png"))
    decoder.stdout.close()
    decoder.wait()
    for p in (clean, captioned):
        p.stdin.close()
        p.wait()
    # A landscape copy of the raw recording, for your own edits.
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", os.path.join(RAW, sid + ".mp4"), "-vf", "scale=1920:-2:flags=lanczos",
                    "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                    os.path.join(OUT, "landscape", sid + ".mp4")], check=True)
    print(f"  {sid}: {n} frames")


def main():
    for d in ("clean", "captioned", "stills", "landscape"):
        os.makedirs(os.path.join(OUT, d), exist_ok=True)
    scenes = []
    for name in sorted(os.listdir(RAW)):
        if name.endswith(".json") and os.path.exists(os.path.join(RAW, name[:-5] + ".mp4")):
            with open(os.path.join(RAW, name), encoding="utf-8") as fh:
                scene = json.load(fh)
            if scene["frames"] > 0:
                scenes.append(scene)
    if not scenes:
        sys.exit(f"No recorded scenes in {RAW}: check the recording step's output.")
    for i, scene in enumerate(scenes, 1):
        compose(scene, i, len(scenes))
    with open(os.path.join(OUT, "reel.txt"), "w", encoding="utf-8") as fh:
        for scene in scenes:
            fh.write(f"file 'captioned/{scene['id']}.mp4'\n")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", os.path.join(OUT, "reel.txt"),
                    "-c", "copy", "-movflags", "+faststart", os.path.join(OUT, "reel.mp4")], check=True)
    os.remove(os.path.join(OUT, "reel.txt"))
    print(f"Composed {len(scenes)} scenes into {OUT}")


if __name__ == "__main__":
    main()

# Klemme15 brand assets

| File | Use |
|---|---|
| `klemme15-icon.svg` / `.png` | App icon: white tile, spade terminal, 15. The master artwork. |
| `klemme15-icon-mono.svg` | Single-colour icon (no amber). |
| `klemme15-lockup-dark.svg` / `.png` | Icon + wordmark for dark backgrounds. |
| `klemme15-lockup-light.svg` / `.png` | Icon + wordmark for light backgrounds (inverted tile). |
| `klemme15-social.png` | 1280 x 640 GitHub social preview. |

Colours: ink `#0B0F14`, white `#FFFFFF`, amber `#F2A93B` (only the terminal's hole).
Type: Barlow SemiBold (numerals) and Barlow Medium (wordmark), SIL Open Font License, converted to outlines.

Everything here, the app's `Assets/Logo` folder, `Controls/LogoMark.axaml` and `Core/Logo.cs` is generated:

```
python3 tools/logo/gen.py      # SVG, XAML, C#   (fontTools)
node tools/logo/render.js      # PNG             (Playwright)
python3 tools/logo/ico.py      # Windows .ico    (Pillow)
```

Change the drawing in `tools/logo/gen.py`, never the outputs.

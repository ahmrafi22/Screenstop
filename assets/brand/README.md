# Screenstop brand assets

The mark is a capture frame: four corner brackets that read as a viewfinder at
any size. Everything else is restraint — no wordmark, no secondary glyph.

Open `preview.html` in a browser to see the full set side by side.

## Variants

The gradient is present in every variant. What changes is how each one protects
white brackets from the light end of the ramp.

| File | Use |
| --- | --- |
| `screenstop-logo.svg` | Hero mark. Full gradient plus the warm glow behind the top-left bracket. Docs, README, installer splash, About dialog. |
| `screenstop-icon.svg` | Rounded corners, heavier brackets, plus a vignette that deepens the corners. App icon, taskbar, tray, shortcuts. Legible to 16px. |
| `screenstop-mark.svg` | White brackets, transparent. Over photography or dark UI. |
| `screenstop-mark-gradient.svg` | Light end of the ramp (moss → cream), transparent. Gradient version for dark surfaces. |
| `screenstop-mark-ink.svg` | Dark end of the ramp (pine → moss), transparent. Gradient version for light surfaces. |

## How the gradient survives small sizes

The ramp ends in warm cream, and white brackets on cream have almost no
contrast. Rather than dropping the gradient at small sizes, each variant solves
it in place:

- The **icon** adds a radial vignette, deepening the corners where the brackets
  sit, so the light end never sits directly under a bracket.
- The brackets are heavier on the icon (30 units vs 21) and carry a drop shadow.
- The **gradient mark** is split by surface: the light end of the ramp for dark
  backgrounds, the dark end for light ones. A single ramp cannot serve both,
  because whichever end you choose disappears on the opposite surface.

## Palette

| Swatch | Hex | Role |
| --- | --- | --- |
| Deep pine | `#26483D` | Primary. Gradient start, ink mark, text on cream. |
| Pine light | `#2E5548` | Icon gradient top, for a hint of dimension. |
| Moss | `#8FA863` | Carries the green-to-cream turn. |
| Warm cream | `#F2EDCC` | Gradient end. |
| Glow | `#FFF3D2` | Warm highlight, never pure white — white over desaturated green turns grey. |
| Vignette | `#14261F` | Corner depth on the icon. |

## Geometry

512×512 viewBox. Bracket arms are 82 units long, 21 units thick on the logo and
marks, 30 on the icon. Arms are drawn as a single stroked path with miter joins
and butt caps so corners stay sharp at every scale.

## Regenerating the .ico

The app currently ships `src/Screenstop.App/Assets/screenstop.ico`, which
predates this mark. To swap in `screenstop-icon.svg`, render it to 16/32/48/
256px PNGs and pack them into a multi-size .ico. No SVG rasterizer is
installed in this environment (`rsvg-convert`, `inkscape`, and `magick` are
all absent), so that conversion is left as a follow-up.

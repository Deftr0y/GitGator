# GitGator identity

The approved mark is the friendly all-orange gator, with a charcoal smile and
one ivory tooth. The chin stays orange so its silhouette works on both light
and dark backgrounds. Keep the mark's aspect ratio and transparent padding.

`gitgator.svg` is the editable source of truth. It uses solid fills, with no
background, gradients, fonts or external assets:

| Color | Value | Use |
| --- | --- | --- |
| Git orange | `#F05032` | Head, chin, and the “Gator” wordmark |
| Charcoal | `#252326` | Mouth, pupil, nostril, and dark presentation backgrounds |
| Warm ivory | `#F0EFE7` | Eye, tooth, and light lettering on dark backgrounds |

Use the exact name **GitGator**. In the app, the “Git” lettering follows the
current theme's foreground; “Gator” stays Git orange.

## Export app icons

From the repository root, run:

```sh
python3 build/scripts/export-brand-assets.py
```

This requires Python 3, Pillow, librsvg 2 and Cairo. On Ubuntu the packages are
`python3-pil`, `librsvg2-2`, and `libcairo2`. Exported files are checked in, so
normal application builds do not require these tools.

The exporter updates:

- `src/Resources/Images/gitgator.png`: transparent 1024-pixel in-app image.
- `src/App.ico`: window/executable icon with 16, 24, 32, 48, 64, 128 and
  256-pixel representations.
- `packaging/linux/gitgator.png`: transparent 512-pixel desktop icon.

The Linux desktop entry uses `io.github.Deftr0y.GitGator` as its icon name.
See [Linux build instructions](../build/README.md) for local installation.

# cute clash kitten

`cute-clash.svg` is the editable master: an original navy-blue (`#172B4D`) baby kitten drawn for this project as a nod to Clash's feline identity. No upstream logo, font, raster image or third-party artwork is embedded. Its background, inner ears, eyes and smile are transparent, so the icon works on the light native application surfaces and Explorer.

The artwork is licensed under GPL-3.0-or-later with cute clash; see the repository's `LICENSE`.

- `cute-clash.png`: 512 × 512 transparent preview, derived from the SVG.
- `cute-clash.ico`: 16, 24, 32, 48, 64, 128 and 256px application icon. Frames below 256px use 32-bit DIB images with transparency masks for the .NET Framework icon loader; the 256px frame uses PNG for Explorer (supported since Windows Vista).

To regenerate both derived files after editing the SVG, run `powershell -ExecutionPolicy Bypass -File scripts/render-icon.ps1` from the repository root. The script installs the exact build-time renderer `@resvg/resvg-js@2.6.2` into `.tools/icon-renderer` and renders every size from the same SVG. Node.js/npm are needed for this optional artwork task only; the desktop application and normal build do not need them.

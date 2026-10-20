# Third-party notices

Retro87 is distributed under **GPL-3.0-or-later** (see [`LICENSE`](LICENSE)). It depends on
third-party packages, each under its own license. This file lists the direct dependencies and
notes the bundled assets. The full license text of each package is included in its NuGet package.

| Package | License | Used for |
|---|---|---|
| Avalonia | MIT | desktop UI framework |
| Avalonia.Desktop | MIT | platform backends |
| Avalonia.Themes.Fluent | MIT | base theme |
| Avalonia.Fonts.Inter | MIT (package) | embeds the **Inter** font family, licensed under SIL OFL 1.1 |
| CommunityToolkit.Mvvm | MIT | MVVM helpers |
| HidSharp | Apache-2.0 | cross-platform HID transport |
| Avalonia.Svg.Skia | MIT | rendering original SVG artwork |
| SkiaSharp / Svg.Skia | MIT | transitive dependencies of the SVG renderer |

Transitive dependencies of Avalonia (for example SkiaSharp, HarfBuzzSharp and MicroCom) are also
MIT-licensed and ship inside their own NuGet packages.

## Artwork

The files in `src/Retro87/Brand/*.svg` are **original** vector artwork created for this project
(a slate/amber calibration bench; ports with letters or the symbols » ◆ X ▾). They do not
incorporate images, logos, or assets from the official app, the Mecha Break game, or 8BitDo.

## Fonts

The **Inter** typeface is bundled through `Avalonia.Fonts.Inter` and is licensed under the **SIL
Open Font License 1.1**. Copyright (c) 2016 The Inter Project Authors
(<https://github.com/rsms/inter>).

## Trademarks

8BitDo and Retro 87 are trademarks of their respective owners. This project is unofficial and is
not affiliated with, endorsed by, or sponsored by 8BitDo.

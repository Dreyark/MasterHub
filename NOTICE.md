# Third-party notices

MasterHub is MIT licensed (see `LICENSE`). It uses these packages, each under its own licence:

| Package | Licence |
| :-- | :-- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter | MIT (Inter font: SIL OFL 1.1) |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.DependencyInjection, Microsoft.Win32.SystemEvents, System.Management, System.Security.Cryptography.ProtectedData | MIT |
| NAudio.Wasapi | MIT |
| SIPSorcery | BSD-3-Clause (with additional attribution terms, see its repository) |
| SkiaSharp | MIT |
| Svg.Skia | MIT |

## Bundled assets

| Asset | Where | Licence |
| :-- | :-- | :-- |
| **Lucide** icons (lucide-static 1.73.0, 2 359 icons) – © Lucide Icons and Contributors; icons derived from **Feather** © 2013-present Cole Bemis | `src/MasterHub.Core/Rendering/Icons/lucide.txt.gz` (packed SVG elements) | ISC (Feather-derived icons: MIT). Full text: `src/MasterHub.Core/Rendering/Icons/LUCIDE_LICENSE.txt` |

Test projects additionally use xUnit and Avalonia.Headless (Apache-2.0 / MIT).

The Stream Deck plugin store lists plugins from the public [OpenAction plugin registry](https://github.com/OpenActionAPI/plugins) (only the index is read at run time, nothing from it is bundled). Plugins are third-party content downloaded from their authors' GitHub repositories; each plugin has its own licence.

The icon library uses Lucide (ISC); the MasterHub code itself is MIT licensed, Copyright (c) 2026 Dreyark.

MasterHub is an independent project and is not affiliated with Cooler Master, Elgato / Corsair, Razer, Wooting or any other company whose products it can work with; their names are trademarks of their respective owners.

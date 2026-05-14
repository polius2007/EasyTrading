# Package icons

NuGet picks these up via `<PackageIcon>icon.png</PackageIcon>` in the venue csproj files. Each
file is included only when it exists (the `Condition="Exists(...)"` in the csproj keeps the build
green when an icon is missing), so a package may publish without an icon — it just won't have a
custom thumbnail on nuget.org.

## Expected files

| File | Used by package         | Brand                |
|---------------------------|-------------------------|----------------------|
| `hyperliquid.png`         | `EasyTrading.HyperLiquid` | EasyTrading + HyperLiquid |
| `aster.png`               | `EasyTrading.Aster`       | EasyTrading + Aster Finance |
| `dydx.png`                | `EasyTrading.Dydx` (future) | EasyTrading + dYdX |

`EasyTrading.Abstractions` and `EasyTrading.Core` ship without a custom icon — they're
infrastructure packages that pull in transitively.

## Format requirements

NuGet accepts PNG, JPG, or GIF, but **PNG is preferred**. Dimensions should be **square**,
between 128×128 and 512×512, with a file size under 1 MB. Smaller icons stay legible in the
"dependency tree" rendering at ~32×32.

## Branding note

The exchange logos (HyperLiquid mark, Aster cross-star, dYdX "X") are the trademarks of their
respective exchanges; the composite icons combine them with the EasyTrading "ET" badge to mark
that this is a third-party integration library, not an official client. If a venue ever objects,
strip its logo from the composite (the ET badge alone is enough to identify the package).

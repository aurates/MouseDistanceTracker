# Mouse Distance Tracker

A small Windows-only .NET 8 WinForms app for testing raw mouse movement.

## Features

- Press **Tab** to start tracking
- Press **Tab** again to stop tracking
- Reads mouse movement from **WM_INPUT / Raw Input**
- Does not use cursor position or screen-pixel movement
- Shows:
  - Total path distance in raw counts
  - Total absolute X movement in raw counts
  - Total absolute Y movement in raw counts
  - Number of raw input events counted
  - Last raw delta

## Notes

The distance value is in **raw mouse counts**, not centimeters or inches.

The app uses `RAWMOUSE.lLastX` and `RAWMOUSE.lLastY` from relative Raw Input mouse events. For normal mice, those values are movement deltas reported by Raw Input. Absolute mouse devices are ignored because they provide normalized coordinates instead of relative movement counts.

## Local build

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

## Publish a single EXE

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## GitHub Actions

The included workflow builds and publishes a Windows x64 artifact on push, pull request, or manual dispatch.

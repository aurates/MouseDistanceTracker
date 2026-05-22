# Mouse Distance Tracker

A simple Windows app that tracks raw mouse movement distance.

## Features

- Press `Tab` to start tracking.
- Press `Tab` again to stop tracking.
- Shows total mouse movement distance in raw input counts.
- Shows net X/Y movement.
- Uses Windows Raw Input for mouse delta tracking.
- Uses a low-level keyboard hook for the Tab toggle while the app is running.

## Local build

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

## Publish single exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The `.exe` will be inside the `publish` folder.

## GitHub Actions

The workflow in `.github/workflows/build.yml` builds and publishes a Windows x64 executable. After the workflow finishes, download the `MouseDistanceTracker-win-x64` artifact from the GitHub Actions run.

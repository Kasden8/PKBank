# PKBank — Build

PKBank requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).

```bat
:: Set up .NET 10 (once per machine): installer from dot.microsoft.com, or user-local via the script
powershell -ExecutionPolicy Bypass -Command "irm https://dot.net/v1/dotnet-install.ps1 | iex; .\dotnet-install.ps1 -Channel 10.0 -Architecture x64"
:: The user-local copy installs under %LOCALAPPDATA%\Microsoft\dotnet — add it to your environment
setx DOTNET_ROOT "%LOCALAPPDATA%\Microsoft\dotnet"
```

## Build & test

```bat
dotnet restore PKBank.slnx
dotnet build PKBank.slnx --configuration Release
dotnet test PKBank.slnx --configuration Release
```

## Portable release (self-contained win-x64, single-file)

```bat
dotnet publish src\PKBank.App\PKBank.App.csproj --configuration Release ^
  --output artifacts\publish\win-x64 /property:PublishForWindows=true
:: Compress artifacts\publish\win-x64 into a zip for download
```

The resulting folder is self-contained (runtime included) — `PKBank.exe` runs without a .NET install.
The `Assets/` folder ships next to the exe and can be updated independently of the exe.

## Run

```bat
src\PKBank.App\bin\Release\net10.0-windows\PKBank.exe
:: or from the publish folder after publishing
```

Logs land in `%AppData%\PKBank\logs\`; the bank at `%AppData%\PKBank\bank`; settings and backups
next to them. Credits and licenses: [ATTRIBUTION.md](ATTRIBUTION.md).

## Automated builds

`.github/workflows/build.yml` builds & tests on push/PR (branch `master`, `pc-port`) on
windows-latest. `.github/workflows/release.yml` publishes the portable zip on tag `v*`.

Param()
$ErrorActionPreference = 'Stop'

Write-Host "Building tools (Release)..."
dotnet build tools/ancp/f/f.fsproj -c Release -v minimal
dotnet build tools/ancp/fcc/fcc.fsproj -c Release -v minimal

Write-Host "Installing/updating global tools..."
dotnet tool install --global --add-source ./tools/ancp/f/nupkg ancp 2>$null \
  ; if ($LASTEXITCODE -ne 0) { dotnet tool update --global --add-source ./tools/ancp/f/nupkg ancp }

dotnet tool install --global --add-source ./tools/ancp/fcc/nupkg ancp.fcc 2>$null \
  ; if ($LASTEXITCODE -ne 0) { dotnet tool update --global --add-source ./tools/ancp/fcc/nupkg ancp.fcc }

Write-Host "Done. Commands available: f, fcc"

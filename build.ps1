# Builds the self-contained single-file robot.exe and moves it to the project root.
#
#   .\build.ps1            ->  .\robot.exe   (Release, win-x64)
#
# Everything ships inside the one exe: the .NET runtime (no install needed) and the
# native raylib library (extracted to %TEMP% on first start).

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Tfm = "net10.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Main.csproj pins ApplicationIcon to Main\icon.ico, which git does not carry:
# generate it from the tracked PNG so a fresh clone builds without a binary in git.
$ico = Join-Path $root "Main\icon.ico"
if (!(Test-Path $ico)) {
    $pngPath = Join-Path $root "assets\icon.png"
    if (!(Test-Path $pngPath)) { throw "neither Main\icon.ico nor assets\icon.png exists" }
    $png = [System.IO.File]::ReadAllBytes($pngPath)
    $w = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($png, 16))
    $h = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($png, 20))
    $fs = [System.IO.File]::Create($ico)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)  # ICONDIR
    $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
    $bw.Write([byte]($(if ($h -ge 256) { 0 } else { $h })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)                       # one 32bpp PNG image
    $bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
    $bw.Write($png)
    $bw.Close(); $fs.Close()
    Write-Host "generated Main\icon.ico from assets\icon.png"
}

dotnet publish (Join-Path $root "Main\Main.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -v q --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$published = Join-Path $root "Main\bin\$Configuration\$Tfm\$Runtime\publish\robot.exe"
if (!(Test-Path $published)) { throw "publish output not found: $published" }

$target = Join-Path $root "robot.exe"
Move-Item -Path $published -Destination $target -Force

Write-Host "built $target ($([math]::Round((Get-Item $target).Length / 1MB, 1)) MB)"

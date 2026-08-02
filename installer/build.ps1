$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$manifest = Get-Content "src\Installer.Win\Payload\release-manifest.json" -Raw | ConvertFrom-Json
python "tools\release.py" `
    --version $manifest.version `
    --echoforge-root $root `
    --skip-prepare @args
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

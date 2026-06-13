$ErrorActionPreference = "Stop"
$modsDir = "C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods"
$modName = "StardewLivingValley"
$targetDir = Join-Path $modsDir $modName
$configPath = Join-Path $targetDir "config.json"
$tempConfigPath = Join-Path $env:TEMP "config_backup_$modName.json"

Write-Host "Backing up config.json if it exists..."
if (Test-Path $configPath) {
    Copy-Item $configPath -Destination $tempConfigPath -Force
    Write-Host "Config backed up to $tempConfigPath"
} else {
    Write-Host "No config.json found to backup."
}

Write-Host "Cleaning old build..."
if (Test-Path $targetDir) {
    Remove-Item -Recurse -Force $targetDir
    Write-Host "Old build deleted."
}

Write-Host "Running dotnet build..."
dotnet build

Write-Host "Restoring config.json..."
if (Test-Path $tempConfigPath) {
    Copy-Item $tempConfigPath -Destination $configPath -Force
    Remove-Item $tempConfigPath -Force
    Write-Host "Config restored to $configPath"
} else {
    Write-Host "No config.json to restore."
}

Write-Host "Build and deploy complete!"

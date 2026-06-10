$sourceFile = "c:\Users\Trabajo\Desktop\Trabajo\Codigos de Ejemplo\StardewLivingRPG.zip-42597-1-0-5-1774874635\StardewLivingRPG\[0] Living Valley Main\assets\vanilla-canon-lore.json"
$targetDir = "c:\Users\Trabajo\Desktop\Trabajo\Proyectos AI\Proyecto X\Assets\Knowledge"

$jsonRaw = [System.IO.File]::ReadAllText($sourceFile)
$json = $jsonRaw | ConvertFrom-Json

foreach ($npc in $json.Npcs.PSObject.Properties) {
    $npcName = $npc.Name
    $npcData = $npc.Value
    
    $npcDir = Join-Path $targetDir $npcName
    if (-not (Test-Path $npcDir)) {
        New-Item -ItemType Directory -Force -Path $npcDir | Out-Null
    }
    
    # Manejar ForbiddenClaims si existe, si no, array vacío
    $forbidden = @()
    if ($null -ne $npcData.ForbiddenClaims) {
        $forbidden = $npcData.ForbiddenClaims
    }
    
    # Crear el objeto de perfil
    $profile = [ordered]@{
        "Role" = if ($null -ne $npcData.Role) { $npcData.Role } else { "" }
        "Persona" = if ($null -ne $npcData.Persona) { $npcData.Persona } else { "" }
        "Speech" = if ($null -ne $npcData.Speech) { $npcData.Speech } else { "" }
        "Ties" = if ($null -ne $npcData.Ties) { $npcData.Ties } else { "" }
        "Boundaries" = if ($null -ne $npcData.Boundaries) { $npcData.Boundaries } else { "" }
        "ForbiddenClaims" = $forbidden
        "DynamicLore" = @()
    }
    
    $profileJson = $profile | ConvertTo-Json -Depth 10
    $profilePath = Join-Path $npcDir "profile.json"
    
    Set-Content -Path $profilePath -Value $profileJson -Encoding UTF8
    Write-Output "Created profile for $npcName"
}

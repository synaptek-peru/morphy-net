$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$localSkill = Join-Path $scriptDir ".claude\skills\morphy\SKILL.md"
$destDir = Join-Path $HOME ".claude\skills\morphy"
$destFile = Join-Path $destDir "SKILL.md"

New-Item -ItemType Directory -Force -Path $destDir | Out-Null

if (Test-Path $localSkill) {
    Copy-Item $localSkill $destFile -Force
    Write-Host "Installed Morphy skill from local repo -> $destFile"
} else {
    $tmp = Join-Path $env:TEMP "morphy-skill-$(Get-Random)"
    try {
        Write-Host "Local SKILL.md not found. Cloning morphy-net repo..."
        git clone --depth=1 git@github.com:synaptek-peru/morphy-net.git $tmp 2>&1 | Out-Null
        Copy-Item (Join-Path $tmp ".claude\skills\morphy\SKILL.md") $destFile -Force
        Write-Host "Installed Morphy skill from remote repo -> $destFile"
    } finally {
        if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    }
}

Write-Host "Done. Restart Claude Code to load the new skill."

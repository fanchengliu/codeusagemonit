param([string]$OutputDirectory = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
& (Join-Path $PSScriptRoot 'verify-interaction.ps1') -AppDirectory $OutputDirectory
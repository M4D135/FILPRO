#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$WebView2,
    [Parameter(Mandatory)][string]$Usmt,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [ValidateSet('PROD', 'PROD-LIGHT')][string]$Flavor = 'PROD',
    [switch]$AllowNetworkRestore
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Flavor = $Flavor.ToUpperInvariant()
if ($Flavor -eq 'PROD-LIGHT' -and $PSBoundParameters.ContainsKey('WebView2')) {
    throw 'PROD-LIGHT utilise Evergreen sur le poste : ne pas fournir -WebView2.'
}
if ($Flavor -ne 'PROD-LIGHT' -and [string]::IsNullOrWhiteSpace($WebView2)) {
    throw 'PROD exige la distribution fixe -WebView2.'
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choisir un nouveau dossier de travail.' }
if ($output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $output -eq $root) {
    throw 'Le dossier de sortie doit être hors des sources.'
}
$null = Get-Command dotnet -CommandType Application -ErrorAction Stop
$null = Get-Command python -CommandType Application -ErrorAction Stop
New-Item -ItemType Directory -Path $output | Out-Null
$nuget = if ($AllowNetworkRestore) { Join-Path $root 'tools/ci/NuGet.Windows.config' } else { Join-Path $root 'NuGet.config' }
$arguments = @('publish', (Join-Path $root 'src/Filpro.App/Filpro.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-m:1', '--configfile', $nuget,
    '--artifacts-path', (Join-Path $output 'artifacts'), '-o', (Join-Path $output 'publish'), '-p:EnableWindowsTargeting=true')
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée ; les journaux et fichiers de travail sont conservés.' }
$prepare = @((Join-Path $PSScriptRoot 'prepare_input.py'), '--publish', (Join-Path $output 'publish'), '--usmt', $Usmt,
    '--output', (Join-Path $output 'input'), '--flavor', $Flavor, '--source-commit', $SourceCommit)
if ($Flavor -ne 'PROD-LIGHT') { $prepare += @('--webview2', $WebView2) }
& python @prepare
if ($LASTEXITCODE -ne 0) { throw 'Assemblage refusé ; aucun pack qualifié.' }
Write-Host "Entrée prête pour la qualification : $(Join-Path $output 'input')"
Write-Host 'Les tests, create_pack.py et la publication restent des étapes distinctes.'

#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [switch] $AllowNetworkRestore,
    [ValidateSet('ModeTests', 'BridgeTests', 'BackupPlanTests', 'RestorePlanTests',
        'SauvegarderPlanTests', 'SauvegarderRestorePlanTests', 'UsmtArgumentsTests',
        'SoftwareSelectionCodecTests', 'SoftwareSelectionStoreTests', 'SoftwareCatalogTests',
        'ConflictPolicyTests', 'SessionEvidenceTests', 'SimulationTests',
        'LastSelectionRulesTests', 'PosteSelectionPersistenceTests', 'SelectionBridgeTests',
        'SoftwareDiscoveryCatalogTests', 'SoftwareFileDiscoveryTests', 'LoadedRegistryDiscoveryTests',
        'LoadedRegistryWindowsTests', 'ConfigurationCodecTests', 'ConfigurationStoreTests',
        'StoragePathPolicyTests', 'PackIntegrityTests', 'UpdateTransactionTests', 'UpdateSourcePreferenceTests',
        'ApplicationOperationGateTests', 'ConfirmedPlanStoreTests', 'CommonSelectionTests', 'CommonPatchTests',
        'ExecutionBridgeTests', 'SimulationWorldTests', 'ExecutionCoordinatorTests', 'ExecutionHistoryTests',
        'ExecutionManifestTests', 'ExecutionInputSafetyTests', 'ExecutionCleanupTests', 'PlainStoreTests',
        'ProfileFolderPolicyTests', 'LegacyExecutionTests', 'RepairReconciliationTests',
        'OfflineUserHiveTests', 'OwnedCopiedHiveMountTests',
        'ExecutionWindowsTests', 'StorageFileIdentityWindowsTests', 'StartupPolicyTests', 'UserDataLocationTests',
        'WebViewDataLocationTests', 'SessionAccessTests',
        'PrivilegePolicyTests', 'NativeConfirmationTests', 'ConfirmationSheetsTests', 'NativeWindowPolicyTests',
        'ConfirmationBindingTests', 'OperatorPresenceTests', 'SituationAdmissionTests', 'ConfirmationThemeTests',
        'LightUpdateTests', 'WebViewRuntimePolicyTests', 'WebViewSocleTests')]
    [string[]] $Suite = @('ModeTests', 'BridgeTests', 'BackupPlanTests', 'RestorePlanTests',
        'SauvegarderPlanTests', 'SauvegarderRestorePlanTests', 'UsmtArgumentsTests',
        'SoftwareSelectionCodecTests', 'SoftwareSelectionStoreTests', 'ConflictPolicyTests',
        'LastSelectionRulesTests', 'PosteSelectionPersistenceTests', 'SelectionBridgeTests',
        'SoftwareDiscoveryCatalogTests', 'SoftwareFileDiscoveryTests', 'LoadedRegistryDiscoveryTests',
        'LoadedRegistryWindowsTests', 'ConfigurationCodecTests', 'ConfigurationStoreTests',
        'StoragePathPolicyTests', 'PackIntegrityTests', 'UpdateTransactionTests', 'UpdateSourcePreferenceTests',
        'ApplicationOperationGateTests', 'ConfirmedPlanStoreTests', 'CommonSelectionTests', 'CommonPatchTests',
        'ExecutionBridgeTests', 'SimulationWorldTests', 'ExecutionCoordinatorTests', 'ExecutionHistoryTests',
        'ExecutionManifestTests', 'ExecutionInputSafetyTests', 'ExecutionCleanupTests', 'PlainStoreTests',
        'ProfileFolderPolicyTests', 'LegacyExecutionTests', 'RepairReconciliationTests',
        'OfflineUserHiveTests', 'OwnedCopiedHiveMountTests',
        'ExecutionWindowsTests', 'StorageFileIdentityWindowsTests', 'StartupPolicyTests', 'UserDataLocationTests',
        'WebViewDataLocationTests', 'SessionAccessTests',
        'PrivilegePolicyTests', 'NativeConfirmationTests', 'ConfirmationSheetsTests', 'NativeWindowPolicyTests',
        'ConfirmationBindingTests', 'OperatorPresenceTests', 'SituationAdmissionTests', 'ConfirmationThemeTests',
        'LightUpdateTests', 'WebViewRuntimePolicyTests', 'WebViewSocleTests')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows -or -not [Environment]::Is64BitProcess) {
    throw 'Cette validation exige Windows et un processus PowerShell x64. Aucun test exécuté.'
}
if ($Suite.Count -eq 0 -or @($Suite | Select-Object -Unique).Count -ne $Suite.Count) {
    throw 'Choisir au moins une suite, sans doublon.'
}

$filproRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('FILPRO-V2-validation-' + [guid]::NewGuid().ToString('N'))
}
$filproEvidence = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $filproEvidence) {
    throw 'Le dossier de preuves doit être nouveau : aucun ancien résultat ne sera remplacé.'
}
$filproRepoRoot = Split-Path -Parent $filproRoot
if ($filproEvidence.Equals($filproRepoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $filproEvidence.StartsWith($filproRepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choisir un dossier de preuves hors du dépôt, pour préserver les sources et les packs V1.'
}
New-Item -ItemType Directory -Path $filproEvidence | Out-Null

function Get-FilproSourceInventory {
    @(Get-ChildItem -LiteralPath $filproRoot -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs|out)[\\/]' } |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($filproRoot, $_.FullName).Replace('\', '/')
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
}

function Get-FilproInventoryHash([object[]] $Inventory) {
    $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Inventory -Depth 5 -Compress))
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Invoke-FilproDotNet([string] $Name, [string[]] $Arguments) {
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $filproEvidence ($Name + '.log')) | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet : étape $Name en échec (code $LASTEXITCODE)."
    }
}

$filproReport = [ordered]@{
    schemaVersion = 1
    startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    finishedUtc = $null
    sourceCommit = $null
    sourceWorkingTreeClean = $false
    sourceFilesSHA256 = $null
    sourceUnchanged = $false
    exactCommitProven = $false
    platform = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    networkRestoreRequested = [bool] $AllowNetworkRestore
    scope = 'Compilation Windows et contrats ciblés ; données fictives, fichiers temporaires et lectures du contexte Windows courant.'
    windowsBuildPassed = $false
    windowsContractsPassed = $false
    loadedUserRegistryReadPassed = $false
    directoryLeaseWindowsPassed = $false
    hardlinkWindowsPassed = $false
    fullWindowsQualification = $false
    guiQualified = $false
    usmtQualified = $false
    realProfilesQualified = $false
    releaseQualified = $false
    suites = @()
    status = 'Failed'
    failedPhase = 'initialization'
    error = $null
}
$filproBefore = @()
$filproExit = 1
Push-Location $filproRoot
try {
    $null = Get-Command dotnet -CommandType Application -ErrorAction Stop
    if (Get-Command git -CommandType Application -ErrorAction SilentlyContinue) {
        $head = & git -C $filproRepoRoot rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $head -match '^[a-fA-F0-9]{40}$') {
            $filproReport.sourceCommit = $head.Trim().ToLowerInvariant()
            $dirty = @(& git -C $filproRepoRoot status --porcelain=v1 --untracked-files=all -- v2)
            $filproReport.sourceWorkingTreeClean = ($LASTEXITCODE -eq 0 -and $dirty.Count -eq 0)
            if ($env:GITHUB_SHA -and $env:GITHUB_SHA -ne $filproReport.sourceCommit) {
                throw 'Le commit du checkout diffère du commit annoncé par GitHub.'
            }
        }
    }
    $filproBefore = @(Get-FilproSourceInventory)
    $filproReport.sourceFilesSHA256 = Get-FilproInventoryHash $filproBefore
    ConvertTo-Json -InputObject $filproBefore -Depth 5 | Set-Content -LiteralPath (Join-Path $filproEvidence 'source-files.json') -Encoding utf8
    Invoke-FilproDotNet 'dotnet-info' @('--info')
    $filproReport.failedPhase = 'restore'
    $nuget = if ($AllowNetworkRestore) { Join-Path $PSScriptRoot 'NuGet.Windows.config' } else { Join-Path $filproRoot 'NuGet.config' }
    # Un runner neuf doit restaurer WindowsDesktop et ses références transitives.
    # Même valeur à restore/build ; le comportement hors ligne par défaut reste inchangé.
    [string[]] $frameworkArguments = @()
    if ($AllowNetworkRestore) { $frameworkArguments = @('-p:DisableTransitiveFrameworkReferenceDownloads=false') }
    Invoke-FilproDotNet 'restore' (@('restore', 'Filpro.slnx', '--configfile', $nuget, '--disable-parallel') + $frameworkArguments)
    $filproReport.failedPhase = 'build'
    Invoke-FilproDotNet 'build-release' (@('build', 'Filpro.slnx', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1') + $frameworkArguments)
    $filproReport.windowsBuildPassed = $true

    $assembly = Join-Path $filproRoot 'tests/Filpro.Tests/bin/Release/net10.0-windows/win-x64/Filpro.Tests.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Assemblée de tests attendue absente.' }
    foreach ($name in $Suite) {
        $filproReport.failedPhase = $name
        Invoke-FilproDotNet $name @($assembly, $name)
        $summary = Get-Content -LiteralPath (Join-Path $filproEvidence ($name + '.log')) -Tail 1
        if ($summary -notmatch '^(\d+) réussis, (\d+) en échec, (\d+) ignorés, (\d+) au total\.$') {
            throw "Résumé structuré attendu absent pour $name : aucun succès déduit du seul code de sortie."
        }
        $counts = [ordered]@{ name = $name; passed = [int] $Matches[1]; failed = [int] $Matches[2]; skipped = [int] $Matches[3]; total = [int] $Matches[4] }
        $filproReport.suites += $counts
        if ($counts.passed -eq 0 -or $counts.failed -ne 0 -or $counts.skipped -ne 0 -or $counts.passed -ne $counts.total) {
            throw "Suite ciblée $name incomplète : un contrôle ignoré ne vaut pas validation."
        }
        if ($name -eq 'LoadedRegistryWindowsTests') { $filproReport.loadedUserRegistryReadPassed = $true }
        if ($name -eq 'ExecutionWindowsTests') { $filproReport.directoryLeaseWindowsPassed = $true }
        if ($name -eq 'StorageFileIdentityWindowsTests') { $filproReport.hardlinkWindowsPassed = $true }
    }
    $filproReport.windowsContractsPassed = $true
    $filproReport.failedPhase = 'source-comparison'
    $filproAfter = @(Get-FilproSourceInventory)
    $filproReport.sourceUnchanged = ((Get-FilproInventoryHash $filproAfter) -eq $filproReport.sourceFilesSHA256)
    if (-not $filproReport.sourceUnchanged) { throw 'Les sources ont changé pendant la validation.' }
    $filproReport.exactCommitProven = $filproReport.sourceWorkingTreeClean -and $null -ne $filproReport.sourceCommit
    $filproReport.status = 'Passed'
    $filproReport.failedPhase = $null
    $filproExit = 0
}
catch {
    $filproReport.error = $_.Exception.Message
    Write-Warning $filproReport.error
}
finally {
    $filproReport.finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    ConvertTo-Json -InputObject $filproReport -Depth 8 | Set-Content -LiteralPath (Join-Path $filproEvidence 'validation.json') -Encoding utf8
    Pop-Location
    Write-Host "Preuves : $filproEvidence"
}
exit $filproExit

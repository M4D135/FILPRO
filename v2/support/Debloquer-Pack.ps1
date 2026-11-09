# Compatible Windows PowerShell 5.1. Usage volontaire depuis wad/support.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    function Assert-Ordinary([string]$Path) {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
        if (([int]$item.Attributes -band (0x400 -bor 0x1000 -bor 0x40000 -bor 0x400000)) -ne 0) {
            throw "Lien ou fichier a la demande refuse : $Path"
        }
        return $item
    }
    $ancestor = $root
    while ($ancestor) {
        $null = Assert-Ordinary $ancestor
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    $exe = Assert-Ordinary (Join-Path $root 'FILPRO.exe')
    $wad = Assert-Ordinary (Join-Path $root 'wad')
    if ($exe.PSIsContainer -or -not $wad.PSIsContainer) { throw 'Structure FILPRO.exe + wad invalide.' }
    $entries = @(Get-ChildItem -LiteralPath $root -Force)
    if ($entries.Count -ne 2) { throw 'La racine doit contenir uniquement FILPRO.exe et wad.' }
    # Inventaire complet avant toute modification ; aucune jonction n'est parcourue.
    $files = New-Object 'Collections.Generic.List[string]'
    $directories = New-Object 'Collections.Generic.Stack[string]'
    $directories.Push($root)
    $count = 0
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        $null = Assert-Ordinary $directory
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Force) {
            if (++$count -gt 80000) { throw 'Pack trop volumineux.' }
            $item = Assert-Ordinary $entry.FullName
            if ($item.PSIsContainer) { $directories.Push($item.FullName) }
            else { $files.Add($item.FullName) }
        }
    }
    foreach ($file in $files) {
        $null = Assert-Ordinary $file
        Unblock-File -LiteralPath $file -ErrorAction Stop
    }
    Write-Host ("Deblocage termine : {0} fichiers examines. Les donnees principales sont conservees." -f $files.Count)
    Write-Host 'Cette action retire uniquement le marquage de provenance Windows ; elle ne prouve pas la fiabilite du pack.'
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}

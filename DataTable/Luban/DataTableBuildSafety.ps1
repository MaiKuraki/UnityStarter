[CmdletBinding(DefaultParameterSetName = 'ValidateBridgeFiles')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateBridgeFiles')]
    [switch] $ValidateBridgeFiles,

    [Parameter(Mandatory = $true, ParameterSetName = 'CopyBridgeFiles')]
    [switch] $CopyBridgeFiles,

    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateTemplateRoot')]
    [switch] $ValidateTemplateRoot,

    [Parameter(Mandatory = $true, ParameterSetName = 'CleanOrphanMeta')]
    [switch] $CleanOrphanMeta,

    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateBridgeFiles')]
    [Parameter(Mandatory = $true, ParameterSetName = 'CopyBridgeFiles')]
    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateTemplateRoot')]
    [Parameter(Mandatory = $true, ParameterSetName = 'CleanOrphanMeta')]
    [ValidateNotNullOrEmpty()]
    [string] $RepoRoot,

    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateBridgeFiles')]
    [Parameter(Mandatory = $true, ParameterSetName = 'CopyBridgeFiles')]
    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateTemplateRoot')]
    [ValidateNotNullOrEmpty()]
    [string] $CustomTemplateRoot,

    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateBridgeFiles')]
    [Parameter(Mandatory = $true, ParameterSetName = 'CopyBridgeFiles')]
    [ValidateNotNullOrEmpty()]
    [string] $OutputRoot,

    [Parameter(Mandatory = $true, ParameterSetName = 'ValidateBridgeFiles')]
    [Parameter(Mandatory = $true, ParameterSetName = 'CopyBridgeFiles')]
    [ValidateNotNullOrEmpty()]
    [string] $BridgeFiles,

    [Parameter(Mandatory = $true, ParameterSetName = 'CleanOrphanMeta')]
    [ValidateNotNullOrEmpty()]
    [string] $Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$MaximumBridgeFiles = 256
$MaximumBridgePathCharacters = 1024
$MaximumBridgeSegmentCharacters = 255
$MaximumBridgeFileBytes = 16L * 1024 * 1024
$MaximumTotalBridgeBytes = 64L * 1024 * 1024
$MaximumCleanupEntries = 500000
$MaximumCleanupDirectories = 100000
$MaximumCleanupDepth = 128
$MaximumOrphanMetaCandidates = 100000
$MaximumOrphanMetaFileBytes = 1024L * 1024
$MaximumTotalOrphanMetaBytes = 256L * 1024 * 1024

function Get-CanonicalFullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if (-not [string]::IsNullOrEmpty($pathRoot) -and
        [string]::Equals($fullPath, $pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $pathRoot
    }

    return $fullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Test-EqualPath {
    param(
        [Parameter(Mandatory = $true)][string] $First,
        [Parameter(Mandatory = $true)][string] $Second
    )

    return [string]::Equals(
        (Get-CanonicalFullPath -Path $First),
        (Get-CanonicalFullPath -Path $Second),
        [StringComparison]::OrdinalIgnoreCase)
}

function Test-IsEqualOrChildPath {
    param(
        [Parameter(Mandatory = $true)][string] $Candidate,
        [Parameter(Mandatory = $true)][string] $Parent
    )

    $candidatePath = Get-CanonicalFullPath -Path $Candidate
    $parentPath = Get-CanonicalFullPath -Path $Parent
    if ([string]::Equals($candidatePath, $parentPath, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $prefix = $parentPath
    if (-not $prefix.EndsWith([IO.Path]::DirectorySeparatorChar.ToString(), [StringComparison]::Ordinal)) {
        $prefix += [IO.Path]::DirectorySeparatorChar
    }

    return $candidatePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparseChain {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $StopRoot,
        [Parameter(Mandatory = $true)][string] $Description
    )

    $probe = Get-CanonicalFullPath -Path $Path
    $stop = Get-CanonicalFullPath -Path $StopRoot
    if (-not (Test-IsEqualOrChildPath -Candidate $probe -Parent $stop)) {
        throw "$Description is not contained by its approved root: $Path"
    }

    while ($true) {
        if (Test-Path -LiteralPath $probe) {
            $item = Get-Item -Force -LiteralPath $probe
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Description traverses a reparse point: $probe"
            }
        }

        if ([string]::Equals($probe, $stop, [StringComparison]::OrdinalIgnoreCase)) {
            return
        }

        $parent = [IO.Directory]::GetParent($probe)
        if ($null -eq $parent) {
            throw "$Description is not contained by its approved root: $Path"
        }

        $probe = $parent.FullName
    }
}

function Get-DirectChildItem {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = Get-CanonicalFullPath -Path $Path
    $parentPath = [IO.Path]::GetDirectoryName($fullPath)
    $leafName = [IO.Path]::GetFileName($fullPath)
    if ([string]::IsNullOrEmpty($parentPath) -or
        [string]::IsNullOrEmpty($leafName) -or
        -not [IO.Directory]::Exists($parentPath)) {
        return $null
    }

    foreach ($item in Get-ChildItem -Force -LiteralPath $parentPath) {
        if ([string]::Equals($item.Name, $leafName, [StringComparison]::OrdinalIgnoreCase)) {
            return $item
        }
    }

    return $null
}

function Assert-PhysicalLeafIfPresent {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    $item = Get-DirectChildItem -Path $Path
    if ($null -ne $item -and
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description refuses a reparse-point path: $Path"
    }

    return $item
}

function Initialize-SafetyContext {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'DataTableBuildSafety.ps1 is the Windows-only safety boundary. Use the Unix wrapper on macOS or Linux.'
    }

    $canonicalRepoRoot = Get-CanonicalFullPath -Path $RepoRoot
    if (-not [IO.Directory]::Exists($canonicalRepoRoot)) {
        throw "Repository root does not exist: $canonicalRepoRoot"
    }

    $expectedScriptPath = Get-CanonicalFullPath -Path (
        [IO.Path]::Combine($canonicalRepoRoot, 'DataTable', 'Luban', 'DataTableBuildSafety.ps1'))
    $actualScriptPath = Get-CanonicalFullPath -Path $PSCommandPath
    if (-not [IO.File]::Exists($expectedScriptPath) -or
        -not (Test-EqualPath -First $actualScriptPath -Second $expectedScriptPath)) {
        throw "Repository root does not own this safety script: $canonicalRepoRoot"
    }

    $unityAssetsRoot = [IO.Path]::Combine($canonicalRepoRoot, 'UnityStarter', 'Assets')
    if (-not [IO.Directory]::Exists($unityAssetsRoot)) {
        throw "Repository root does not contain the Unity Assets directory: $unityAssetsRoot"
    }

    Assert-NoReparseChain -Path $actualScriptPath -StopRoot $canonicalRepoRoot -Description 'Safety script'

    $approvedRoots = @(
        (Get-CanonicalFullPath -Path ([IO.Path]::Combine(
            $canonicalRepoRoot, 'UnityStarter', 'Assets', 'UnityStarter', 'Scripts', 'Generated', 'DataTable'))),
        (Get-CanonicalFullPath -Path ([IO.Path]::Combine(
            $canonicalRepoRoot, 'UnityStarter', 'Assets', 'StreamingAssets', 'DataTable'))),
        (Get-CanonicalFullPath -Path ([IO.Path]::Combine(
            $canonicalRepoRoot, 'DataTable', 'Luban', 'Generated')))
    )

    $approvedTemplateRoot = Get-CanonicalFullPath -Path ([IO.Path]::Combine(
        $canonicalRepoRoot, 'DataTable', 'Luban'))

    return [pscustomobject]@{
        RepoRoot = $canonicalRepoRoot
        ApprovedRoots = $approvedRoots
        ApprovedTemplateRoot = $approvedTemplateRoot
    }
}

function Assert-ApprovedOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)][string] $Description
    )

    $canonicalPath = Get-CanonicalFullPath -Path $Path
    $approved = $false
    foreach ($approvedRoot in $Context.ApprovedRoots) {
        if (Test-IsEqualOrChildPath -Candidate $canonicalPath -Parent $approvedRoot) {
            $approved = $true
            break
        }
    }

    if (-not $approved) {
        throw "$Description is outside the fixed generated-output roots: $canonicalPath"
    }

    Assert-NoReparseChain -Path $canonicalPath -StopRoot $Context.RepoRoot -Description $Description
    return $canonicalPath
}

function Assert-PortableBridgeName {
    param([Parameter(Mandatory = $true)][string] $Name)

    if ($Name.Length -gt $MaximumBridgePathCharacters -or
        [IO.Path]::IsPathRooted($Name) -or
        $Name.IndexOf('\') -ge 0 -or
        $Name.IndexOf(':') -ge 0 -or
        $Name.IndexOf([char]0) -ge 0) {
        throw "Bridge file must use a portable relative slash-separated path: $Name"
    }

    $segments = $Name.Split([char]'/')
    for ($i = 0; $i -lt $segments.Length; $i++) {
        $segment = $segments[$i]
        if ([string]::IsNullOrEmpty($segment) -or
            $segment -eq '.' -or
            $segment -eq '..') {
            throw "Bridge file contains an empty or traversal segment: $Name"
        }
        if ($segment.Length -gt $MaximumBridgeSegmentCharacters -or
            -not [regex]::IsMatch(
                $segment,
                '\A[A-Za-z0-9._-]+\z',
                [Text.RegularExpressions.RegexOptions]::CultureInvariant) -or
            $segment.EndsWith('.', [StringComparison]::Ordinal) -or
            [regex]::IsMatch(
                $segment,
                '\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)',
                [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            throw (
                "Bridge path segment is not portable across Windows, macOS, and Linux: $segment. " +
                'Use 1-255 ASCII letters, digits, dot, underscore, or hyphen; avoid reserved device names and trailing dots.')
        }
    }
}

function Assert-ApprovedTemplateRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $Context
    )

    $canonicalPath = Get-CanonicalFullPath -Path $Path
    if (-not [IO.Directory]::Exists($canonicalPath)) {
        throw "Bridge custom template root does not exist: $canonicalPath"
    }
    if ((Test-EqualPath -First $canonicalPath -Second $Context.ApprovedTemplateRoot) -or
        -not (Test-IsEqualOrChildPath -Candidate $canonicalPath -Parent $Context.ApprovedTemplateRoot)) {
        throw (
            "Bridge custom template root must be a strict child of the repository-owned DataTable/Luban directory: $canonicalPath`n" +
            "Place the template directory below: $($Context.ApprovedTemplateRoot)")
    }

    Assert-NoReparseChain -Path $canonicalPath -StopRoot $Context.RepoRoot -Description 'Bridge custom template root'
    return $canonicalPath
}

function Test-FilesEqual {
    param(
        [Parameter(Mandatory = $true)][string] $First,
        [Parameter(Mandatory = $true)][string] $Second
    )

    $firstInfo = [IO.FileInfo]::new($First)
    $secondInfo = [IO.FileInfo]::new($Second)
    if ($firstInfo.Length -ne $secondInfo.Length) {
        return $false
    }

    $firstStream = [IO.FileStream]::new(
        $First,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        65536,
        [IO.FileOptions]::SequentialScan)
    try {
        $secondStream = [IO.FileStream]::new(
            $Second,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read,
            65536,
            [IO.FileOptions]::SequentialScan)
        try {
            $firstBuffer = New-Object byte[] 65536
            $secondBuffer = New-Object byte[] 65536
            while ($true) {
                $firstRead = $firstStream.Read($firstBuffer, 0, $firstBuffer.Length)
                $secondRead = $secondStream.Read($secondBuffer, 0, $secondBuffer.Length)
                if ($firstRead -ne $secondRead) {
                    return $false
                }
                if ($firstRead -eq 0) {
                    return $true
                }
                for ($i = 0; $i -lt $firstRead; $i++) {
                    if ($firstBuffer[$i] -ne $secondBuffer[$i]) {
                        return $false
                    }
                }
            }
        }
        finally {
            $secondStream.Dispose()
        }
    }
    finally {
        $firstStream.Dispose()
    }
}

function Get-BridgePlans {
    param([Parameter(Mandatory = $true)] $Context)

    if (-not [IO.Directory]::Exists($CustomTemplateRoot)) {
        throw "Bridge custom template root does not exist: $CustomTemplateRoot"
    }

    $custom = Assert-ApprovedTemplateRoot -Path $CustomTemplateRoot -Context $Context
    $output = Assert-ApprovedOutputRoot -Path $OutputRoot -Context $Context -Description 'Bridge output root'

    if ($BridgeFiles.Length -gt $MaximumBridgeFiles * $MaximumBridgePathCharacters) {
        throw 'bridge_files exceeds the bounded configuration length.'
    }

    $rawNames = $BridgeFiles.Split([char]',')
    if ($rawNames.Length -gt $MaximumBridgeFiles) {
        throw "Bridge file count $($rawNames.Length) exceeds the limit $MaximumBridgeFiles."
    }

    $destinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $plans = [Collections.Generic.List[object]]::new()
    [long] $totalBytes = 0

    foreach ($raw in $rawNames) {
        $name = $raw.Trim()
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'bridge_files contains an empty item.'
        }

        Assert-PortableBridgeName -Name $name
        $platformName = $name.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $path = Get-CanonicalFullPath -Path ([IO.Path]::Combine($custom, $platformName))
        if (-not (Test-IsEqualOrChildPath -Candidate $path -Parent $custom) -or
            (Test-EqualPath -First $path -Second $custom)) {
            throw "Bridge file escapes custom_template_dir: $name"
        }
        if (-not [IO.File]::Exists($path)) {
            throw "Bridge file does not exist: $path"
        }

        Assert-NoReparseChain -Path $path -StopRoot $custom -Description 'Bridge file'
        $sourceItem = Get-Item -Force -LiteralPath $path
        if ($sourceItem.PSIsContainer -or
            ($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Bridge source must be a physical file: $path"
        }
        if ($sourceItem.Length -gt $MaximumBridgeFileBytes) {
            throw "Bridge file exceeds the $MaximumBridgeFileBytes-byte limit: $path"
        }

        if ($totalBytes -gt $MaximumTotalBridgeBytes - [long]$sourceItem.Length) {
            throw "Bridge files exceed the total $MaximumTotalBridgeBytes-byte limit."
        }
        $totalBytes += [long]$sourceItem.Length

        $destination = Get-CanonicalFullPath -Path (
            [IO.Path]::Combine($output, [IO.Path]::GetFileName($path)))
        if (-not $destinations.Add($destination)) {
            throw "Bridge files collide at output name: $([IO.Path]::GetFileName($path))"
        }

        $destinationItem = Assert-PhysicalLeafIfPresent -Path $destination -Description 'Bridge output'
        if ($null -ne $destinationItem) {
            if ($destinationItem.PSIsContainer) {
                throw "Bridge output destination is a directory: $destination"
            }
            if (-not (Test-FilesEqual -First $path -Second $destination)) {
                throw (
                    "Bridge output already exists with different content and has no ownership proof: $destination`n" +
                    'Refusing to overwrite it. Remove or migrate the reviewed generated target explicitly, then rerun.')
            }
        }

        $plans.Add([pscustomobject]@{
                Name = $name
                Source = $path
                Destination = $destination
                Length = [long]$sourceItem.Length
            })
    }

    return $plans.ToArray()
}

function Copy-BridgeFileAtomic {
    param(
        [Parameter(Mandatory = $true)] $Plan,
        [Parameter(Mandatory = $true)] $Context
    )

    $outputRoot = Assert-ApprovedOutputRoot -Path $OutputRoot -Context $Context -Description 'Bridge output root'
    if (-not [IO.Directory]::Exists($outputRoot)) {
        throw "Bridge output root does not exist: $outputRoot"
    }

    $customRoot = Assert-ApprovedTemplateRoot -Path $CustomTemplateRoot -Context $Context
    Assert-NoReparseChain -Path $Plan.Source -StopRoot $customRoot -Description 'Bridge file'
    $existingDestination = Assert-PhysicalLeafIfPresent -Path $Plan.Destination -Description 'Bridge output'
    if ($null -ne $existingDestination) {
        if (-not $existingDestination.PSIsContainer -and
            (Test-FilesEqual -First $Plan.Source -Second $Plan.Destination)) {
            [Console]::WriteLine("[Luban] Bridge already current: $($Plan.Name)")
            return
        }

        throw "Bridge output appeared or changed after validation; refusing to overwrite: $($Plan.Destination)"
    }

    $temporaryPath = [IO.Path]::Combine(
        $outputRoot,
        '.' + [IO.Path]::GetFileName($Plan.Destination) + '.cyclonegames-bridge-' +
        [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $sourceStream = [IO.FileStream]::new(
            $Plan.Source,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read,
            65536,
            [IO.FileOptions]::SequentialScan)
        try {
            if ($sourceStream.Length -ne $Plan.Length -or
                $sourceStream.Length -gt $MaximumBridgeFileBytes) {
                throw "Bridge source changed after validation: $($Plan.Source)"
            }

            $temporaryStream = [IO.FileStream]::new(
                $temporaryPath,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None,
                65536,
                [IO.FileOptions]::WriteThrough)
            try {
                $buffer = New-Object byte[] 65536
                [long] $copied = 0
                while (($read = $sourceStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($copied -gt $MaximumBridgeFileBytes - [long]$read -or
                        $copied -gt [long]$Plan.Length - [long]$read) {
                        throw "Bridge source exceeded its validated byte budget while copying: $($Plan.Source)"
                    }
                    $copied += [long]$read
                    $temporaryStream.Write($buffer, 0, $read)
                }
                if ($copied -ne $Plan.Length) {
                    throw "Bridge source length changed while copying: $($Plan.Source)"
                }
                $temporaryStream.Flush($true)
            }
            finally {
                $temporaryStream.Dispose()
            }
        }
        finally {
            $sourceStream.Dispose()
        }

        $outputRoot = Assert-ApprovedOutputRoot -Path $OutputRoot -Context $Context -Description 'Bridge output root'
        $lateDestination = Assert-PhysicalLeafIfPresent -Path $Plan.Destination -Description 'Bridge output'
        if ($null -ne $lateDestination) {
            if (-not $lateDestination.PSIsContainer -and
                (Test-FilesEqual -First $temporaryPath -Second $Plan.Destination)) {
                [Console]::WriteLine("[Luban] Bridge became current during publication: $($Plan.Name)")
                return
            }

            throw "Bridge output appeared during publication; refusing to overwrite: $($Plan.Destination)"
        }

        [Console]::WriteLine("[Luban] Publishing bridge: $($Plan.Name)")
        [IO.File]::Move($temporaryPath, $Plan.Destination)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

function Invoke-CleanOrphanMeta {
    param([Parameter(Mandatory = $true)] $Context)

    if ($env:CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN -ne '1') {
        throw 'CleanOrphanMeta requires CYCLONE_DATATABLE_ALLOW_DESTRUCTIVE_CLEAN=1.'
    }

    $canonicalRoot = Assert-ApprovedOutputRoot -Path $Root -Context $Context -Description 'Orphan-meta cleanup root'
    if (-not [IO.Directory]::Exists($canonicalRoot)) {
        throw "Orphan-meta cleanup root does not exist: $canonicalRoot"
    }

    $pending = [Collections.Generic.Stack[object]]::new()
    $orphanCandidates = [Collections.Generic.List[string]]::new()
    $pending.Push([pscustomobject]@{ Path = $canonicalRoot; Depth = 0 })
    $directoryCount = 0
    $entryCount = 0
    [long] $candidateBytes = 0

    while ($pending.Count -gt 0) {
        $pendingDirectory = $pending.Pop()
        $directory = [string]$pendingDirectory.Path
        $depth = [int]$pendingDirectory.Depth
        if ($depth -gt $MaximumCleanupDepth) {
            throw "Orphan-meta cleanup depth exceeds the limit $MaximumCleanupDepth."
        }

        $directoryCount++
        if ($directoryCount -gt $MaximumCleanupDirectories) {
            throw "Orphan-meta cleanup directory count exceeds the limit $MaximumCleanupDirectories."
        }

        Assert-NoReparseChain -Path $directory -StopRoot $canonicalRoot -Description 'Orphan-meta cleanup'
        $directoryItem = Get-Item -Force -LiteralPath $directory
        if (($directoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Orphan-meta cleanup refuses reparse point: $directory"
        }

        foreach ($item in Get-ChildItem -Force -LiteralPath $directory) {
            $entryCount++
            if ($entryCount -gt $MaximumCleanupEntries) {
                throw "Orphan-meta cleanup entry count exceeds the limit $MaximumCleanupEntries."
            }
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Orphan-meta cleanup refuses reparse point: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $pending.Push([pscustomobject]@{ Path = $item.FullName; Depth = $depth + 1 })
                continue
            }
            if (-not [string]::Equals($item.Extension, '.meta', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $pairPath = $item.FullName.Substring(0, $item.FullName.Length - 5)
            if ($null -ne (Get-DirectChildItem -Path $pairPath)) {
                continue
            }
            if ($item.Length -gt $MaximumOrphanMetaFileBytes) {
                throw "Orphan .meta file exceeds the $MaximumOrphanMetaFileBytes-byte limit: $($item.FullName)"
            }
            if ($orphanCandidates.Count -ge $MaximumOrphanMetaCandidates) {
                throw "Orphan-meta candidate count exceeds the limit $MaximumOrphanMetaCandidates."
            }

            if ($candidateBytes -gt $MaximumTotalOrphanMetaBytes - [long]$item.Length) {
                throw "Orphan-meta candidates exceed the total $MaximumTotalOrphanMetaBytes-byte limit."
            }
            $candidateBytes += [long]$item.Length
            $orphanCandidates.Add($item.FullName)
        }
    }

    foreach ($metaPath in $orphanCandidates) {
        $metaItem = Get-DirectChildItem -Path $metaPath
        $pairPath = $metaPath.Substring(0, $metaPath.Length - 5)
        if ($null -eq $metaItem -or $null -ne (Get-DirectChildItem -Path $pairPath)) {
            continue
        }
        if ($metaItem.PSIsContainer -or
            ($metaItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $metaItem.Length -gt $MaximumOrphanMetaFileBytes) {
            throw "Orphan-meta candidate changed after validation: $metaPath"
        }

        Assert-NoReparseChain -Path $metaPath -StopRoot $canonicalRoot -Description 'Orphan-meta cleanup'
        [Console]::WriteLine("[Luban] Removing orphan meta: $metaPath")
        [IO.File]::Delete($metaPath)
    }
}

$context = Initialize-SafetyContext
switch ($PSCmdlet.ParameterSetName) {
    'ValidateBridgeFiles' {
        $null = Get-BridgePlans -Context $context
    }
    'CopyBridgeFiles' {
        $plans = Get-BridgePlans -Context $context
        foreach ($plan in $plans) {
            Copy-BridgeFileAtomic -Plan $plan -Context $context
        }
    }
    'ValidateTemplateRoot' {
        $null = Assert-ApprovedTemplateRoot -Path $CustomTemplateRoot -Context $context
    }
    'CleanOrphanMeta' {
        Invoke-CleanOrphanMeta -Context $context
    }
    default {
        throw "Unsupported safety action parameter set: $($PSCmdlet.ParameterSetName)"
    }
}

param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$ResourceVersion,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/bundled-images')
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw "Use a new output directory to avoid stale bundled images: $outputRoot" }
if ($ResourceVersion -notmatch '^\d+\.\d+(\.\d+)?$') { throw 'ResourceVersion must be a game snapshot version, such as 7.1.' }

# Accept an extracted raw directory, not URL-hashed cache files. Do not invent contract versions
# for the unversioned category ZIP API: the snapshot version is descriptive, not a freshness claim.
$images = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Sort-Object FullName)
$entries = [Collections.Generic.List[object]]::new()
foreach ($image in $images) {
    $relative = [IO.Path]::GetRelativePath($sourceRoot, $image.FullName).Replace('\', '/')
    if ($image.Extension -notin @('.png', '.jpg', '.jpeg', '.webp', '.gif', '.bmp')) { continue }
    if ($relative -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $relative.Split('/') -contains '..') {
        throw "Images must be under raw/<category>/<filename>: $relative"
    }
    if ($image.Length -le 0 -or ($image.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Invalid image: $relative" }
    $entries.Add([ordered]@{path=$relative; length=$image.Length; sha256=(Get-FileHash -LiteralPath $image.FullName -Algorithm SHA256).Hash})
}
if ($entries.Count -eq 0) { throw 'No images found. Supply the directory containing category folders.' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
foreach ($entry in $entries) {
    $destination = Join-Path $outputRoot $entry.path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $entry.path) -Destination $destination
}
[ordered]@{schemaVersion=1; resourceVersion=$ResourceVersion; files=@($entries.ToArray())} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'manifest.json') -Encoding utf8NoBOM
Write-Host "Prepared $($entries.Count) images ($ResourceVersion) at $outputRoot"

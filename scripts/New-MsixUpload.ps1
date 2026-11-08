param(
    [Parameter(Mandatory)][string]$PackagePath,
    [string]$SymbolsPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$package = (Resolve-Path -LiteralPath $PackagePath).Path
if ([IO.Path]::GetExtension($package) -ne '.msix') { throw 'PackagePath must be a built MSIX package.' }
$output = [IO.Path]::ChangeExtension($package, '.msixupload')
if (Test-Path -LiteralPath $output) { throw "Upload already exists: $output" }
if ($SymbolsPath) { $SymbolsPath = (Resolve-Path -LiteralPath $SymbolsPath).Path }

# A Store upload is a ZIP container containing the MSIX and, optionally, an appxsym
# symbol archive. Merely renaming the MSIX itself does not create an upload file.
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $package, [IO.Path]::GetFileName($package), [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
        if ($SymbolsPath) {
            $symbolEntry = $zip.CreateEntry(([IO.Path]::GetFileNameWithoutExtension($package) + '.appxsym'))
            $symbolStream = $symbolEntry.Open()
            try {
                $symbolZip = [IO.Compression.ZipArchive]::new($symbolStream, [IO.Compression.ZipArchiveMode]::Create, $true)
                try {
                    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($symbolZip, $SymbolsPath, [IO.Path]::GetFileName($SymbolsPath)) | Out-Null
                } finally { $symbolZip.Dispose() }
            } finally { $symbolStream.Dispose() }
        } else { Write-Warning 'No application PDB supplied; this upload will not include crash-analysis symbols.' }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Write-Host "Store upload: $output"

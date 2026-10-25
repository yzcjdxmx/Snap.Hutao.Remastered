$ErrorActionPreference = 'Stop'
# Pinned upstream source, no edits or PRs are sent to the upstream repository.
$revision = '270016179ea5c01ad9b76ea9331739c200ac3eb7'
$repoRoot = Split-Path $PSScriptRoot -Parent
$patchRoot = Join-Path $repoRoot 'native/CraftHotkey'
$tempRoot = Join-Path $env:RUNNER_TEMP 'craft-hotkey-native'
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
$archive = Join-Path $tempRoot 'island.zip'
Invoke-WebRequest -Uri "https://codeload.github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.UnlockerIsland/zip/$revision" -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $tempRoot -Force
$sourceRoot = Join-Path $tempRoot "Snap.Hutao.Remastered.UnlockerIsland-$revision"
$projectRoot = Join-Path $sourceRoot 'src/Snap.Hutao.Remastered.UnlockerIsland'
Push-Location $sourceRoot
try {
    git apply --check (Join-Path $patchRoot 'island.patch')
    if ($LASTEXITCODE -ne 0) { throw 'Pinned native source does not match the patch' }
    git apply (Join-Path $patchRoot 'island.patch')
    if ($LASTEXITCODE -ne 0) { throw 'Native patch failed' }
}
finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $patchRoot 'CraftHotkeyBinding.h') -Destination (Join-Path $projectRoot 'hook/CraftHotkeyBinding.h')
foreach ($suffix in @('', '.filters')) {
    $project = Join-Path $projectRoot ("Snap.Hutao.Remastered.UnlockerIsland.vcxproj" + $suffix)
    $text = Get-Content -LiteralPath $project -Raw
    $needle = '<ClInclude Include="hook\HookWndProc.h"'
    if (-not $text.Contains($needle)) { throw 'Expected native header item not found' }
    $text = $text.Replace($needle, '<ClInclude Include="hook\CraftHotkeyBinding.h" />' + [Environment]::NewLine + '    ' + $needle)
    [IO.File]::WriteAllText($project, $text, [Text.UTF8Encoding]::new($false))
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual C++ build tools unavailable' }
$msbuild = Join-Path $vs 'MSBuild/Current/Bin/MSBuild.exe'
& (Join-Path $env:VCPKG_INSTALLATION_ROOT 'vcpkg.exe') integrate install
if ($LASTEXITCODE -ne 0) { throw 'vcpkg integration failed' }
$nativeOut = Join-Path $tempRoot 'out'
$testOut = Join-Path $tempRoot 'test-out'
& $msbuild (Join-Path $patchRoot 'CraftHotkeyBindingTest.vcxproj') /m /v:minimal /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$testOut/" "/p:IntDir=$tempRoot/test-obj/" /p:VcpkgEnabled=false
if ($LASTEXITCODE -ne 0) { throw 'Native hotkey matcher tests failed to compile' }
& (Join-Path $testOut 'CraftHotkeyBindingTest.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native hotkey matcher tests failed' }
& $msbuild (Join-Path $projectRoot 'Snap.Hutao.Remastered.UnlockerIsland.vcxproj') /m /v:minimal /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$nativeOut/" "/p:SolutionDir=$sourceRoot/src/"
if ($LASTEXITCODE -ne 0) { throw 'Native module build failed' }
$assetRoot = Join-Path $repoRoot 'src/Snap.Hutao.Remastered/Snap.Hutao.Remastered/Assets/Native'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $nativeOut 'Snap.Hutao.Remastered.UnlockerIsland.dll') -Destination (Join-Path $assetRoot 'CraftModifiersIsland.dll')
Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE.txt') -Destination (Join-Path $assetRoot 'UnlockerIsland-LICENSE.txt')
Write-Output "Built CraftModifiersIsland.dll from $revision; native key matching tests passed."

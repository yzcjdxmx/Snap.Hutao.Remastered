$ErrorActionPreference = 'Stop'
# Pinned upstream source, no edits or PRs are sent to the upstream repository.
$repoRoot = Split-Path $PSScriptRoot -Parent
$patchRoot = Join-Path $repoRoot 'native/CraftHotkey'
function Build-CraftModule([string]$revision, [string]$outputName, [bool]$repairRewardOffset = $false) {
$tempRoot = Join-Path $env:RUNNER_TEMP ('hk-' + $revision.Substring(0, 8))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
$archive = Join-Path $tempRoot 'island.zip'
Invoke-WebRequest -Uri "https://codeload.github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.UnlockerIsland/zip/$revision" -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $tempRoot -Force
$sourceRoot = Join-Path $tempRoot "Snap.Hutao.Remastered.UnlockerIsland-$revision"
$projectRoot = Join-Path $sourceRoot 'src/Snap.Hutao.Remastered.UnlockerIsland'
$patchFile = Join-Path $tempRoot 'island.patch'
# Git checkout may turn the patch into CRLF; codeload source uses LF.
$patchText = [IO.File]::ReadAllText((Join-Path $patchRoot 'island.patch')).Replace("`r`n", "`n")
[IO.File]::WriteAllText($patchFile, $patchText, [Text.UTF8Encoding]::new($false))
Push-Location $sourceRoot
try {
    git apply --check $patchFile
    if ($LASTEXITCODE -ne 0) { throw 'Pinned native source does not match the patch' }
    git apply $patchFile
    if ($LASTEXITCODE -ne 0) { throw 'Native patch failed' }
    if ($repairRewardOffset) {
        $rewardPatch = Join-Path $tempRoot 'resin-7.2.patch'
        $rewardText = [IO.File]::ReadAllText((Join-Path $patchRoot 'resin-7.2.patch')).Replace("`r`n", "`n")
        [IO.File]::WriteAllText($rewardPatch, $rewardText, [Text.UTF8Encoding]::new($false))
        git apply --check $rewardPatch
        if ($LASTEXITCODE -ne 0) { throw 'Pinned 7.2 source does not match reward patch' }
        git apply $rewardPatch
        if ($LASTEXITCODE -ne 0) { throw '7.2 reward offset patch failed' }
    }
}
finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $patchRoot 'CraftHotkeyBinding.h') -Destination (Join-Path $projectRoot 'hook/CraftHotkeyBinding.h')
Copy-Item -LiteralPath (Join-Path $patchRoot 'ResinListOffset.h') -Destination (Join-Path $projectRoot 'hook/ResinListOffset.h')
foreach ($suffix in @('', '.filters')) {
    $project = Join-Path $projectRoot ("Snap.Hutao.Remastered.UnlockerIsland.vcxproj" + $suffix)
    $text = Get-Content -LiteralPath $project -Raw
    $needle = '<ClInclude Include="hook\HookWndProc.h"'
    if (-not $text.Contains($needle)) { throw 'Expected native header item not found' }
    $text = $text.Replace($needle, '<ClInclude Include="hook\CraftHotkeyBinding.h" />' + [Environment]::NewLine + '    ' + $needle)
    $text = $text.Replace($needle, '<ClInclude Include="hook\ResinListOffset.h" />' + [Environment]::NewLine + '    ' + $needle)
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
& $msbuild (Join-Path $projectRoot 'Snap.Hutao.Remastered.UnlockerIsland.vcxproj') /m /v:minimal /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$nativeOut/" "/p:IntDir=$tempRoot/obj/" "/p:SolutionDir=$sourceRoot/src/"
if ($LASTEXITCODE -ne 0) { throw 'Native module build failed' }
$assetRoot = Join-Path $repoRoot 'src/Snap.Hutao.Remastered/Snap.Hutao.Remastered/Assets/Native'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $nativeOut 'Snap.Hutao.Remastered.UnlockerIsland.dll') -Destination (Join-Path $assetRoot $outputName)
Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE.txt') -Destination (Join-Path $assetRoot 'UnlockerIsland-LICENSE.txt')
Write-Output "Built $outputName from $revision; native key matching and 7.2 reward offset tests passed."
}

# Match the separate upstream 7.1 and 7.2 delivery modules.
Build-CraftModule '270016179ea5c01ad9b76ea9331739c200ac3eb7' 'CraftModifiersIsland.dll'
Build-CraftModule 'e309041f5a304b4bcb9090f63ae203dc7520b99c' 'CraftModifiersIsland_7.2.0.dll' $true


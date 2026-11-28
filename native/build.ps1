param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\work\native-build'),
    [string]$VisualStudioRoot = $env:WEIMIRROR_VS_ROOT,
    [switch]$RunTests,
    [switch]$RunProtocolTests
)
$ErrorActionPreference = 'Stop'
$env:VSLANG = '1033'
if (!$VisualStudioRoot) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $VisualStudioRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    }
}
if (!$VisualStudioRoot) { throw 'Install Visual Studio C++ Build Tools and the Windows SDK, or set WEIMIRROR_VS_ROOT.' }
$vcvars = Join-Path $VisualStudioRoot 'VC\Auxiliary\Build\vcvars64.bat'
$cmake = Join-Path $VisualStudioRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ninja = Join-Path $VisualStudioRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
foreach ($tool in @($vcvars,$cmake,$ninja)) {
    if (!(Test-Path -LiteralPath $tool)) { throw "Missing C++ build tool: $tool" }
}
# Import the developer environment into this build process without persisting
# changes to the user's environment or printing its contents.
$developerEnvironment = & $env:ComSpec /d /c "call `"$vcvars`" >nul && set"
if ($LASTEXITCODE) { throw 'Could not initialize the x64 C++ build environment.' }
foreach ($line in $developerEnvironment) {
    if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1],$Matches[2],'Process') }
}
$buildDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$tests = if ($RunTests) { 'ON' } else { 'OFF' }
$protocolTests = if ($RunProtocolTests) { 'ON' } else { 'OFF' }
& $cmake -S $PSScriptRoot -B $buildDirectory -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" -DCMAKE_BUILD_TYPE=Release "-DIPHONEMIRROR_BUILD_TESTS=$protocolTests" "-DWEIMIRROR_AUDIO_TESTS=$tests"
if ($LASTEXITCODE) { throw 'Native configure failed.' }
$targets = @('iPhoneMirror.Core')
if ($RunTests) { $targets += 'WeiMirror.AudioQA' }
if ($RunProtocolTests) { $targets += 'iPhoneMirror.Core.Tests' }
& $cmake --build $buildDirectory --target @targets --parallel 4
if ($LASTEXITCODE) { throw 'Native build failed.' }
if ($RunTests) {
    & (Join-Path $buildDirectory 'WeiMirror.AudioQA.exe')
    if ($LASTEXITCODE) { throw 'Native audio policy checks failed.' }
}
if ($RunProtocolTests) {
    Push-Location (Join-Path $buildDirectory 'src\Core')
    try {
        & .\iPhoneMirror.Core.Tests.exe
        if ($LASTEXITCODE) { throw 'Native protocol checks failed.' }
    } finally { Pop-Location }
}
Join-Path $buildDirectory 'src\Core\iPhoneMirror.Core.dll'

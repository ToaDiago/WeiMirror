param([string]$BuildRoot = (Join-Path $PSScriptRoot ('..\work\qa-' + [guid]::NewGuid().ToString('N'))),
      [string]$ApplicationExe)
$ErrorActionPreference = 'Stop'
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$runtime = Join-Path $BuildRoot 'fixture-runtime'
$wireless = Join-Path $runtime 'Wireless'
New-Item -Force -ItemType Directory $wireless | Out-Null
$project = Split-Path -Parent $PSScriptRoot
$compile = Join-Path $PSScriptRoot 'Compile.ps1'
$support = Join-Path $BuildRoot FixtureSupport.dll
& $compile -Name FixtureSupport -Sources (Join-Path $PSScriptRoot 'FixtureSupport.cs') -Out $support -Library
# Static forwarding thunks are an explicit test DLL with the same native ABI.
# ILAsm produces 64-bit native exports callable from the normal P/Invoke.
$table = @(
'im_api_version ApiVersion int32'
'im_initialize Initialize int32'
'im_shutdown Shutdown void'
'im_last_error Error ptr'
'im_get_environment EnvironmentInfo int32 ptr'
'im_refresh_devices_ex UsbDevices int32 ptr ptr int32'
'im_refresh_wireless_devices AirDevices int32 ptr ptr'
'im_wireless_receiver_start_ex StartAir int32 ptr ptr uint32 uint32 uint32'
'im_wireless_receiver_stop StopAir void'
'im_wireless_receiver_get_status AirStatus int32 ptr ptr'
'im_session_create UsbSession int32 ptr ptr ptr'
'im_wireless_session_create AirSession int32 ptr ptr ptr'
'im_session_get_status Status int32 uint64 ptr'
'im_session_copy_latest_video_frame CopyFrame int32 uint64 ptr ptr ptr uint32 uint32'
'im_session_attach_preview Attach int32 uint64 ptr'
'im_session_detach_preview Detach void uint64 ptr'
'im_session_force_preview_refresh Refresh int32 uint64'
'im_session_set_audio_enabled EnableAudio int32 uint64 int32'
'im_session_stop Stop int32 uint64'
'im_session_destroy Destroy void uint64'
)
function Convert-IlType([string]$t) { if($t -eq 'ptr') {'native int'} else {$t} }
$il = [Collections.Generic.List[string]]::new()
$il.Add('.assembly extern mscorlib {}')
$il.Add('.assembly extern FixtureSupport {}')
$il.Add('.assembly TestShim {}')
$il.Add('.module iPhoneMirror.Core.dll')
$il.Add('.corflags 0')
$il.Add('.class public abstract sealed TestShim {')
$ordinal = 0
foreach ($line in $table) {
    $words = $line.Split(' ')
    $ordinal++
    $export = $words[0]
    $name = $words[1]
    $returnType = Convert-IlType $words[2]
    $argsIl = @($words | Select-Object -Skip 3 | ForEach-Object { Convert-IlType $_ })
    $sig = $argsIl -join ', '
    $il.Add(".method public static $returnType M${ordinal}($sig) cil managed {")
    $il.Add(".export [$ordinal] as $export")
    $il.Add('.maxstack 8')
    for ($i = 0; $i -lt $argsIl.Length; $i++) { $il.Add("ldarg.s $i") }
    $il.Add("call $returnType [FixtureSupport]FixtureSupport::${name}($sig)")
    $il.Add('ret')
    $il.Add('}')
}
$il.Add('}')
$ilPath = Join-Path $BuildRoot 'Fixture.il'
[IO.File]::WriteAllLines($ilPath,$il,[Text.Encoding]::ASCII)
$nativePath = Join-Path $runtime 'iPhoneMirror.Core.dll'
$asm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\ilasm.exe'
& $asm /QUIET /DLL /X64 /PE64 "/OUTPUT:$nativePath" $ilPath
if ($LASTEXITCODE) { throw "ILAsm failed with $LASTEXITCODE" }
$probe = Join-Path $wireless 'iPhoneMirror.WirelessHost.exe'
& $compile -Name FixtureHost -Sources (Join-Path $PSScriptRoot HostProbe.cs) -Out $probe
foreach ($name in 'airplay2dll.dll','avcodec-58.dll','avutil-56.dll','dnssd.dll','swresample-3.dll','swscale-5.dll') {
    [IO.File]::WriteAllBytes((Join-Path $wireless $name),[byte[]]@())
}
$test = Join-Path $BuildRoot 'ReceiverQA.exe'
$sources = @((Join-Path $project 'UsbReceiver.cs'),(Join-Path $project 'UsbDiagnostics.cs'),(Join-Path $project 'WirelessReceiver.cs'),(Join-Path $project 'NativeSnapshot.cs'),(Join-Path $PSScriptRoot 'ReceiverQA.cs'))
& $compile -Name ReceiverQA -Sources $sources -ExtraRefs $support -Out $test
& $test $runtime
if ($LASTEXITCODE) { throw "Receiver integration failed with $LASTEXITCODE" }
if ($ApplicationExe) {
    $guiTest = Join-Path $BuildRoot 'GuiQA.exe'
    & $compile -Name GuiQA -Sources (Join-Path $PSScriptRoot GuiQA.cs) -ExtraRefs $support -Out $guiTest
    & $guiTest $runtime ([IO.Path]::GetFullPath($ApplicationExe))
    if ($LASTEXITCODE) { throw "Application GUI integration failed with $LASTEXITCODE" }
    $airHostTest = Join-Path $BuildRoot 'AirHostQA.exe'
    & $compile -Name AirHostQA -Sources (Join-Path $PSScriptRoot 'AirHostQA.cs') -Out $airHostTest
    $sdkFixture = Join-Path $BuildRoot 'AirFixture.dll'
    & $asm /QUIET /DLL /X64 /PE64 "/OUTPUT:$sdkFixture" (Join-Path $PSScriptRoot 'SdkExports.il')
    if ($LASTEXITCODE) { throw "SDK fixture assembly failed with $LASTEXITCODE" }
    foreach ($scenario in 'normal','no-key','pipe-loss','1080p120','2k60') {
        & $airHostTest ([IO.Path]::GetFullPath($ApplicationExe)) $sdkFixture $scenario
        if ($LASTEXITCODE) { throw "AirPlay host integration failed for $scenario with $LASTEXITCODE" }
    }
}

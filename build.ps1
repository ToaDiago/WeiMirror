# Builds the x64 Windows desktop executable, including its complete source archive.
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$outDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csharp = Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'
if (!(Test-Path $csharp)) { throw 'Run this script in PowerShell 7 for Windows x64 (pwsh).' }
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path $csharp
$files = @(Get-ChildItem -LiteralPath $root -File -Force | Sort-Object Name)
$testFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -File -Recurse | Sort-Object FullName)
$archive = Join-Path $outDir 'WeiMirror.Source.zip'
$zipStream = [IO.File]::Create($archive)
try {
    $zip = [IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach ($file in ($files + $testFiles)) {
            $relativePath = [IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
            $entry = $zip.CreateEntry($relativePath,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $inputStream = $file.OpenRead()
            $entryStream = $entry.Open()
            try { $inputStream.CopyTo($entryStream) } finally { $inputStream.Dispose(); $entryStream.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $zipStream.Dispose() }
$trees = [Microsoft.CodeAnalysis.SyntaxTree[]]@($files | Where-Object Extension -eq '.cs' | ForEach-Object {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName))
})
$refs = [Microsoft.CodeAnalysis.MetadataReference[]]@('mscorlib','System','System.Core','System.Windows.Forms','System.Drawing','System.IO.Compression','System.IO.Compression.FileSystem','System.Net.Http' | ForEach-Object {
    [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $framework ($_+'.dll')))
})
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication).WithPlatform([Microsoft.CodeAnalysis.Platform]::X64).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('WeiMirror',$trees,$refs,$options)
# A managed provider is invoked by Roslyn during emission, including outside the PowerShell pipeline.
if (!('WeiMirrorBuild.FileResource' -as [type])) {
    Add-Type -TypeDefinition 'namespace WeiMirrorBuild { public class FileResource { private readonly string path; public FileResource(string file) { path=file; } public System.IO.Stream Open() { return System.IO.File.OpenRead(path); } } }'
}
$resources = [Collections.Generic.List[Microsoft.CodeAnalysis.ResourceDescription]]::new()
foreach ($pair in @(@('WeiMirror.Source.zip',$archive),@('WeiMirror.ico',(Join-Path $root 'WeiMirror.ico')),@('WeiMirror.LatencyTest.html',(Join-Path $root 'LatencyTest.html')))) {
    $provider = [WeiMirrorBuild.FileResource]::new($pair[1])
    $resources.Add([Microsoft.CodeAnalysis.ResourceDescription]::new($pair[0],[Func[IO.Stream]]$provider.Open,$true))
}
$manifest = [IO.File]::OpenRead((Join-Path $root 'manifest.xml'))
$icon = [IO.File]::OpenRead((Join-Path $root 'WeiMirror.ico'))
$win = $comp.CreateDefaultWin32Resources($true,$false,$manifest,$icon)
$target = Join-Path $outDir 'WeiMirror.exe'
$temp = Join-Path $outDir ([Guid]::NewGuid().ToString('N')+'.tmp')
try {
    $output = [IO.File]::Create($temp)
    try {
        $result = $comp.Emit($output,$null,$null,$win,$resources)
        if (!$result.Success) { throw ($result.Diagnostics -join [Environment]::NewLine) }
    } finally { $output.Dispose() }
    [IO.File]::Move($temp,$target,$true)
} finally {
    $win.Dispose(); $icon.Dispose(); $manifest.Dispose()
    if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
}
Write-Output $target

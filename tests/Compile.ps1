param([string]$Name, [string[]]$Sources, [string]$Out, [switch]$Library, [string[]]$ExtraRefs = @(), [string]$MainTypeName)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME Microsoft.CodeAnalysis.dll)
Add-Type -Path (Join-Path $PSHOME Microsoft.CodeAnalysis.CSharp.dll)
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = [Microsoft.CodeAnalysis.MetadataReference[]]@(
    @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $fx $_)) }
    $ExtraRefs | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile([IO.Path]::GetFullPath($_)) }
)
$trees = [Microsoft.CodeAnalysis.SyntaxTree[]]@($Sources | ForEach-Object {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText([IO.Path]::GetFullPath($_)))
})
$kind = if ($Library) { [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary } else { [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication }
$opts = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($kind).WithPlatform([Microsoft.CodeAnalysis.Platform]::X64)
if ($MainTypeName) { $opts = $opts.WithMainTypeName($MainTypeName) }
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($Name,$trees,$refs,$opts)
$file = [IO.File]::Create([IO.Path]::GetFullPath($Out))
try {
    $result = $compilation.Emit($file)
    if (!$result.Success) { throw ($result.Diagnostics -join [Environment]::NewLine) }
} finally { $file.Dispose() }

param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'dist\VCUT-win-x64'
}

$projectRoot = [IO.Path]::GetFullPath($projectRoot)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$allowedPrefix = $projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$outputPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'La carpeta de publicacion debe estar dentro del proyecto VCUT.'
}

Push-Location $projectRoot
try {
    & dotnet build 'VCUT.slnx' -c Release --disable-build-servers -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'La compilacion o las pruebas no han terminado correctamente.' }

    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }
    [IO.Directory]::CreateDirectory($outputPath) | Out-Null

    & dotnet publish 'VCUT.vbproj' -c Release -p:PublishProfile=Portable-win-x64 -p:RunTestsOnBuild=false -p:DebugType=None -p:DebugSymbols=false -o $outputPath --disable-build-servers -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'No se ha podido crear la publicacion portable.' }

    $files = @(Get-ChildItem -LiteralPath $outputPath -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'VCUT.exe') {
        throw 'La publicacion no contiene unicamente VCUT.exe.'
    }

    $hash = Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256
    Write-Host ''
    Write-Host ('Portable: {0}' -f $files[0].FullName)
    Write-Host ('Tamano:   {0:N1} MB' -f ($files[0].Length / 1MB))
    Write-Host ('SHA256:   {0}' -f $hash.Hash)
}
finally {
    Pop-Location
}

param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $projectRoot 'tools\ffmpeg'
$downloadRoot = Join-Path $projectRoot 'artifacts\downloads'
New-Item -ItemType Directory -Force -Path $destination, $downloadRoot | Out-Null
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$archive = Join-Path $downloadRoot 'ffmpeg-essentials.zip'
$expectedHash = ((Invoke-WebRequest -UseBasicParsing -Uri "$url.sha256").Content.Trim() -split '\s+')[0]
if ($expectedHash -notmatch '^[a-fA-F0-9]{64}$') { throw 'El proveedor no devolvio un SHA256 valido.' }
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    Write-Host 'Descargando FFmpeg desde gyan.dev (proveedor enlazado por ffmpeg.org)...'
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'El SHA256 de FFmpeg no coincide.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($name in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE', 'README.txt')) {
        $entry = $zip.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
        if (!$entry) { throw "No se encontro $name en el archivo verificado." }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $name), $true)
    }
} finally { $zip.Dispose() }
@("Source: $url", "SHA256: $expectedHash", "Downloaded: $([DateTime]::UtcNow.ToString('o'))") | Set-Content -LiteralPath (Join-Path $destination 'download-info.txt') -Encoding UTF8
$versionOutput = & (Join-Path $destination 'ffmpeg.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg no se puede ejecutar.' }
$versionOutput | Select-Object -First 1
Write-Host 'FFmpeg y FFprobe preparados en tools\ffmpeg.'

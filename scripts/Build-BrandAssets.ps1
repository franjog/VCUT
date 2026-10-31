param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'Assets\vcut-source.png'
$mark = Join-Path $projectRoot 'Assets\vcut-mark.png'
$icon = Join-Path $projectRoot 'Assets\vcut.ico'
$ffmpeg = Join-Path $projectRoot 'tools\ffmpeg\ffmpeg.exe'

if (!(Test-Path -LiteralPath $source)) { throw "No se encuentra $source" }
if (!(Test-Path -LiteralPath $ffmpeg)) { throw 'Ejecuta scripts\Setup-FFmpeg.ps1 antes de generar los recursos.' }

$temporary = Join-Path ([IO.Path]::GetTempPath()) ("VCUT.Brand." + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null

try {
    & $ffmpeg -v error -y -i $source -vf 'scale=480:480:force_original_aspect_ratio=decrease,pad=512:512:(ow-iw)/2:(oh-ih)/2:color=0x00000000' -frames:v 1 $mark
    if ($LASTEXITCODE -ne 0) { throw 'No se ha podido crear la marca para la interfaz.' }

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $images = @()
    foreach ($size in $sizes) {
        $png = Join-Path $temporary ("icon-$size.png")
        & $ffmpeg -v error -y -i $source -vf "scale=$($size - 2):$($size - 2):force_original_aspect_ratio=decrease,pad=$size`:$size`:(ow-iw)/2`:(oh-ih)/2`:color=0x00000000" -frames:v 1 $png
        if ($LASTEXITCODE -ne 0) { throw "No se ha podido crear el icono de $size px." }
        $images += ,([IO.File]::ReadAllBytes($png))
    }

    $stream = [IO.File]::Create($icon)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $encodedSize = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([Byte]$encodedSize)
            $writer.Write([Byte]$encodedSize)
            $writer.Write([Byte]0)
            $writer.Write([Byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$images[$index].Length)
            $writer.Write([UInt32]$offset)
            $offset += $images[$index].Length
        }
        foreach ($image in $images) { $writer.Write($image) }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

Write-Host "Marca: $mark"
Write-Host "Icono: $icon"

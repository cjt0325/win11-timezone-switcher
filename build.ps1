param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'), [switch]$Tests, [switch]$IntegrationTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$assetDirectory = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$iconPath = Join-Path $assetDirectory 'app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    Add-Type -AssemblyName System.Drawing
    $pictures = @()
    foreach ($size in @(16, 32, 48, 64)) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(44, 98, 217))
        $graphics.FillEllipse($brush, [single]0, [single]0, [single]($size - 1), [single]($size - 1))
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]($size * 0.06))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $graphics.DrawEllipse($pen, [single]($size * 0.21), [single]($size * 0.21), [single]($size * 0.57), [single]($size * 0.57))
        $graphics.DrawLine($pen, [single]($size * 0.5), [single]($size * 0.33), [single]($size * 0.5), [single]($size * 0.51))
        $graphics.DrawLine($pen, [single]($size * 0.5), [single]($size * 0.51), [single]($size * 0.64), [single]($size * 0.58))
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pictures += ,($stream.ToArray())
        $stream.Dispose(); $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    $iconStream = [System.IO.File]::Create($iconPath)
    $writer = New-Object System.IO.BinaryWriter($iconStream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]4)
        $offset = 6 + 16 * 4
        $index = 0
        foreach ($size in @(16, 32, 48, 64)) {
            $writer.Write([byte]$size); $writer.Write([byte]$size); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$pictures[$index].Length); $writer.Write([uint32]$offset)
            $offset += $pictures[$index].Length; $index++
        }
        foreach ($picture in $pictures) { $writer.Write([byte[]]$picture) }
    } finally { $writer.Dispose(); $iconStream.Dispose() }
}
$common = @('/nologo', '/codepage:65001', '/optimize+', '/platform:x64', '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll')
$exe = Join-Path $OutputDirectory 'TimeZoneSwitcher.exe'
$sources = @('Core.cs', 'Program.cs', 'MainForm.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler @common '/target:winexe' "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" "/win32icon:$iconPath" "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination "$exe.config" -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.txt') -Destination (Join-Path $OutputDirectory '使用说明.txt') -Force
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $OutputDirectory 'LICENSE') -Force
}
Write-Output "Built: $exe"
if ($Tests) {
    $testExe = Join-Path $OutputDirectory 'TimeZoneSwitcher.Tests.exe'
    & $compiler @common '/target:exe' "/out:$testExe" (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $uiTestExe = Join-Path $OutputDirectory 'TimeZoneSwitcher.UiTests.exe'
    & $compiler @common '/target:exe' '/main:UiTests' "/out:$uiTestExe" @sources (Join-Path $PSScriptRoot 'tests\CoreTests.cs') (Join-Path $PSScriptRoot 'tests\UiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination "$uiTestExe.config" -Force
    & $uiTestExe $exe $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
}
if ($IntegrationTests) {
    $integrationExe = Join-Path $OutputDirectory 'TimeZoneSwitcher.IntegrationTests.exe'
    & $compiler @common '/target:exe' "/out:$integrationExe" (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'tests\IntegrationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Integration test build failed.' }
    & $integrationExe
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed; check restoration output.' }
}

$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'NewAge2D.csproj'
$plugins = 'C:\Program Files (x86)\Steam\steamapps\common\New Age\BepInEx\plugins\NewAge2D'

Write-Host 'Собираю NewAge2D…' -ForegroundColor Cyan
dotnet build $proj -c Debug -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }

New-Item -ItemType Directory -Force $plugins | Out-Null
$bin = Join-Path $PSScriptRoot 'bin\Debug'
foreach ($name in 'NewAge2D.dll', 'NewAge.Swf.dll', 'NewAge.Swf.Skia.dll', 'SkiaSharp.dll', 'libSkiaSharp.dll') {
    $src = Join-Path $bin $name
    $dst = Join-Path $plugins $name
    try {
        Copy-Item $src $dst -Force
    } catch {
        Rename-Item $dst "$dst.old_$(Get-Date -Format 'MMdd_HHmm')" -Force
        Copy-Item $src $dst -Force
    }
    Write-Host "  → $name" -ForegroundColor Green
}
Get-ChildItem $plugins -Filter '*.old_*' -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-Item $_.FullName -Force } catch { }
}

Write-Host "`nГотово. Перезапусти клиент." -ForegroundColor Cyan
Get-ChildItem $plugins -Filter '*.dll' | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

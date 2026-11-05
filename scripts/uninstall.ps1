param([switch]$RestoreHandy)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'OpenAIDictation'
$app = Join-Path $base 'app'
$exe = Join-Path $app 'OpenAIDictation.exe'
$running = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe })
if ($running.Count -gt 0) {
    Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -Wait
    foreach ($item in $running) {
        $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($process -and -not $process.WaitForExit(15000)) { throw 'Close OpenAI Dictation before uninstalling so audio can be restored.' }
    }
}
$startup = [Environment]::GetFolderPath('Startup')
$link = Join-Path $startup 'OpenAI Dictation.lnk'
if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link }
if (Test-Path -LiteralPath $app) {
    $resolved = (Resolve-Path -LiteralPath $app).Path
    if ($resolved -ne [IO.Path]::GetFullPath((Join-Path $base 'app')) -or (Get-Item -LiteralPath $app).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected application installation path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if ($RestoreHandy) {
    $backup = Join-Path $base 'rollback\Handy Local.lnk'
    if (Test-Path -LiteralPath $backup) { Copy-Item -LiteralPath $backup -Destination (Join-Path $startup 'Handy Local.lnk') -Force }
}
Write-Output 'Application removed. Encrypted credentials and settings were preserved.'


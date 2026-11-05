param([switch]$ReplaceHandy)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repo 'artifacts\publish'
$binary = Join-Path $publish 'OpenAIDictation.exe'
if (-not (Test-Path -LiteralPath $binary)) {
    & dotnet publish (Join-Path $repo 'src\OpenAIDictation.Windows') -c Release -r win-x64 --self-contained false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; installation was not changed.' }
}
$base = Join-Path $env:LOCALAPPDATA 'OpenAIDictation'
$app = Join-Path $base 'app'
$startup = [Environment]::GetFolderPath('Startup')
$exe = Join-Path $app 'OpenAIDictation.exe'
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe } | ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
if ($ReplaceHandy) {
    $handy = Join-Path $env:LOCALAPPDATA 'Handy'
    $helper = Join-Path $handy 'shortcut-helper\AutoHotkey64.exe'
    Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -eq (Join-Path $handy 'handy.exe') -or
        ($_.ExecutablePath -eq $helper -and $_.CommandLine -like '*WinH.ahk*') -or
        ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($handy + '\', [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($_.ExecutablePath) -eq 'llama-server.exe')
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
    $oldLink = Join-Path $startup 'Handy Local.lnk'
    if (Test-Path -LiteralPath $oldLink) {
        $rollback = Join-Path $base 'rollback'
        New-Item -ItemType Directory -Path $rollback -Force | Out-Null
        $backup = Join-Path $rollback 'Handy Local.lnk'
        if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $oldLink -Destination $backup }
        Remove-Item -LiteralPath $oldLink
    }
}
New-Item -ItemType Directory -Path $base -Force | Out-Null
if (Test-Path -LiteralPath $app) {
    $resolved = (Resolve-Path -LiteralPath $app).Path
    if ($resolved -ne [IO.Path]::GetFullPath((Join-Path $base 'app')) -or (Get-Item -LiteralPath $app).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected application installation path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $app -Force | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $app -Recurse
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $startup 'OpenAI Dictation.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $app
$link.Description = 'Win+H OpenAI dictation'
$link.Save()
Start-Process -FilePath $exe -WorkingDirectory $app -WindowStyle Hidden
Write-Output "Installed OpenAI Dictation to $app"


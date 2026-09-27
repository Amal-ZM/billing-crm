$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }
$evt = $raw | ConvertFrom-Json
$path = [string]$evt.tool_input.file_path
if (-not $path -or -not (Test-Path $path)) { exit 0 }
if ($path -match '\.cs$') {
  dotnet format --include $path 2>$null | Out-Null
}
exit 0
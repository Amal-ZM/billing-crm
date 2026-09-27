$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }
$evt = $raw | ConvertFrom-Json
$path = ""
$content = ""
if ($evt.tool_input.file_path) { $path = [string]$evt.tool_input.file_path }
if ($evt.tool_input.content) { $content = [string]$evt.tool_input.content }
$check = "$path $content"
$hit = $false
if ($path -match 'appsettings.*\.json$' -and $content -match '(Password|ClientSecret|ApiKey)\s*=\s*[^"]+') { $hit = $true }
if ($path -match '\.(env|pfx)$') { $hit = $true }
if ($content -match 'Server=.*;.*Password=') { $hit = $true }
if ($hit) {
  Write-Output "BLOCKED: possible secret in $path. Use user-secrets or env vars."
  exit 2
}
exit 0
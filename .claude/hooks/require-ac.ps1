$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }
$evt = $raw | ConvertFrom-Json
$path = [string]$evt.tool_input.file_path
if ($path -notmatch 'spec\.md$') { exit 0 }
$txt = [string]$evt.tool_input.content
if ($txt -notmatch 'Given' -or $txt -notmatch 'When' -or $txt -notmatch 'Then') {
  Write-Output "SPEC REJECTED: $path must include Given / When / Then acceptance criteria."
  exit 2
}
exit 0
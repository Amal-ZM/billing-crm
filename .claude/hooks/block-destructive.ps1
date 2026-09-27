$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }
$evt = $raw | ConvertFrom-Json
$cmd = [string]$evt.tool_input.command
if ($cmd -match 'ef database drop|DROP DATABASE|git push --force|git reset --hard|Format-Volume|Remove-Item -Recurse') {
  Write-Output "BLOCKED destructive command: $cmd"
  exit 2
}
exit 0
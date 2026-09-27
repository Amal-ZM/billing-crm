$ErrorActionPreference = "SilentlyContinue"
$root = $env:CLAUDE_PROJECT_DIR
if (-not $root) { $root = Get-Location }
$featureFile = Join-Path $root ".specify/feature.json"
$msg = "BillingCRM: work one spec at a time."
if (Test-Path $featureFile) {
  $j = Get-Content $featureFile -Raw | ConvertFrom-Json
  $msg = "Active spec: $($j.feature_directory). Do not start a later spec. Invoke matching skills. Run the app before claiming done."
}
Write-Output $msg
exit 0
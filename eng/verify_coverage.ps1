param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [double] $MinimumLine = 20,
    [double] $MinimumBranch = 30
)

$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter 'coverage.cobertura.xml' -File -Recurse)
if ($reports.Count -eq 0) {
    throw "No Cobertura report found under '$ResultsDirectory'."
}
$report = $reports | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
Write-Host "Checking $($report.FullName)"

[xml] $coverage = Get-Content -LiteralPath $report.FullName -Raw
$line = [double]::Parse($coverage.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture) * 100
$branch = [double]::Parse($coverage.coverage.'branch-rate', [Globalization.CultureInfo]::InvariantCulture) * 100
Write-Host ("Coverage: lines {0:N2}% (minimum {1:N2}%), branches {2:N2}% (minimum {3:N2}%)." -f $line, $MinimumLine, $branch, $MinimumBranch)

if ($line -lt $MinimumLine -or $branch -lt $MinimumBranch) {
    throw 'Coverage is below the configured minimum.'
}

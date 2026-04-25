[CmdletBinding()]
param(
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [int]$Days = 7
)

$ErrorActionPreference = "Stop"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
}

$since = (Get-Date).AddDays(-1 * $Days).ToString("yyyy-MM-dd")
$search = "merged:>=$since"
$raw = & $gh.Source pr list --repo $Repository --state merged --search $search --limit 100 --json number,title,url,mergedAt,files,closingIssuesReferences
if ($LASTEXITCODE -ne 0) {
    throw "gh pr list failed."
}

$prs = $raw | ConvertFrom-Json

Write-Host "# Taskdeck Docs Rehydration Checklist"
Write-Host ""
Write-Host "Repository: $Repository"
Write-Host "Merged since: $since"
Write-Host ""
Write-Host "## PRs To Reconcile"

foreach ($pr in $prs) {
    $files = @($pr.files | ForEach-Object { $_.path })
    $issues = @($pr.closingIssuesReferences | ForEach-Object { "#$($_.number)" })
    Write-Host "- [ ] PR #$($pr.number): $($pr.title)"
    Write-Host "  URL: $($pr.url)"
    Write-Host "  Issues: $($issues -join ', ')"
    Write-Host "  Touched docs: $(($files | Where-Object { $_ -like 'docs/*' -or $_ -eq 'AGENTS.md' }) -join ', ')"
}

Write-Host ""
Write-Host "## Rehydrate"
Write-Host "- [ ] Update docs/STATUS.md if shipped reality changed."
Write-Host "- [ ] Update docs/IMPLEMENTATION_MASTERPLAN.md if sequencing or delivery history changed."
Write-Host "- [ ] Update docs/TESTING_GUIDE.md if verification posture, command expectations, or test totals changed."
Write-Host "- [ ] Update manual/product docs if user-visible behavior changed."
Write-Host "- [ ] Seed follow-up issues for any accepted deferred work."

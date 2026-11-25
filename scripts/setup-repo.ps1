<#
  One-time GitHub repository setup. Run as the repository OWNER (admin rights required):
      gh auth login
      ./scripts/setup-repo.ps1
  Rulesets on private repositories need a paid plan (Pro/Team) or a public repository.
#>
param([string]$Repo = "greenyarik0505-jpg/windows-tools")

$ErrorActionPreference = "Stop"

Write-Host "Merge settings: squash only, PR title as commit, auto-delete branches"
gh api --method PATCH "repos/$Repo" `
  -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false `
  -F delete_branch_on_merge=true -F allow_auto_merge=true `
  -f squash_merge_commit_title=PR_TITLE -f squash_merge_commit_message=PR_BODY | Out-Null

Write-Host "Actions: allow release-please to open pull requests"
gh api --method PUT "repos/$Repo/actions/permissions/workflow" `
  -f default_workflow_permissions=read -F can_approve_pull_request_reviews=true | Out-Null

Write-Host "Labels"
$labels = @{
  "feature" = "1f883d"; "bug" = "d73a4a"; "core-api" = "5319e7"
  "mod:audio" = "0e8a16"; "mod:monitor" = "0e8a16"; "mod:switcher" = "0e8a16"; "mod:snap" = "0e8a16"
  "mod:clipboard" = "0e8a16"; "mod:capture" = "0e8a16"; "mod:island" = "0e8a16"
  "mod:cleaner" = "0e8a16"; "mod:tools" = "0e8a16"; "mod:core" = "0e8a16"
}
foreach ($l in $labels.GetEnumerator()) {
  gh label create $l.Key --color $l.Value --repo $Repo --force | Out-Null
}

Write-Host "Ruleset: protect main"
gh api --method POST "repos/$Repo/rulesets" --input "$PSScriptRoot/../.github/rulesets/main.json" | Out-Null

Write-Host "Done. Remaining manual step: add the RELEASE_PLEASE_TOKEN secret (see docs/REPO_SETUP.md)."

param(
    [Parameter(Position=0)][ValidateSet("run","status","help","gui-smoke")][string]$Command="run",
    [string]$ProjectName="",
    [string]$Request=""
)

$ErrorActionPreference = "Stop"
$Root = "D:\AITeam"
. (Join-Path $Root "app\AITeam-Core.ps1")

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
. (Join-Path $Root "app\AITeam-ProjectManager.ps1")
Initialize-AITeamSessionAvailability
Initialize-AITeamAvailability
Repair-AITeamProjectRegistrations

function Get-AITeamCandidateOrder {
    param([string[]]$Preferred,[string[]]$Avoid=@())
    $first = @($Preferred | Where-Object { $Avoid -notcontains $_ })
    $last = @($Preferred | Where-Object { $Avoid -contains $_ })
    return @($first + $last | Select-Object -Unique)
}

$script:EngineeringAgents = @()
function Register-EngineeringAgent {
    param([string]$AgentId)
    if (-not [string]::IsNullOrWhiteSpace($AgentId) -and $script:EngineeringAgents -notcontains $AgentId) {
        $script:EngineeringAgents += $AgentId
    }
}

$script:MainForm = $null
$script:ProgressLabel = $null
$script:ProgressBox = $null
$script:RequestBox = $null
$script:ProjectCombo = $null
$script:ProjectInfo = $null
$script:ManageProjectsButton = $null
$script:StartButton = $null
$script:NewTaskButton = $null
$script:RetryButton = $null
$script:CodexCheck = $null
$script:ClaudeCheck = $null
$script:AgyCheck = $null
$script:TaskRunning = $false
$script:HealthChecking = $false

function Get-AITeamStatusText {
    Initialize-AITeamAvailability
    $rows = @()
    foreach ($pair in @(
        @{Id="codex"; Command="codex"},
        @{Id="claude"; Command="claude"},
        @{Id="antigravity"; Command="agy"}
    )) {
        $cmd = Get-Command $pair.Command -ErrorAction SilentlyContinue
        $toolState = if ($null -eq $cmd) { "MISSING" } else { "OK" }
        $rows += ("[{0}] {1} | AI={2}" -f $toolState,(Get-AITeamAgentFriendlyName $pair.Id),(Get-AITeamAgentStateLabel $pair.Id))
    }

    $data = Read-AITeamJson "config\projects.json"
    foreach ($p in @($data.projects | Where-Object { $_.active -ne $false })) {
        $physical = Join-Path ([string]$p.repo_path) ([string]$p.repo_subpath)
        $state = if ((Test-Path -LiteralPath $p.repo_path) -and (Test-Path -LiteralPath $physical)) { "OK" } else { "ERROR" }
        $rows += ("[{0}] {1} -> {2}" -f $state,$p.name,$physical)
    }
    return ($rows -join [Environment]::NewLine)
}

function Start-AITeamProgress {
    param([string]$Project)
    if ($null -ne $script:ProgressLabel) { $script:ProgressLabel.Text = "準備中..." }
    if ($null -ne $script:ProgressBox) {
        $script:ProgressBox.Clear()
        $script:ProgressBox.AppendText("AITeam - " + $Project + [Environment]::NewLine + [Environment]::NewLine)
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function Set-AITeamProgress {
    param([string]$Stage,[string]$Detail="")
    if ($null -ne $script:ProgressLabel) { $script:ProgressLabel.Text = $Stage }
    if ($null -ne $script:ProgressBox) {
        $line = "[" + (Get-Date -Format "HH:mm:ss") + "] " + $Stage
        if (-not [string]::IsNullOrWhiteSpace($Detail)) { $line += " - " + $Detail }
        $script:ProgressBox.AppendText($line + [Environment]::NewLine)
        $script:ProgressBox.SelectionStart = $script:ProgressBox.TextLength
        $script:ProgressBox.ScrollToCaret()
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function Close-AITeamProgress { }

function Show-AITeamResult {
    param([string]$Title,[string]$Text,[switch]$ErrorResult)
    if ($null -ne $script:ProgressLabel) {
        $script:ProgressLabel.Text = if ($ErrorResult) { "AITeam 執行失敗" } else { "AITeam 完成" }
    }
    if ($null -ne $script:ProgressBox) {
        $script:ProgressBox.AppendText([Environment]::NewLine + ("=" * 70) + [Environment]::NewLine)
        $script:ProgressBox.AppendText($Text + [Environment]::NewLine)
        $script:ProgressBox.SelectionStart = $script:ProgressBox.TextLength
        $script:ProgressBox.ScrollToCaret()
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function Refresh-AITeamProjectCombo {
    param([string]$Preferred="")
    $combo = $script:ProjectCombo
    if ($null -eq $combo) { return }
    $combo.Items.Clear()
    $names = @(Get-AITeamProjectNames)
    foreach ($n in $names) { [void]$combo.Items.Add($n) }
    if ($names.Count -gt 0) {
        $idx = -1
        if (-not [string]::IsNullOrWhiteSpace($Preferred)) { $idx = $combo.Items.IndexOf($Preferred) }
        if ($idx -lt 0) { $idx = 0 }
        $combo.SelectedIndex = $idx
    }
}

function Refresh-AITeamSelectedProjectInfo {
    if ($null -eq $script:ProjectInfo -or $null -eq $script:ProjectCombo) { return }
    if ($null -eq $script:ProjectCombo.SelectedItem) {
        $script:ProjectInfo.Text = "尚未登錄專案。請按『管理專案』新增。"
        return
    }
    try {
        $p = Resolve-AITeamProject ([string]$script:ProjectCombo.SelectedItem)
        $sub = if ([string]::IsNullOrWhiteSpace([string]$p.repo_subpath)) { "Repo 根目錄" } else { [string]$p.repo_subpath }
        $vf = if ($null -ne $p.PSObject.Properties['version_file'] -and -not [string]::IsNullOrWhiteSpace([string]$p.version_file)) { [string]$p.version_file } else { "未設定" }
        $script:ProjectInfo.Text = "Repo：$($p.repo_name)`r`n子目錄：$sub`r`n版本檔：$vf"
    } catch { $script:ProjectInfo.Text = $_.Exception.Message }
}

function Refresh-AITeamAgentControls {
    if ($null -eq $script:CodexCheck) { return }
    $script:CodexCheck.Checked = Test-AITeamSessionAgentEnabled "codex"
    $script:CodexCheck.Text = "GPT / Codex — " + (Get-AITeamAgentStateLabel "codex")
    $script:ClaudeCheck.Checked = Test-AITeamSessionAgentEnabled "claude"
    $script:ClaudeCheck.Text = "Claude — " + (Get-AITeamAgentStateLabel "claude")
    $script:AgyCheck.Checked = Test-AITeamSessionAgentEnabled "antigravity"
    $script:AgyCheck.Text = "Gemini / Antigravity — " + (Get-AITeamAgentStateLabel "antigravity")
}

function Set-AITeamTaskControlsEnabled {
    param([bool]$Enabled)
    foreach ($c in @(
        $script:ProjectCombo,$script:ManageProjectsButton,$script:RequestBox,
        $script:RetryButton,$script:CodexCheck,$script:ClaudeCheck,$script:AgyCheck,
        $script:StartButton,$script:NewTaskButton
    )) {
        if ($null -ne $c) { $c.Enabled = $Enabled }
    }
}

function Invoke-AITeamTask {
    param([Parameter(Mandatory=$true)][string]$ProjectName,[Parameter(Mandatory=$true)][string]$Request)
    $script:EngineeringAgents = @()
$settings = Read-AITeamJson "config\settings.json"
$agents = Read-AITeamJson "config\agents.json"
$project = Resolve-AITeamProject $ProjectName

$repo = [string]$project.repo_path
$subpath = ([string]$project.repo_subpath -replace '\\','/').Trim('/')
$physicalProject = Join-Path $repo $subpath
$defaultBranch = [string]$project.default_branch
$tasksRoot = [string]$settings.paths.tasks_root
$worktreesRoot = [string]$settings.paths.worktrees_root
$logsRoot = [string]$settings.paths.logs_root
$maxRepairs = [int]$settings.workflow.max_repair_rounds

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$taskId = $stamp + "-" + $ProjectName.ToLowerInvariant()
$taskDir = Join-Path $tasksRoot $taskId
$worktreeRoot = Join-Path $worktreesRoot $taskId
$workProject = Join-Path $worktreeRoot $subpath
$branch = "aitask/" + $ProjectName.ToLowerInvariant() + "/" + $stamp

New-Item -ItemType Directory -Path $taskDir -Force | Out-Null
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
Write-Utf8NoBom (Join-Path $taskDir "REQUEST.md") (
    "# REQUEST`r`n`r`n" + $Request.Trim() + "`r`n"
)

$version = ""
$finalCommit = ""
$baseCommit = ""
$currentVersion = ""
$nextVersion = ""
$scoutAgent = ""
$planAgent = ""
$implementerAgent = ""
$challengeAgent = ""
$finalAgent = ""
$repairAgent = ""

Start-AITeamProgress -Project $ProjectName

try {
    # 1. Intent only: use the first available AI; no repo inspection is needed.
    Set-AITeamProgress "1/6 判斷需求" "先判斷查詢/分析或修改；不可用 AI 會自動跳過"

    $routerPrompt = @"
You are the intent router for AITeam. Your provider identity is irrelevant to this routing decision.

Decide ONLY from the user's wording below.
Do NOT inspect the repository.
Do NOT use shell commands, filesystem tools, Git, project files, profiles, or external tools.
Do NOT solve the task yet.

PROJECT:
$ProjectName

USER REQUEST:
$Request

Classify:
- inquiry = inspect/explain/summarize/compare/diagnose/report/check current version/content/status, without explicit permission to change source.
- change = user explicitly asks to modify/fix/add/remove/implement/refactor/redesign or otherwise change the program/source/configuration.

If ambiguous, classify as inquiry.

Output exactly two lines:
AITeam-Intent: inquiry
Reason: <brief Traditional Chinese reason>

or

AITeam-Intent: change
Reason: <brief Traditional Chinese reason>
"@

    $routerPromptPath = Join-Path $taskDir "ROUTER_PROMPT.md"
    $routerResultPath = Join-Path $taskDir "ROUTER_RESULT.md"
    Write-Utf8NoBom $routerPromptPath $routerPrompt

    $routerRun = Invoke-AITeamRoleWithFallback `
        -Role "intent_router" `
        -Candidates @("codex","antigravity","claude") `
        -Mode "readonly" `
        -WorkingDirectory $physicalProject `
        -PromptPath $routerPromptPath `
        -OutputPath $routerResultPath `
        -TaskDir $taskDir `
        -AgentsConfig $agents `
        -ReasoningEffort "low" `
        -WorktreeRoot $repo `
        -RepoSubpath $subpath `
        -EnsureReadOnly `
        -ValidationPattern '(?im)^\s*AITeam-Intent\s*:\s*(inquiry|change)\s*$' `
        -ProgressScript { param($m) Set-AITeamProgress "1/6 判斷需求" $m }

    $routerResult = Read-Utf8Text $routerResultPath
    $intent = (Get-PlanMarker $routerResult "AITeam-Intent" "").ToLowerInvariant()

    if ($intent -notin @("inquiry","change")) {
        throw "Intent router did not return inquiry/change. No project files were changed."
    }

    if ($intent -eq "inquiry") {
        # Now, and only now, GPT may inspect the repo.
        Set-AITeamProgress "2/2 AI 查詢與分析" "依可用狀態選擇 GPT / Gemini / Claude"

        $inquiryPrompt = @"
You are the active AITeam inquiry agent. Answer from the current repository regardless of provider identity.

Project: $ProjectName
Physical project path: $physicalProject

USER REQUEST:
$Request

This is READ-ONLY.
Now autonomously inspect the current repository only as needed to answer.
Prefer rg/git grep and focused reads; inspect more broadly only when genuinely needed.
Do not edit files, create commits, push, merge, tag, deploy, release, or read secrets,
runtime customer data, Data/Cache/Logs, or unrelated user files.

Answer directly in Traditional Chinese.
Ground factual claims in the current repository you actually inspected.
"@

        $inquiryPromptPath = Join-Path $taskDir "INQUIRY_PROMPT.md"
        $inquiryResultPath = Join-Path $taskDir "INQUIRY_RESULT.md"
        Write-Utf8NoBom $inquiryPromptPath $inquiryPrompt

        $inquiryRun = Invoke-AITeamRoleWithFallback `
            -Role "inquiry" `
            -Candidates @("codex","antigravity","claude") `
            -Mode "readonly" `
            -WorkingDirectory $physicalProject `
            -PromptPath $inquiryPromptPath `
            -OutputPath $inquiryResultPath `
            -TaskDir $taskDir `
            -AgentsConfig $agents `
            -ReasoningEffort ([string]$agents.agents.codex.planner_reasoning_effort) `
            -WorktreeRoot $repo `
            -RepoSubpath $subpath `
            -EnsureReadOnly `
            -ProgressScript { param($m) Set-AITeamProgress "2/2 AI 查詢與分析" $m }

        $answer = Read-Utf8Text $inquiryResultPath
        $result = "AITeam 查詢完成`r`n專案：$ProjectName`r`n回答 AI：$($inquiryRun.FriendlyName)`r`n`r`n" + $answer
        Write-Utf8NoBom (Join-Path $Root "last_result.txt") $result

        if ($settings.workflow.keep_successful_task_artifacts -ne $true) {
            Remove-Item -LiteralPath $taskDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        Show-AITeamResult -Title ("AITeam - " + $ProjectName) -Text $result
        return
    }

    # Change only from here.
    # Every change task follows the SAME three-AI pipeline.
    # Risk changes review depth only; it does not skip a model.
    Set-AITeamProgress "2/7 Git 正式來源確認" "確認 main 並同步 GitHub"

    $dirty = Get-GitOutput -RepoPath $repo -Arguments @("status","--porcelain")
    if (-not [string]::IsNullOrWhiteSpace($dirty.Text)) {
        throw "正式 Git repo 有尚未提交的變更。AITeam 為避免覆蓋資料，本次停止。`r`n$($dirty.Text)"
    }

    $branchNow = (Get-GitOutput -RepoPath $repo -Arguments @("branch","--show-current")).Text.Trim()
    if ($branchNow -ne $defaultBranch) {
        [void](Get-GitOutput -RepoPath $repo -Arguments @("switch",$defaultBranch))
    }

    if ($settings.workflow.auto_sync_main_before_task -eq $true) {
        [void](Get-GitOutput -RepoPath $repo -Arguments @("pull","--ff-only","origin",$defaultBranch))
    }

    $baseCommit = (Get-GitOutput -RepoPath $repo -Arguments @("rev-parse","HEAD")).Text.Trim()

    $versionFileRel = [string]$project.version_file
    if ([string]::IsNullOrWhiteSpace($versionFileRel)) {
        throw "Project has no version_file configured."
    }

    $baseVersionPath = Join-Path $physicalProject $versionFileRel
    if (-not (Test-Path -LiteralPath $baseVersionPath)) {
        throw "Version file missing: $baseVersionPath"
    }

    $currentVersion = (Read-Utf8Text $baseVersionPath).Trim()
    $nextVersion = Get-NextVersionFallback $currentVersion

    $tagPrefix = if ($null -ne $project.tag_prefix) { [string]$project.tag_prefix } else { "v" }
    $tag = $tagPrefix + $nextVersion

    $tagExists = Get-GitOutput -RepoPath $repo -Arguments @("rev-parse","-q","--verify",("refs/tags/" + $tag)) -AllowFailure
    if ($tagExists.ExitCode -eq 0) {
        throw "Next version tag already exists: $tag"
    }

    # Create one isolated worktree immediately after a confirmed CHANGE request.
    # Gemini Scout, GPT Plan Gate and Claude all work against the same exact base.
    Set-AITeamProgress "3/7 建立隔離工作區" "所有 AI 都以同一個 base commit 為準"

    if (Test-Path -LiteralPath $worktreeRoot) {
        throw "Worktree path already exists: $worktreeRoot"
    }

    [void](Get-GitOutput -RepoPath $repo -Arguments @("worktree","add","-b",$branch,$worktreeRoot,$baseCommit))

    if (-not (Test-Path -LiteralPath $workProject)) {
        throw "Project subpath missing in new worktree: $workProject"
    }

    $workBase = (Get-GitOutput -RepoPath $worktreeRoot -Arguments @("rev-parse","HEAD")).Text.Trim()
    if ($workBase -ne $baseCommit) {
        throw "New worktree base commit does not match the synchronized main commit."
    }

    # ----------------------------------------------------------
    # Scout + Draft Plan. Preferred provider: Gemini/Antigravity.
    # ----------------------------------------------------------
    Set-AITeamProgress "4/7 Scout 蒐集資料與初步規劃" "優先 Gemini；不可用時自動換 AI"

    $scoutBriefPath = Join-Path $taskDir "SCOUT_BRIEF.md"
    $scoutPath = Join-Path $taskDir "SCOUT.md"

    $scoutBrief = @"
# AITeam Scout Brief

Project: $ProjectName
Physical worktree project path: $workProject
Repository subtree: $subpath
Base commit: $baseCommit
Current version: $currentVersion
Reserved next version: $nextVersion

## USER REQUEST
$Request

## ROLE
You are the first engineering scout for this CHANGE task.
Inspect the current worktree broadly enough to understand the relevant implementation.
Use search and focused source reads. Trace callers/callees/state/data flow when relevant.
Do not modify any file.

Return a concise but evidence-rich report with:
# CURRENT STATE
# RELEVANT FILES AND SYMBOLS
# IMPORTANT BEHAVIOR / DATA FLOW
# RISKS OR UNCERTAINTIES
# DRAFT PLAN
# VERIFICATION IDEAS

Do not decide the final risk level. The Plan Gate agent will verify your findings and make the final plan.
"@
    Write-Utf8NoBom $scoutBriefPath $scoutBrief

    $scoutRun = Invoke-AITeamRoleWithFallback `
        -Role "scout" `
        -Candidates @("antigravity","codex","claude") `
        -Mode "readonly" `
        -WorkingDirectory $workProject `
        -PromptPath $scoutBriefPath `
        -OutputPath $scoutPath `
        -TaskDir $taskDir `
        -AgentsConfig $agents `
        -ReasoningEffort "medium" `
        -WorktreeRoot $worktreeRoot `
        -RepoSubpath $subpath `
        -EnsureReadOnly `
        -ProgressScript { param($m) Set-AITeamProgress "4/7 Scout 蒐集資料與初步規劃" $m }

    $scoutAgent = $scoutRun.Agent
    Register-EngineeringAgent $scoutAgent
    $scout = Read-Utf8Text $scoutPath

    # ----------------------------------------------------------
    # GPT Plan Gate + formal risk decision
    # ----------------------------------------------------------
    Set-AITeamProgress "5/7 Plan Gate 驗證資料與核准方案" "優先 GPT；不可用時自動換 AI"

    $profilePath = Join-Path (Join-Path ([string]$settings.paths.profiles_root) $ProjectName) "PROJECT_PROFILE.md"
    $profile = if (Test-Path -LiteralPath $profilePath) { Read-Utf8Text $profilePath } else { "(no profile)" }

    $planPrompt = @"
You are the active Plan Gate and technical decision-maker for an AITeam CHANGE task. Provider identity does not change the responsibility.

Project: $ProjectName
Physical worktree project path: $workProject
Repository root: $worktreeRoot
Registered project subtree: $subpath
Base commit: $baseCommit
Current version: $currentVersion
AITeam-reserved next version: $nextVersion

USER REQUEST:
$Request

SCOUT REPORT (produced by $($scoutRun.FriendlyName)):
--- SCOUT START ---
$scout
--- SCOUT END ---

OPTIONAL ORIENTATION PROFILE
(may be stale; verify relevant facts against current Git source):
--- PROFILE START ---
$profile
--- PROFILE END ---

Your job is NOT to blindly accept the Scout report.
Verify important claims against the current repository. Search/read additional source only when needed.
Correct missing or mistaken evidence. Then produce the final implementation plan for the implementation agent.
Do not modify files.

Risk is recorded for rigor only. Every change task still uses the same pipeline:
Scout -> Plan Gate -> Implement -> Verification -> Independent Challenge -> Final Review.
Risk must NOT skip any of these stages.

At the top output exactly:
AITeam-Risk: small|normal|high

Then provide:
# FINAL PLAN
## Request understanding
## Verified current behavior
## Files / symbols likely to change
## Implementation instructions for Claude
## Verification requirements
## Risks / regression points
## Completion criteria
"@

    $planPromptPath = Join-Path $taskDir "PLAN_GATE_PROMPT.md"
    $planPath = Join-Path $taskDir "PLAN.md"
    Write-Utf8NoBom $planPromptPath $planPrompt

    $planRun = Invoke-AITeamRoleWithFallback `
        -Role "plan_gate" `
        -Candidates @("codex","antigravity","claude") `
        -Mode "readonly" `
        -WorkingDirectory $workProject `
        -PromptPath $planPromptPath `
        -OutputPath $planPath `
        -TaskDir $taskDir `
        -AgentsConfig $agents `
        -ReasoningEffort ([string]$agents.agents.codex.planner_reasoning_effort) `
        -WorktreeRoot $worktreeRoot `
        -RepoSubpath $subpath `
        -EnsureReadOnly `
        -ValidationPattern '(?im)^\s*AITeam-Risk\s*:\s*(small|normal|high)\s*$' `
        -ProgressScript { param($m) Set-AITeamProgress "5/7 Plan Gate 驗證資料與核准方案" $m }

    $planAgent = $planRun.Agent
    Register-EngineeringAgent $planAgent
    $plan = Read-Utf8Text $planPath
    $risk = (Get-PlanMarker $plan "AITeam-Risk" "normal").ToLowerInvariant()
    if ($risk -notin @("small","normal","high")) { $risk = "normal" }

    $headAfterPlan = (Get-GitOutput -RepoPath $worktreeRoot -Arguments @("rev-parse","HEAD")).Text.Trim()
    $statusAfterPlan = (Get-GitOutput -RepoPath $worktreeRoot -Arguments @("status","--porcelain")).Text
    if ($headAfterPlan -ne $baseCommit -or -not [string]::IsNullOrWhiteSpace($statusAfterPlan)) {
        throw "Plan Gate changed the worktree during a read-only stage."
    }

    # ----------------------------------------------------------
    # Implementation. Preferred provider: Claude.
    # ----------------------------------------------------------
    Set-AITeamProgress "6/7 實作" ("Risk=" + $risk + "；優先 Claude，不可用時自動換 AI")

    $implPrompt = @"
You are the active implementation agent for this AITeam task.
Work inside the current isolated Git worktree.

Project: $ProjectName
Current working directory: $workProject
Reserved completed version: $nextVersion
Risk classification from Plan Gate: $risk

USER REQUEST:
$Request

SCOUT REPORT (background evidence; approved plan is authoritative):
$scout

APPROVED FINAL PLAN:
$plan

Implement the requested change now.

Rules:
- Follow the approved final plan; if source reality conflicts with it, make the smallest safe correction and explain it.
- Edit only files inside this registered project subtree.
- Do not modify repository siblings.
- Do not edit VERSION; AITeam owns versioning.
- Do not commit, push, merge, tag, deploy, release, or rewrite Git history.
- Do not read secrets, runtime customer data, or unrelated user files.
- Preserve behavior outside the requested scope.
- Add/update focused tests when appropriate.
- Run available local checks when useful; AITeam verifies again afterward.
"@

    $implPromptPath = Join-Path $taskDir "IMPLEMENT_PROMPT.md"
    Write-Utf8NoBom $implPromptPath $implPrompt

    $implPath = Join-Path $taskDir "IMPLEMENTATION.md"
    $implRun = Invoke-AITeamRoleWithFallback `
        -Role "implementation" `
        -Candidates @("claude","antigravity","codex") `
        -Mode "write" `
        -WorkingDirectory $workProject `
        -PromptPath $implPromptPath `
        -OutputPath $implPath `
        -TaskDir $taskDir `
        -AgentsConfig $agents `
        -ReasoningEffort "high" `
        -ProgressScript { param($m) Set-AITeamProgress "6/7 實作" $m }

    $implementerAgent = $implRun.Agent
    Register-EngineeringAgent $implementerAgent

    Assert-ProjectOnlyChanges -WorktreeRoot $worktreeRoot -RepoSubpath $subpath

    $headAfterImplementer = (Get-GitOutput -RepoPath $worktreeRoot -Arguments @("rev-parse","HEAD")).Text.Trim()
    if ($headAfterImplementer -ne $baseCommit) {
        throw "Implementation agent created Git commits; AITeam requires uncommitted changes until final acceptance."
    }

    $changeCheck = Get-GitOutput -RepoPath $worktreeRoot -Arguments @("status","--porcelain","--",$subpath)
    if ([string]::IsNullOrWhiteSpace($changeCheck.Text)) {
        throw "Change task completed without project changes."
    }

    # One completed user change task = one formal version.
    $version = $nextVersion
    $versionPath = Join-Path $workProject $versionFileRel
    Write-Utf8NoBom $versionPath ($version + [Environment]::NewLine)

    $passed = $false

    for ($round = 0; $round -le $maxRepairs; $round++) {
        Set-AITeamProgress "6/7 驗證與審查" ("第 " + ($round + 1) + " 輪")

        Write-Utf8NoBom $versionPath ($version + [Environment]::NewLine)
        Assert-ProjectOnlyChanges -WorktreeRoot $worktreeRoot -RepoSubpath $subpath

        $verifyPath = Join-Path $taskDir ("VERIFY_{0}.txt" -f ($round + 1))
        $verifyOK = Run-ProjectVerification -Project $project -ProjectPath $workProject -WorktreeRoot $worktreeRoot -ReportPath $verifyPath

        $diff = Get-GitOutput -RepoPath $worktreeRoot -Arguments @("diff","--",$subpath)
        $untracked = Get-GitOutput -RepoPath $worktreeRoot -Arguments @("ls-files","--others","--exclude-standard","--",$subpath)
        $diffText = $diff.Text
        if (-not [string]::IsNullOrWhiteSpace($untracked.Text)) {
            $diffText += "`r`n`r`nUNTRACKED FILES:`r`n" + $untracked.Text
        }

        Write-Utf8NoBom (Join-Path $taskDir ("DIFF_{0}.patch" -f ($round + 1))) $diffText

        # Every change task gets an independent challenge. Prefer Gemini, but avoid the implementer when possible.
        Set-AITeamProgress "6/7 驗證與審查" "Independent Challenge；不可用 AI 自動跳過"

        $challengePath = Join-Path $taskDir ("CHALLENGE_{0}.md" -f ($round + 1))
        $challengePromptPath = Join-Path $taskDir ("CHALLENGE_PROMPT_{0}.md" -f ($round + 1))
        $challengePrompt = @"
You are the independent Challenger for an AITeam code change.
Do not edit files. Read the current worktree, the task artifacts in $taskDir, the verification report, and current diff.
Challenge the implementation for concrete requirement misses, correctness bugs, regressions, or safety issues.

USER REQUEST:
$Request

APPROVED PLAN:
$plan

VERIFICATION:
$(Read-Utf8Text $verifyPath)

CURRENT DIFF / UNTRACKED SUMMARY:
$diffText

First line exactly:
AITeam-Verdict: PASS
or
AITeam-Verdict: ISSUES

If ISSUES, list only concrete actionable concerns.
"@
        Write-Utf8NoBom $challengePromptPath $challengePrompt

        $challengeCandidates = Get-AITeamCandidateOrder -Preferred @("antigravity","codex","claude") -Avoid @($implementerAgent)
        $challengeRun = Invoke-AITeamRoleWithFallback `
            -Role ("challenge_" + ($round + 1)) `
            -Candidates $challengeCandidates `
            -Mode "readonly" `
            -WorkingDirectory $workProject `
            -PromptPath $challengePromptPath `
            -OutputPath $challengePath `
            -TaskDir $taskDir `
            -AgentsConfig $agents `
            -ReasoningEffort "medium" `
            -WorktreeRoot $worktreeRoot `
            -RepoSubpath $subpath `
            -EnsureReadOnly `
            -ValidationPattern '(?im)^\s*AITeam-Verdict\s*:\s*(PASS|ISSUES)\s*$' `
            -ProgressScript { param($m) Set-AITeamProgress "6/7 驗證與審查" $m }

        $challengeAgent = $challengeRun.Agent
        Register-EngineeringAgent $challengeAgent
        $reviewText = Read-Utf8Text $challengePath

        Set-AITeamProgress "6/7 驗證與審查" "Final Review / 技術裁決；優先 GPT"

        $finalPrompt = @"
You are the active final technical reviewer for an AITeam code change. Provider identity does not change the responsibility.
You may inspect relevant current source if needed. Do not modify files.

USER REQUEST:
$Request

APPROVED PLAN:
$plan

LOCAL VERIFICATION:
$(Read-Utf8Text $verifyPath)

INDEPENDENT REVIEW:
$reviewText

CURRENT GIT DIFF / UNTRACKED SUMMARY:
$diffText

Decide whether the implementation satisfies the request safely.
Independent-review objections are hypotheses, not facts; verify them.
A required verification failure normally requires repair unless demonstrably unrelated.

First line exactly:
AITeam-Verdict: PASS
or
AITeam-Verdict: REPAIR

If REPAIR, list only confirmed actionable issues.
"@

        $finalPromptPath = Join-Path $taskDir ("FINAL_PROMPT_{0}.md" -f ($round + 1))
        $finalPath = Join-Path $taskDir ("FINAL_REVIEW_{0}.md" -f ($round + 1))
        Write-Utf8NoBom $finalPromptPath $finalPrompt

        $effort = if ($risk -eq "high") {
            [string]$agents.agents.codex.high_risk_final_review_reasoning_effort
        } else {
            [string]$agents.agents.codex.normal_final_review_reasoning_effort
        }

        $finalCandidates = Get-AITeamCandidateOrder -Preferred @("codex","antigravity","claude") -Avoid @($implementerAgent)
        $finalRun = Invoke-AITeamRoleWithFallback `
            -Role ("final_review_" + ($round + 1)) `
            -Candidates $finalCandidates `
            -Mode "readonly" `
            -WorkingDirectory $workProject `
            -PromptPath $finalPromptPath `
            -OutputPath $finalPath `
            -TaskDir $taskDir `
            -AgentsConfig $agents `
            -ReasoningEffort $effort `
            -WorktreeRoot $worktreeRoot `
            -RepoSubpath $subpath `
            -EnsureReadOnly `
            -ValidationPattern '(?im)^\s*AITeam-Verdict\s*:\s*(PASS|REPAIR)\s*$' `
            -ProgressScript { param($m) Set-AITeamProgress "6/7 驗證與審查" $m }

        $finalAgent = $finalRun.Agent
        Register-EngineeringAgent $finalAgent
        $finalText = Read-Utf8Text $finalPath
        $verdict = Get-Verdict $finalText

        if ($verdict -eq "PASS" -and $verifyOK) {
            $passed = $true
            break
        }

        if ($round -ge $maxRepairs) {
            throw "Review/verification still requires repair after maximum $maxRepairs repair rounds."
        }

        Set-AITeamProgress "6/7 驗證與審查" "Repair 已確認問題；優先原實作者/Claude"

        $repairPrompt = @"
Repair the current implementation in this same isolated worktree.

Original request:
$Request

Original approved plan:
$plan

Latest final review / confirmed issues:
$finalText

Latest verification:
$(Read-Utf8Text $verifyPath)

Fix only confirmed issues.
Do not edit VERSION.
Do not restart the task, redesign unrelated code, commit, push, merge, tag, deploy,
or access unrelated user files.
"@

        $repairPromptPath = Join-Path $taskDir ("REPAIR_PROMPT_{0}.md" -f ($round + 1))
        Write-Utf8NoBom $repairPromptPath $repairPrompt

        $repairCandidates = @($implementerAgent,"claude","antigravity","codex") | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique
        $repairPath = Join-Path $taskDir ("REPAIR_RESULT_{0}.md" -f ($round + 1))
        $repairRun = Invoke-AITeamRoleWithFallback `
            -Role ("repair_" + ($round + 1)) `
            -Candidates $repairCandidates `
            -Mode "write" `
            -WorkingDirectory $workProject `
            -PromptPath $repairPromptPath `
            -OutputPath $repairPath `
            -TaskDir $taskDir `
            -AgentsConfig $agents `
            -ReasoningEffort "high" `
            -ProgressScript { param($m) Set-AITeamProgress "6/7 驗證與審查" $m }

        $repairAgent = $repairRun.Agent
        Register-EngineeringAgent $repairAgent
    }

    if (-not $passed) {
        throw "AITeam did not reach PASS."
    }

    # Degraded safety: one remaining AI may continue the work, but cannot self-approve a formal release.
    $distinctEngineeringAgents = @($script:EngineeringAgents | Select-Object -Unique)
    $hasIndependentReviewer = (($challengeAgent -ne $implementerAgent) -or ($finalAgent -ne $implementerAgent))
    if ($distinctEngineeringAgents.Count -lt 2 -or -not $hasIndependentReviewer) {
        $hold = @"
AITeam 已完成修改與驗證，但目前只有單一 AI 能完成工程流程，缺少獨立審查。

狀態：DEGRADED HOLD
專案：$ProjectName
預定版本：V$version
實作者：$(Get-AITeamAgentFriendlyName $implementerAgent)
Challenge：$(Get-AITeamAgentFriendlyName $challengeAgent)
Final Review：$(Get-AITeamAgentFriendlyName $finalAgent)

AITeam 已保留 worktree 與 task，不會 Merge / Tag / Push。
待另一個 AI 恢復後，再補獨立審查即可，不需要從頭重做。
診斷資料：$taskDir
Worktree：$worktreeRoot
"@
        Write-Utf8NoBom (Join-Path $Root "last_result.txt") $hold
        $historyPath = Join-Path $logsRoot "history.tsv"
        $line = (Get-Date -Format o) + "`t" + $ProjectName + "`t" + $version + "`t`tHOLD" + [Environment]::NewLine
        [System.IO.File]::AppendAllText($historyPath,$line,(New-Object System.Text.UTF8Encoding($false)))
        Show-AITeamResult -Title ("AITeam - " + $ProjectName + " - 待獨立審查") -Text $hold
        return
    }

    Set-AITeamProgress "7/7 保存正式版本" ("V" + $version + " / Commit + Merge + Tag + Push")

    Assert-ProjectOnlyChanges -WorktreeRoot $worktreeRoot -RepoSubpath $subpath

    [void](Get-GitOutput -RepoPath $worktreeRoot -Arguments @("add","--",$subpath))
    $staged = Get-GitOutput -RepoPath $worktreeRoot -Arguments @("diff","--cached","--name-only","--",$subpath)

    if ([string]::IsNullOrWhiteSpace($staged.Text)) {
        throw "Nothing staged for completed version."
    }

    [void](Get-GitOutput -RepoPath $worktreeRoot -Arguments @("commit","-m",($ProjectName + " V" + $version)))

    $mainBranchNow = (Get-GitOutput -RepoPath $repo -Arguments @("branch","--show-current")).Text.Trim()
    if ($mainBranchNow -ne $defaultBranch) {
        [void](Get-GitOutput -RepoPath $repo -Arguments @("switch",$defaultBranch))
    }

    $dirtyMain = Get-GitOutput -RepoPath $repo -Arguments @("status","--porcelain")
    if (-not [string]::IsNullOrWhiteSpace($dirtyMain.Text)) {
        throw "Main became dirty before merge. Completed task branch is preserved."
    }

    [void](Get-GitOutput -RepoPath $repo -Arguments @("merge","--no-ff",$branch,"-m",("Merge " + $ProjectName + " V" + $version)))
    $finalCommit = (Get-GitOutput -RepoPath $repo -Arguments @("rev-parse","HEAD")).Text.Trim()

    [void](Get-GitOutput -RepoPath $repo -Arguments @("tag","-a",$tag,"-m",($ProjectName + " V" + $version)))

    if ($settings.workflow.auto_push_completed_versions -eq $true) {
        [void](Get-GitOutput -RepoPath $repo -Arguments @("push","--atomic","origin",$defaultBranch,$tag))
    }

    $result = @"
AITeam 完成
專案：$ProjectName
版本：V$version
Tag：$tag
Commit：$finalCommit

Scout：$(Get-AITeamAgentFriendlyName $scoutAgent)
Plan Gate / Risk：$(Get-AITeamAgentFriendlyName $planAgent)
Implement：$(Get-AITeamAgentFriendlyName $implementerAgent)
Challenge：$(Get-AITeamAgentFriendlyName $challengeAgent)
Final Review：$(Get-AITeamAgentFriendlyName $finalAgent)
Risk：$risk
Final Review：PASS
GitHub Push：$(if ($settings.workflow.auto_push_completed_versions -eq $true) { "YES" } else { "NO" })
完成時間：$(Get-Date -Format o)
"@

    Write-Utf8NoBom (Join-Path $Root "last_result.txt") $result

    $historyPath = Join-Path $logsRoot "history.tsv"
    $line = (Get-Date -Format o) + "`t" + $ProjectName + "`t" + $version + "`t" + $finalCommit + "`tPASS" + [Environment]::NewLine
    [System.IO.File]::AppendAllText($historyPath,$line,(New-Object System.Text.UTF8Encoding($false)))

    [void](Get-GitOutput -RepoPath $repo -Arguments @("worktree","remove","--force",$worktreeRoot))
    [void](Get-GitOutput -RepoPath $repo -Arguments @("branch","-d",$branch))

    if ($settings.workflow.keep_successful_task_artifacts -ne $true) {
        Remove-Item -LiteralPath $taskDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Show-AITeamResult -Title ("AITeam - " + $ProjectName + " V" + $version) -Text $result

} catch {
    $message = $_.Exception.Message

    $fail = @"
AITeam 執行失敗
專案：$ProjectName
Task：$taskId

$message

AI 狀態：
GPT / Codex：$(Get-AITeamAgentStateLabel "codex")
Claude：$(Get-AITeamAgentStateLabel "claude")
Gemini / Antigravity：$(Get-AITeamAgentStateLabel "antigravity")

診斷資料：
$taskDir

若已建立隔離 worktree，會保留供診斷。
正式 main 不會因失敗而被直接丟棄。
"@

    Write-Utf8NoBom (Join-Path $Root "last_result.txt") $fail

    $historyPath = Join-Path $logsRoot "history.tsv"
    $line = (Get-Date -Format o) + "`t" + $ProjectName + "`t" + $version + "`t" + $finalCommit + "`tFAIL" + [Environment]::NewLine
    [System.IO.File]::AppendAllText($historyPath,$line,(New-Object System.Text.UTF8Encoding($false)))

    Show-AITeamResult -Title ("AITeam - " + $ProjectName + " - 失敗") -Text $fail -ErrorResult
    return
}
}

function Get-AITeamHealthWorkingDirectory {
    # Startup health only checks whether the provider itself can answer a tiny
    # prompt. It must not depend on any project/repo or tool permission.
    return $Root
}

function Invoke-AITeamProviderHealthProbe {
    param(
        [Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId,
        [Parameter(Mandatory=$true)][string]$HealthDir,
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)]$AgentsConfig
    )

    $commandName = switch ($AgentId) { "codex" { "codex" } "claude" { "claude" } "antigravity" { "agy" } }
    if ($null -eq (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        Set-AITeamSessionHealth -AgentId $AgentId -State "missing" -Reason "CLI not installed"
        return
    }

    $friendly = Get-AITeamAgentFriendlyName $AgentId
    $promptPath = Join-Path $HealthDir ($AgentId + "_health_prompt.txt")
    $outputPath = Join-Path $HealthDir ($AgentId + "_health_output.txt")
    $stderrPath = Join-Path $HealthDir ($AgentId + "_health_stderr.txt")
    Write-Utf8NoBom $promptPath "Reply with exactly AITEAM_HEALTH_OK and nothing else. Do not inspect files, use tools, or perform any other action."

    Set-AITeamSessionHealth -AgentId $AgentId -State "checking" -Reason "startup probe"
    Refresh-AITeamAgentControls
    [System.Windows.Forms.Application]::DoEvents()

    try {
        switch ($AgentId) {
            "codex" {
                Invoke-CodexReadOnly -WorkingDirectory $WorkingDirectory -PromptPath $promptPath -OutputPath $outputPath -EventsPath (Join-Path $HealthDir "codex_health.events.jsonl") -StderrPath $stderrPath -ReasoningEffort "low" -Label "GPT startup health" -SkipGitRepoCheck
            }
            "claude" {
                Invoke-ClaudeReadOnly -WorkingDirectory $WorkingDirectory -PromptPath $promptPath -RawOutputPath (Join-Path $HealthDir "claude_health.raw.json") -ResultPath $outputPath -StderrPath $stderrPath -Model ([string]$AgentsConfig.agents.claude.model) -MaxTurns 1 -Label "Claude startup health"
            }
            "antigravity" {
                Invoke-Antigravity -WorkingDirectory $WorkingDirectory -PromptPath $promptPath -OutputPath $outputPath -StderrPath $stderrPath -Label "Gemini startup health"
            }
        }

        $answer = if (Test-Path -LiteralPath $outputPath) { (Read-Utf8Text $outputPath).Trim() } else { "" }
        if ($answer -notmatch '(?i)AITEAM_HEALTH_OK') {
            throw "AITeamProviderFormatError: $friendly startup probe returned unexpected output: $answer"
        }

        Register-AITeamAgentSuccess $AgentId
    }
    catch {
        $message = $_.Exception.Message
        $failureClass = Get-AITeamAgentFailureClass $message
        Register-AITeamAgentFailure -AgentId $AgentId -FailureClass $failureClass

        if ($failureClass -eq "temporary") {
            Set-AITeamSessionHealth -AgentId $AgentId -State "temporary" -Reason $message
        } elseif ($failureClass -notin @("quota","auth")) {
            Set-AITeamSessionHealth -AgentId $AgentId -State "error" -Reason $message
        }

        try {
            [void](Write-AITeamProviderErrorLog -TaskDir $HealthDir -Role "startup_health" -AgentId $AgentId -FailureClass $failureClass -Message $message -StderrPath $stderrPath)
        } catch {}
    }
    finally {
        Refresh-AITeamAgentControls
        [System.Windows.Forms.Application]::DoEvents()
    }
}

function Invoke-AITeamStartupHealthChecks {
    if ($script:HealthChecking -or $script:TaskRunning) { return }
    $script:HealthChecking = $true

    if ($null -ne $script:StartButton) { $script:StartButton.Enabled = $false }
    if ($null -ne $script:RetryButton) { $script:RetryButton.Enabled = $false }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $healthDir = Join-Path $Root ("logs\health\" + $stamp)
    New-Item -ItemType Directory -Path $healthDir -Force | Out-Null
    $workDir = Get-AITeamHealthWorkingDirectory
    $agentsConfig = Read-AITeamJson "config\agents.json"

    try {
        foreach ($id in @("codex","claude","antigravity")) {
            Set-AITeamSessionHealth -AgentId $id -State "checking" -Reason "startup probe"
        }
        Refresh-AITeamAgentControls

        if ($null -ne $script:ProgressBox) {
            $script:ProgressBox.AppendText("啟動 AI 可用性檢查：會對三個 AI 各送出一個極短、不讀 repo 的 probe。`r`n")
        }

        $index = 0
        foreach ($id in @("codex","claude","antigravity")) {
            $index++
            if ($null -ne $script:ProgressLabel) { $script:ProgressLabel.Text = "檢查 AI 狀態 ($index/3)：" + (Get-AITeamAgentFriendlyName $id) }
            Invoke-AITeamProviderHealthProbe -AgentId $id -HealthDir $healthDir -WorkingDirectory $workDir -AgentsConfig $agentsConfig
        }

        if ($null -ne $script:ProgressLabel) { $script:ProgressLabel.Text = "AI 狀態檢查完成 / 等待任務" }
        if ($null -ne $script:ProgressBox) {
            $script:ProgressBox.AppendText(("GPT / Codex：" + (Get-AITeamAgentStateLabel "codex") + "`r`n"))
            $script:ProgressBox.AppendText(("Claude：" + (Get-AITeamAgentStateLabel "claude") + "`r`n"))
            $script:ProgressBox.AppendText(("Gemini / Antigravity：" + (Get-AITeamAgentStateLabel "antigravity") + "`r`n`r`n"))
        }
    }
    finally {
        $script:HealthChecking = $false
        if ($null -ne $script:StartButton) { $script:StartButton.Enabled = $true }
        if ($null -ne $script:RetryButton) { $script:RetryButton.Enabled = $true }
        Refresh-AITeamAgentControls
        [System.Windows.Forms.Application]::DoEvents()
    }
}

function Show-AITeamWorkspace {
    param([switch]$SmokeTest)
    Initialize-AITeamAvailability

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "AITeam"
    $form.StartPosition = "CenterScreen"
    $form.Size = New-Object System.Drawing.Size(1220,720)
    $form.MinimumSize = New-Object System.Drawing.Size(1040,620)
    $form.Font = New-Object System.Drawing.Font("Microsoft JhengHei UI",10)
    $script:MainForm = $form

    $split = New-Object System.Windows.Forms.SplitContainer
    $split.Dock = "Fill"
    $split.Orientation = "Vertical"
    $split.FixedPanel = "None"
    $form.Controls.Add($split)

    # Important: SplitContainer has a tiny default design-time width before it
    # is parented. Setting a 500px SplitterDistance / large PanelMinSize before
    # layout can throw immediately and make a hidden-launch app appear to do
    # nothing. Parent/layout first, then set geometry.
    $form.CreateControl()
    $form.PerformLayout()
    $split.SplitterDistance = 500
    $split.Panel1MinSize = 380
    $split.Panel2MinSize = 420

    # ----- Left: project / AI / request -----
    $left = $split.Panel1

    $projectLabel = New-Object System.Windows.Forms.Label
    $projectLabel.Text = "專案"
    $projectLabel.Location = New-Object System.Drawing.Point(16,16)
    $projectLabel.AutoSize = $true
    $left.Controls.Add($projectLabel)

    $combo = New-Object System.Windows.Forms.ComboBox
    $combo.DropDownStyle = "DropDownList"
    $combo.Location = New-Object System.Drawing.Point(16,40)
    $combo.Size = New-Object System.Drawing.Size(290,30)
    $combo.Anchor = "Top,Left,Right"
    $left.Controls.Add($combo)
    $script:ProjectCombo = $combo

    $manage = New-Object System.Windows.Forms.Button
    $manage.Text = "管理專案"
    $manage.Location = New-Object System.Drawing.Point(318,38)
    $manage.Size = New-Object System.Drawing.Size(105,34)
    $manage.Anchor = "Top,Right"
    $left.Controls.Add($manage)
    $script:ManageProjectsButton = $manage

    $projectInfo = New-Object System.Windows.Forms.Label
    $projectInfo.Location = New-Object System.Drawing.Point(16,78)
    $projectInfo.Size = New-Object System.Drawing.Size(445,64)
    $projectInfo.Anchor = "Top,Left,Right"
    $left.Controls.Add($projectInfo)
    $script:ProjectInfo = $projectInfo

    $group = New-Object System.Windows.Forms.GroupBox
    $group.Text = "AI 使用狀態（取消勾選只限這次開啟期間）"
    $group.Location = New-Object System.Drawing.Point(16,148)
    $group.Size = New-Object System.Drawing.Size(445,130)
    $group.Anchor = "Top,Left,Right"
    $left.Controls.Add($group)

    $codexCheck = New-Object System.Windows.Forms.CheckBox
    $codexCheck.Location = New-Object System.Drawing.Point(12,24)
    $codexCheck.Size = New-Object System.Drawing.Size(300,24)
    $group.Controls.Add($codexCheck)
    $script:CodexCheck = $codexCheck

    $claudeCheck = New-Object System.Windows.Forms.CheckBox
    $claudeCheck.Location = New-Object System.Drawing.Point(12,50)
    $claudeCheck.Size = New-Object System.Drawing.Size(300,24)
    $group.Controls.Add($claudeCheck)
    $script:ClaudeCheck = $claudeCheck

    $agyCheck = New-Object System.Windows.Forms.CheckBox
    $agyCheck.Location = New-Object System.Drawing.Point(12,76)
    $agyCheck.Size = New-Object System.Drawing.Size(300,24)
    $group.Controls.Add($agyCheck)
    $script:AgyCheck = $agyCheck

    $retry = New-Object System.Windows.Forms.Button
    $retry.Text = "重新檢查 AI"
    $retry.Location = New-Object System.Drawing.Point(315,48)
    $retry.Size = New-Object System.Drawing.Size(116,34)
    $retry.Anchor = "Top,Right"
    $retry.Add_Click({
        if ($script:TaskRunning -or $script:HealthChecking) { return }
        Reset-AllAITeamAgentAvailability
        Invoke-AITeamStartupHealthChecks
    })
    $group.Controls.Add($retry)
    $script:RetryButton = $retry

    $requestLabel = New-Object System.Windows.Forms.Label
    $requestLabel.Text = "任務 / 查詢內容"
    $requestLabel.Location = New-Object System.Drawing.Point(16,292)
    $requestLabel.AutoSize = $true
    $left.Controls.Add($requestLabel)

    $requestBox = New-Object System.Windows.Forms.TextBox
    $requestBox.Multiline = $true
    $requestBox.ScrollBars = "Vertical"
    $requestBox.AcceptsReturn = $true
    $requestBox.AcceptsTab = $true
    $requestBox.Location = New-Object System.Drawing.Point(16,318)
    $requestBox.Size = New-Object System.Drawing.Size(445,285)
    $requestBox.Anchor = "Top,Bottom,Left,Right"
    $left.Controls.Add($requestBox)
    $script:RequestBox = $requestBox

    $newTask = New-Object System.Windows.Forms.Button
    $newTask.Text = "新任務"
    $newTask.Location = New-Object System.Drawing.Point(16,620)
    $newTask.Size = New-Object System.Drawing.Size(95,36)
    $newTask.Anchor = "Bottom,Left"
    $left.Controls.Add($newTask)
    $script:NewTaskButton = $newTask

    $start = New-Object System.Windows.Forms.Button
    $start.Text = "開始"
    $start.Location = New-Object System.Drawing.Point(366,620)
    $start.Size = New-Object System.Drawing.Size(95,36)
    $start.Anchor = "Bottom,Right"
    $left.Controls.Add($start)
    $script:StartButton = $start

    $close = New-Object System.Windows.Forms.Button
    $close.Text = "關閉"
    $close.Location = New-Object System.Drawing.Point(261,620)
    $close.Size = New-Object System.Drawing.Size(95,36)
    $close.Anchor = "Bottom,Right"
    $left.Controls.Add($close)

    # ----- Right: persistent progress/result -----
    $right = $split.Panel2
    $progressTitle = New-Object System.Windows.Forms.Label
    $progressTitle.Text = "任務進度 / 結果"
    $progressTitle.Location = New-Object System.Drawing.Point(16,16)
    $progressTitle.AutoSize = $true
    $right.Controls.Add($progressTitle)

    $stage = New-Object System.Windows.Forms.Label
    $stage.Text = "等待任務"
    $stage.Location = New-Object System.Drawing.Point(16,44)
    $stage.Size = New-Object System.Drawing.Size(620,28)
    $stage.Anchor = "Top,Left,Right"
    $right.Controls.Add($stage)
    $script:ProgressLabel = $stage

    $progress = New-Object System.Windows.Forms.TextBox
    $progress.Multiline = $true
    $progress.ReadOnly = $true
    $progress.ScrollBars = "Both"
    $progress.WordWrap = $true
    $progress.Location = New-Object System.Drawing.Point(16,78)
    $progress.Size = New-Object System.Drawing.Size(650,578)
    $progress.Anchor = "Top,Bottom,Left,Right"
    $right.Controls.Add($progress)
    $script:ProgressBox = $progress

    $manage.Add_Click({
        if ($script:TaskRunning) { return }
        $selected = if ($null -ne $combo.SelectedItem) { [string]$combo.SelectedItem } else { "" }
        Show-AITeamProjectManager -Owner $form
        Repair-AITeamProjectRegistrations
        Refresh-AITeamProjectCombo $selected
        Refresh-AITeamSelectedProjectInfo
    })
    $combo.Add_SelectedIndexChanged({ Refresh-AITeamSelectedProjectInfo })

    $codexCheck.Add_CheckedChanged({ if (-not $script:TaskRunning) { Set-AITeamAgentManualEnabled -AgentId "codex" -Enabled ([bool]$codexCheck.Checked); Refresh-AITeamAgentControls } })
    $claudeCheck.Add_CheckedChanged({ if (-not $script:TaskRunning) { Set-AITeamAgentManualEnabled -AgentId "claude" -Enabled ([bool]$claudeCheck.Checked); Refresh-AITeamAgentControls } })
    $agyCheck.Add_CheckedChanged({ if (-not $script:TaskRunning) { Set-AITeamAgentManualEnabled -AgentId "antigravity" -Enabled ([bool]$agyCheck.Checked); Refresh-AITeamAgentControls } })

    $newTask.Add_Click({
        if ($script:TaskRunning) { return }
        $requestBox.Clear()
        $progress.Clear()
        $stage.Text = "等待任務"
        $requestBox.Focus()
    })

    $close.Add_Click({ if (-not $script:TaskRunning) { $form.Close() } })
    $form.Add_FormClosing({ param($sender,$e) if ($script:TaskRunning) { $e.Cancel = $true } })

    $start.Add_Click({
        if ($script:TaskRunning) { return }
        if ($null -eq $combo.SelectedItem) {
            [System.Windows.Forms.MessageBox]::Show("請先按『管理專案』新增或選擇專案。","AITeam") | Out-Null
            return
        }
        if ([string]::IsNullOrWhiteSpace($requestBox.Text)) {
            [System.Windows.Forms.MessageBox]::Show("請先輸入需求。","AITeam") | Out-Null
            return
        }
        if (-not $codexCheck.Checked -and -not $claudeCheck.Checked -and -not $agyCheck.Checked) {
            [System.Windows.Forms.MessageBox]::Show("至少保留一個 AI 可供 AITeam 使用。","AITeam") | Out-Null
            return
        }

        Set-AITeamAgentManualEnabled -AgentId "codex" -Enabled ([bool]$codexCheck.Checked)
        Set-AITeamAgentManualEnabled -AgentId "claude" -Enabled ([bool]$claudeCheck.Checked)
        Set-AITeamAgentManualEnabled -AgentId "antigravity" -Enabled ([bool]$agyCheck.Checked)

        $script:TaskRunning = $true
        Set-AITeamTaskControlsEnabled $false
        $requestBox.ReadOnly = $true
        try {
            Invoke-AITeamTask -ProjectName ([string]$combo.SelectedItem) -Request ([string]$requestBox.Text)
        } finally {
            $script:TaskRunning = $false
            Set-AITeamTaskControlsEnabled $true
            $requestBox.ReadOnly = $false
            Refresh-AITeamAgentControls
            Refresh-AITeamSelectedProjectInfo
        }
    })

    Refresh-AITeamProjectCombo
    Refresh-AITeamSelectedProjectInfo
    Refresh-AITeamAgentControls
    if ($SmokeTest) {
        $form.CreateControl()
        $form.PerformLayout()
        if ($split.Width -le 0 -or $split.Panel1.Width -le 0 -or $split.Panel2.Width -le 0) {
            throw "AITeam GUI smoke test failed: invalid split-container geometry."
        }
        $form.Dispose()
        return
    }

    $form.Add_Shown({
        $requestBox.Focus()
        [System.Windows.Forms.Application]::DoEvents()
        Invoke-AITeamStartupHealthChecks
    })
    [void]$form.ShowDialog()
}

if ($Command -eq "help") {
    Write-Output "AITeam - launch AITeam.vbs or AITeam.cmd to start."
    exit 0
}

if ($Command -eq "status") {
    Write-Output (Get-AITeamStatusText)
    exit 0
}

if ($Command -eq "gui-smoke") {
    Show-AITeamWorkspace -SmokeTest
    Write-Output "AITeam GUI smoke test: PASS"
    exit 0
}

if (-not [string]::IsNullOrWhiteSpace($ProjectName) -and -not [string]::IsNullOrWhiteSpace($Request)) {
    # Command-line maintenance/testing path. Normal daily use is the single-window GUI.
    $script:ProgressBox = New-Object System.Windows.Forms.TextBox
    $script:ProgressLabel = New-Object System.Windows.Forms.Label
    Invoke-AITeamTask -ProjectName $ProjectName -Request $Request
    Write-Output $script:ProgressBox.Text
    exit 0
}

Show-AITeamWorkspace

Set-StrictMode -Version 2.0

$script:AITeamRoot = "D:\AITeam"

function Write-Utf8NoBom {
    param([Parameter(Mandatory=$true)][string]$Path,[Parameter(Mandatory=$true)][AllowEmptyString()][string]$Text)
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path,$Text,(New-Object System.Text.UTF8Encoding($false)))
}

function Read-Utf8Text {
    param([Parameter(Mandatory=$true)][string]$Path)
    return [System.IO.File]::ReadAllText($Path,[System.Text.Encoding]::UTF8)
}

function Read-AITeamJson {
    param([Parameter(Mandatory=$true)][string]$RelativePath)
    $path = Join-Path $script:AITeamRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing AITeam config: $path" }
    return ((Read-Utf8Text $path) | ConvertFrom-Json)
}

function Resolve-AITeamProject {
    param([Parameter(Mandatory=$true)][string]$Name)
    $data = Read-AITeamJson "config\projects.json"
    $project = @($data.projects | Where-Object { $_.name -eq $Name -and ($null -eq $_.active -or $_.active -ne $false) }) | Select-Object -First 1
    if ($null -eq $project) { throw "Project not registered or inactive: $Name" }
    $physical = Join-Path ([string]$project.repo_path) ([string]$project.repo_subpath)
    if (-not (Test-Path -LiteralPath $physical)) { throw "Physical project path missing: $physical" }
    return $project
}

function Resolve-ToolPath {
    param([Parameter(Mandatory=$true)][string]$CommandName)
    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -eq $cmd) { throw "Required tool not found: $CommandName" }
    return $cmd.Source
}

function Get-GitOutput {
    param(
        [Parameter(Mandatory=$true)][string]$RepoPath,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $oldEncoding = [Console]::OutputEncoding
    $oldPreference = $ErrorActionPreference
    try {
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $ErrorActionPreference = "Continue"
        $raw = @(& git -C $RepoPath @Arguments 2>&1)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldPreference
        [Console]::OutputEncoding = $oldEncoding
    }

    $lines = @(
        $raw | ForEach-Object {
            if ($null -eq $_) { "" } else { [string]$_ }
        }
    )
    $text = ($lines -join [Environment]::NewLine).TrimEnd()

    if (-not $AllowFailure -and $code -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$text"
    }

    return [PSCustomObject]@{
        ExitCode = $code
        Text = $text
        Lines = $lines
    }
}

function Wait-ProcessWithProgress {
    param(
        [Parameter(Mandatory=$true)]$Process,
        [Parameter(Mandatory=$true)][string]$Label
    )

    while (-not $Process.HasExited) {
        try {
            if ("System.Windows.Forms.Application" -as [type]) {
                [System.Windows.Forms.Application]::DoEvents()
            }
        } catch {}

        Start-Sleep -Milliseconds 350
        $Process.Refresh()
    }

    $Process.WaitForExit()
    $Process.Refresh()

    $code = $null
    try { $code = $Process.ExitCode } catch {}
    return $code
}

function Invoke-CodexReadOnly {
    param(
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$PromptPath,
        [Parameter(Mandatory=$true)][string]$OutputPath,
        [Parameter(Mandatory=$true)][string]$EventsPath,
        [Parameter(Mandatory=$true)][string]$StderrPath,
        [ValidateSet("low","medium","high","xhigh")][string]$ReasoningEffort="medium",
        [string]$Label="GPT/Codex",
        [switch]$SkipGitRepoCheck
    )
    $codex = Resolve-ToolPath "codex"
    Remove-Item $OutputPath,$EventsPath,$StderrPath -Force -ErrorAction SilentlyContinue
    $configArg = 'model_reasoning_effort="' + $ReasoningEffort + '"'
    $execArgs = @("exec")
    if ($SkipGitRepoCheck) { $execArgs += "--skip-git-repo-check" }
    $execArgs += @("--json","--output-last-message",$OutputPath,"-")
    $args = @("--sandbox","read-only","--ask-for-approval","never","-c",$configArg) + $execArgs
    $proc = Start-Process -FilePath $codex -ArgumentList $args -WorkingDirectory $WorkingDirectory -RedirectStandardInput $PromptPath -RedirectStandardOutput $EventsPath -RedirectStandardError $StderrPath -WindowStyle Hidden -PassThru
    $exit = Wait-ProcessWithProgress -Process $proc -Label $Label
    $completed = $false; $eventErrors = 0
    if (Test-Path -LiteralPath $EventsPath) {
        foreach ($line in [System.IO.File]::ReadLines($EventsPath,[System.Text.Encoding]::UTF8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $evt = $line | ConvertFrom-Json
                if ($evt.type -eq "turn.completed") { $completed = $true }
                if ($evt.type -eq "error") { $eventErrors++ }
            } catch {}
        }
    }
    $hasOutput = (Test-Path -LiteralPath $OutputPath) -and ((Get-Item -LiteralPath $OutputPath).Length -gt 0)
    if (-not $hasOutput -or $eventErrors -gt 0 -or (($null -ne $exit -and $exit -ne 0) -and -not $completed)) {
        $err = if (Test-Path -LiteralPath $StderrPath) { Read-Utf8Text $StderrPath } else { "" }
        $eventTail = ""
        if (Test-Path -LiteralPath $EventsPath) {
            $allEvents = Read-Utf8Text $EventsPath
            if ($allEvents.Length -gt 12000) {
                $eventTail = $allEvents.Substring($allEvents.Length - 12000)
            } else {
                $eventTail = $allEvents
            }
        }
        throw "$Label failed. Exit=$exit completed=$completed errors=$eventErrors`nSTDERR:`n$err`nEVENTS:`n$eventTail"
    }
}

function Convert-ClaudeOuterResult {
    param([Parameter(Mandatory=$true)][string]$RawPath,[Parameter(Mandatory=$true)][string]$ResultPath)
    $raw = Read-Utf8Text $RawPath
    try { $outer = $raw | ConvertFrom-Json } catch { Write-Utf8NoBom $ResultPath $raw; return }
    if ($null -ne $outer.result) { Write-Utf8NoBom $ResultPath ([string]$outer.result) } else { Write-Utf8NoBom $ResultPath $raw }
}

function Invoke-ClaudeImplementer {
    param(
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$PromptPath,
        [Parameter(Mandatory=$true)][string]$RawOutputPath,
        [Parameter(Mandatory=$true)][string]$ResultPath,
        [Parameter(Mandatory=$true)][string]$StderrPath,
        [string]$Model="sonnet",
        [int]$MaxTurns=40,
        [string]$Label="Claude"
    )
    $claude = Resolve-ToolPath "claude"
    Remove-Item $RawOutputPath,$ResultPath,$StderrPath -Force -ErrorAction SilentlyContinue
    $systemPrompt = "You are the primary implementer inside an isolated AITeam Git worktree. Edit only the current project worktree. Never push, merge, tag, deploy, rewrite Git history, read secrets, or access unrelated user files. Follow the task on UTF-8 standard input. Run appropriate local checks when possible."
    $printPrompt = "Use the UTF-8 standard input as the complete implementation task. Work directly in the current working directory. When finished, return a concise implementation report."
    if ($systemPrompt.Contains('"') -or $printPrompt.Contains('"') -or $Model.Contains('"')) { throw "Unexpected quote in Claude argument." }
    $argString = @('-p',('"{0}"' -f $printPrompt),'--system-prompt',('"{0}"' -f $systemPrompt),'--output-format','json','--max-turns',[string]$MaxTurns,'--model',('"{0}"' -f $Model),'--permission-mode','bypassPermissions','--disable-slash-commands','--no-session-persistence') -join ' '
    $proc = Start-Process -FilePath $claude -ArgumentList $argString -WorkingDirectory $WorkingDirectory -RedirectStandardInput $PromptPath -RedirectStandardOutput $RawOutputPath -RedirectStandardError $StderrPath -WindowStyle Hidden -PassThru
    $exit = Wait-ProcessWithProgress -Process $proc -Label $Label
    if (-not (Test-Path -LiteralPath $RawOutputPath) -or (Get-Item $RawOutputPath).Length -eq 0) {
        $err = if (Test-Path -LiteralPath $StderrPath) { Read-Utf8Text $StderrPath } else { "" }
        throw "$Label produced no output. $err"
    }
    if ($null -ne $exit -and $exit -ne 0) {
        $err = if (Test-Path -LiteralPath $StderrPath) { Read-Utf8Text $StderrPath } else { "" }
        throw "$Label exited with code $exit. $err"
    }
    Convert-ClaudeOuterResult -RawPath $RawOutputPath -ResultPath $ResultPath
}

function Get-AITeamOptionalPropertyString {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory=$true)][string]$Name
    )
    if ($null -eq $Object) { return "" }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return "" }
    return [string]$prop.Value
}


function Test-AITeamPathWithinRoot {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$RootPath
    )

    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar,[System.IO.Path]::AltDirectorySeparatorChar)
    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar,[System.IO.Path]::AltDirectorySeparatorChar)

    if ($full.Equals($root,[System.StringComparison]::OrdinalIgnoreCase)) { return $true }

    $prefix = $root + [System.IO.Path]::DirectorySeparatorChar
    return $full.StartsWith($prefix,[System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-Antigravity {
    param(
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$PromptPath,
        [Parameter(Mandatory=$true)][string]$OutputPath,
        [Parameter(Mandatory=$true)][string]$StderrPath,
        [string]$Label="Antigravity"
    )

    $agy = Resolve-ToolPath "agy"
    if (-not (Test-Path -LiteralPath $PromptPath)) { throw "Antigravity prompt file missing: $PromptPath" }
    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) { throw "Antigravity working directory missing: $WorkingDirectory" }

    # AITeam only dispatches Antigravity inside its registered repo/worktree.
    # Headless agy cannot display permission prompts. The official CLI flag
    # below approves tool requests for this one run so repo reads/edits do not
    # get silently auto-denied. AITeam still applies project-scope/read-only
    # fingerprints around provider roles and never grants this as a persistent
    # global Antigravity setting.
    if (-not (Test-AITeamPathWithinRoot -Path $WorkingDirectory -RootPath $script:AITeamRoot)) {
        throw "AITeamSafetyViolation: Antigravity working directory is outside D:\\AITeam: $WorkingDirectory"
    }

    $prompt = Read-Utf8Text $PromptPath
    $inputPath = $OutputPath + ".agy-input.jsonl"
    $streamPath = $OutputPath + ".agy-stream.jsonl"
    Remove-Item $OutputPath,$StderrPath,$inputPath,$streamPath -Force -ErrorAction SilentlyContinue

    $payload = [PSCustomObject]@{
        event = "user"
        message = [PSCustomObject]@{ content = $prompt }
    } | ConvertTo-Json -Compress -Depth 8
    Write-Utf8NoBom $inputPath ($payload + [Environment]::NewLine)

    $args = @(
        "--dangerously-skip-permissions",
        "--input-format","stream-json",
        "--output-format","stream-json",
        "--print-timeout","10m"
    )

    $proc = Start-Process -FilePath $agy -ArgumentList $args -WorkingDirectory $WorkingDirectory -RedirectStandardInput $inputPath -RedirectStandardOutput $streamPath -RedirectStandardError $StderrPath -WindowStyle Hidden -PassThru
    $exit = Wait-ProcessWithProgress -Process $proc -Label $Label

    $lastResult = $null
    if (Test-Path -LiteralPath $streamPath) {
        foreach ($line in [System.IO.File]::ReadLines($streamPath,[System.Text.Encoding]::UTF8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $evt = $line | ConvertFrom-Json
                $eventName = Get-AITeamOptionalPropertyString -Object $evt -Name "event"
                $resultProp = $evt.PSObject.Properties['result']
                if ($eventName -eq "result" -and $null -ne $resultProp -and $null -ne $resultProp.Value) {
                    $lastResult = $resultProp.Value
                }
            } catch {}
        }
    }

    $stderrText = if (Test-Path -LiteralPath $StderrPath) { Read-Utf8Text $StderrPath } else { "" }
    if ($null -eq $lastResult) {
        $tail = if (Test-Path -LiteralPath $streamPath) { Read-Utf8Text $streamPath } else { "" }
        if ($tail.Length -gt 12000) { $tail = $tail.Substring($tail.Length-12000) }
        throw "$Label produced no terminal result. Exit=$exit`nSTDERR:`n$stderrText`nSTREAM:`n$tail"
    }

    # Antigravity result objects are sparse. Missing optional properties must
    # not become PowerShell StrictMode failures.
    $status = (Get-AITeamOptionalPropertyString -Object $lastResult -Name "status").ToUpperInvariant()
    $response = Get-AITeamOptionalPropertyString -Object $lastResult -Name "response"
    $providerError = Get-AITeamOptionalPropertyString -Object $lastResult -Name "error"

    if ([string]::IsNullOrWhiteSpace($status)) { $status = "UNKNOWN" }

    if ($status -ne "SUCCESS" -or ($null -ne $exit -and $exit -ne 0)) {
        throw "$Label failed. Exit=$exit status=$status error=$providerError`nSTDERR:`n$stderrText"
    }

    if ([string]::IsNullOrWhiteSpace($response)) {
        throw "AITeamProviderFormatError: $Label returned SUCCESS with an empty response.`nSTDERR:`n$stderrText"
    }

    Write-Utf8NoBom $OutputPath $response
}

function Get-AITeamAvailabilityPath {
    return (Join-Path $script:AITeamRoot "config\availability.json")
}

function Initialize-AITeamSessionAvailability {
    # Manual checkboxes are SESSION ONLY. They always start enabled on a new
    # AITeam process and are never written to availability.json.
    $script:AITeamSessionEnabled = @{
        codex = $true
        claude = $true
        antigravity = $true
    }

    # Fresh runtime health is also session-only. A real provider probe at
    # application startup replaces UNKNOWN with ONLINE / QUOTA / AUTH / etc.
    $script:AITeamSessionHealth = @{
        codex = [PSCustomObject]@{ state="unknown"; reason="" }
        claude = [PSCustomObject]@{ state="unknown"; reason="" }
        antigravity = [PSCustomObject]@{ state="unknown"; reason="" }
    }
}

function Set-AITeamSessionHealth {
    param(
        [Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId,
        [Parameter(Mandatory=$true)][ValidateSet("unknown","checking","online","quota","auth","temporary","error","missing")][string]$State,
        [AllowEmptyString()][string]$Reason=""
    )
    if ($null -eq $script:AITeamSessionHealth) { Initialize-AITeamSessionAvailability }
    $script:AITeamSessionHealth[$AgentId] = [PSCustomObject]@{ state=$State; reason=$Reason }
}

function Get-AITeamSessionHealth {
    param([Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId)
    if ($null -eq $script:AITeamSessionHealth) { Initialize-AITeamSessionAvailability }
    return $script:AITeamSessionHealth[$AgentId]
}

function Test-AITeamSessionAgentEnabled {
    param([Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId)
    if ($null -eq $script:AITeamSessionEnabled) { Initialize-AITeamSessionAvailability }
    return ($script:AITeamSessionEnabled[$AgentId] -eq $true)
}

function New-AITeamAvailabilityData {
    $now = (Get-Date).ToString("o")
    return [PSCustomObject]@{
        schema_version = 2
        agents = [PSCustomObject]@{
            codex = [PSCustomObject]@{ state="available"; reason=""; updated_at=$now }
            claude = [PSCustomObject]@{ state="available"; reason=""; updated_at=$now }
            antigravity = [PSCustomObject]@{ state="available"; reason=""; updated_at=$now }
        }
    }
}

function Save-AITeamAvailabilityData {
    param([Parameter(Mandatory=$true)]$Data)
    $path = Get-AITeamAvailabilityPath
    $json = $Data | ConvertTo-Json -Depth 8
    Write-Utf8NoBom $path ($json + [Environment]::NewLine)
}

function Initialize-AITeamAvailability {
    if ($null -eq $script:AITeamSessionEnabled) { Initialize-AITeamSessionAvailability }

    $path = Get-AITeamAvailabilityPath
    if (-not (Test-Path -LiteralPath $path)) {
        Save-AITeamAvailabilityData (New-AITeamAvailabilityData)
        return
    }

    try {
        $data = (Read-Utf8Text $path) | ConvertFrom-Json
    } catch {
        $backup = $path + ".invalid_" + (Get-Date -Format "yyyyMMdd_HHmmss")
        Copy-Item -LiteralPath $path -Destination $backup -Force
        Save-AITeamAvailabilityData (New-AITeamAvailabilityData)
        return
    }

    $changed = $false
    if ($null -eq $data.PSObject.Properties['schema_version']) {
        $data | Add-Member -NotePropertyName schema_version -NotePropertyValue 2
        $changed = $true
    } elseif ([int]$data.schema_version -lt 2) {
        $data.schema_version = 2
        $changed = $true
    }

    if ($null -eq $data.agents) {
        $data | Add-Member -NotePropertyName agents -NotePropertyValue ([PSCustomObject]@{})
        $changed = $true
    }

    $defaults = New-AITeamAvailabilityData
    foreach ($id in @("codex","claude","antigravity")) {
        $prop = $data.agents.PSObject.Properties[$id]
        if ($null -eq $prop) {
            $defaultState = $defaults.agents.PSObject.Properties[$id].Value
            $data.agents | Add-Member -NotePropertyName $id -NotePropertyValue $defaultState
            $changed = $true
            continue
        }

        $state = $prop.Value
        foreach ($field in @("state","reason","updated_at")) {
            if ($null -eq $state.PSObject.Properties[$field]) {
                $defaultValue = $defaults.agents.PSObject.Properties[$id].Value.PSObject.Properties[$field].Value
                $state | Add-Member -NotePropertyName $field -NotePropertyValue $defaultValue
                $changed = $true
            }
        }

        # Legacy manual_enabled was accidentally persistent. Remove it.
        if ($null -ne $state.PSObject.Properties['manual_enabled']) {
            $state.PSObject.Properties.Remove('manual_enabled')
            $changed = $true
        }

        # Generic/temporary failures are not persistent provider states.
        if ([string]$state.state -in @("temp_error","error")) {
            $state.state = "available"
            $state.reason = ""
            $state.updated_at = (Get-Date).ToString("o")
            $changed = $true
        }
    }

    if ($changed) { Save-AITeamAvailabilityData $data }
}

function Get-AITeamAvailabilityData {
    Initialize-AITeamAvailability
    return ((Read-Utf8Text (Get-AITeamAvailabilityPath)) | ConvertFrom-Json)
}

function Get-AITeamAgentState {
    param([Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId)
    $data = Get-AITeamAvailabilityData
    return $data.agents.PSObject.Properties[$AgentId].Value
}

function Set-AITeamAgentManualEnabled {
    param(
        [Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId,
        [Parameter(Mandatory=$true)][bool]$Enabled
    )
    if ($null -eq $script:AITeamSessionEnabled) { Initialize-AITeamSessionAvailability }
    $script:AITeamSessionEnabled[$AgentId] = $Enabled
}

function Reset-AITeamAgentAvailability {
    param([Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId)
    Set-AITeamSessionHealth -AgentId $AgentId -State "unknown" -Reason ""
    $data = Get-AITeamAvailabilityData
    $state = $data.agents.PSObject.Properties[$AgentId].Value
    $state.state = "available"
    $state.reason = ""
    $state.updated_at = (Get-Date).ToString("o")
    Save-AITeamAvailabilityData $data
}

function Reset-AllAITeamAgentAvailability {
    $data = Get-AITeamAvailabilityData
    foreach ($id in @("codex","claude","antigravity")) {
        Set-AITeamSessionHealth -AgentId $id -State "unknown" -Reason ""
        $state = $data.agents.PSObject.Properties[$id].Value
        $state.state = "available"
        $state.reason = ""
        $state.updated_at = (Get-Date).ToString("o")
    }
    Save-AITeamAvailabilityData $data
}

function Get-AITeamAgentFriendlyName {
    param([Parameter(Mandatory=$true)][string]$AgentId)
    switch ($AgentId) {
        "codex" { return "GPT / Codex" }
        "claude" { return "Claude" }
        "antigravity" { return "Gemini / Antigravity" }
        default { return $AgentId }
    }
}

function Get-AITeamAgentStateLabel {
    param([Parameter(Mandatory=$true)][string]$AgentId)
    if (-not (Test-AITeamSessionAgentEnabled $AgentId)) { return "手動暫停（本次）" }

    $health = Get-AITeamSessionHealth $AgentId
    switch ([string]$health.state) {
        "checking" { return "檢查中..." }
        "online" { return "上線" }
        "quota" { return "超過限額" }
        "auth" { return "需要重新登入" }
        "temporary" { return "暫時異常（任務時會再試）" }
        "error" { return "異常（本次先跳過）" }
        "missing" { return "CLI 未安裝" }
    }

    # Before the fresh startup probe finishes, preserve useful last-known
    # persistent information for quota/auth only.
    $state = Get-AITeamAgentState $AgentId
    switch ([string]$state.state) {
        "cooldown" { return "上次超過限額，待重新確認" }
        "auth_required" { return "上次需要登入，待重新確認" }
        default { return "待檢查" }
    }
}

function Test-AITeamAgentUsable {
    param(
        [Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId,
        [Parameter(Mandatory=$true)]$AgentsConfig
    )
    $cfgProp = $AgentsConfig.agents.PSObject.Properties[$AgentId]
    if ($null -eq $cfgProp -or $cfgProp.Value.enabled -eq $false) { return $false }
    if (-not (Test-AITeamSessionAgentEnabled $AgentId)) { return $false }

    $health = Get-AITeamSessionHealth $AgentId
    if ([string]$health.state -in @("quota","auth","error","missing")) { return $false }
    # TEMPORARY and UNKNOWN are allowed one normal task attempt; fallback
    # machinery will classify/fall through if they are still unavailable.

    $state = Get-AITeamAgentState $AgentId
    return ([string]$state.state -eq "available")
}

function Get-AITeamAgentFailureClass {
    param([Parameter(Mandatory=$true)][AllowEmptyString()][string]$Text)
    $t = $Text.ToLowerInvariant()

    if ($t -match '(quota|rate[_ -]?limit|usage[_ -]?(limit|cap)|too many requests|http[^\r\n]*429|\b429\b|limit[^\r\n]{0,80}(reached|exceeded)|exceeded[^\r\n]{0,80}(quota|limit)|you.{0,30}hit.{0,30}limit|five[ -]?hour|5[ -]?hour|resets?[^\r\n]{0,80}(usage|limit)|capacity limit|credits? exhausted)') {
        return "quota"
    }
    if ($t -match '(unauthorized|authentication required|not logged in|not authenticated|sign[ -]?in required|login required|invalid api key|invalid token|expired token|\b401\b|credential.*invalid)') {
        return "auth"
    }
    if ($t -match '(timeout|timed out|network error|connection (reset|failed|refused)|temporarily unavailable|service unavailable|bad gateway|gateway timeout|subscriber fell behind|\b502\b|\b503\b|\b504\b)') {
        return "temporary"
    }
    if ($t -match '(auto-denied|permission[^\r\n]{0,100}headless|tool required[^\r\n]{0,100}permission|read_file[^\r\n]{0,100}permission)') { return "permission" }
    if ($t -match 'aiteamproviderformaterror') { return "format" }
    return "error"
}

function Register-AITeamAgentFailure {
    param(
        [Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId,
        [Parameter(Mandatory=$true)][string]$FailureClass
    )

    # Record every failure in current-session health, but persist only states
    # that genuinely require provider/user recovery.
    switch ($FailureClass) {
        "quota" { Set-AITeamSessionHealth -AgentId $AgentId -State "quota" -Reason "額度 / 使用上限" }
        "auth" { Set-AITeamSessionHealth -AgentId $AgentId -State "auth" -Reason "登入 / 授權失效" }
        "temporary" { Set-AITeamSessionHealth -AgentId $AgentId -State "temporary" -Reason "暫時網路 / provider 異常" }
        default { Set-AITeamSessionHealth -AgentId $AgentId -State "error" -Reason $FailureClass }
    }
    if ($FailureClass -notin @("quota","auth")) { return }

    $data = Get-AITeamAvailabilityData
    $state = $data.agents.PSObject.Properties[$AgentId].Value
    if ($FailureClass -eq "quota") {
        $state.state = "cooldown"
        $state.reason = "偵測到額度 / 使用上限；AITeam 會跳過此 AI，按『重試暫停 AI』後重新嘗試。"
    } else {
        $state.state = "auth_required"
        $state.reason = "登入或授權失效；重新登入後按『重試暫停 AI』。"
    }
    $state.updated_at = (Get-Date).ToString("o")
    Save-AITeamAvailabilityData $data
}

function Register-AITeamAgentSuccess {
    param([Parameter(Mandatory=$true)][ValidateSet("codex","claude","antigravity")][string]$AgentId)
    Set-AITeamSessionHealth -AgentId $AgentId -State "online" -Reason "startup/task probe succeeded"
    $data = Get-AITeamAvailabilityData
    $state = $data.agents.PSObject.Properties[$AgentId].Value
    $state.state = "available"
    $state.reason = ""
    $state.updated_at = (Get-Date).ToString("o")
    Save-AITeamAvailabilityData $data
}

function Get-AITeamProviderLogRoot {
    $path = Join-Path $script:AITeamRoot "logs\providers"
    if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
    return $path
}

function Protect-AITeamLogText {
    param([AllowEmptyString()][string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return "" }
    $result = $Text
    $result = [regex]::Replace($result,'(?im)(password|secret|token|api[_-]?key|appkey|access[_-]?key|private[_-]?key)\s*[:=]\s*[^\r\n,}]+','$1=<REDACTED>')
    if ($result.Length -gt 30000) { $result = $result.Substring(0,15000) + "`r`n...[TRUNCATED]...`r`n" + $result.Substring($result.Length-15000) }
    return $result
}

function Write-AITeamProviderErrorLog {
    param(
        [Parameter(Mandatory=$true)][string]$TaskDir,
        [Parameter(Mandatory=$true)][string]$Role,
        [Parameter(Mandatory=$true)][string]$AgentId,
        [Parameter(Mandatory=$true)][string]$FailureClass,
        [Parameter(Mandatory=$true)][string]$Message,
        [string]$StderrPath="",
        [string]$EventsPath=""
    )
    $root = Get-AITeamProviderLogRoot
    $taskName = Split-Path -Leaf $TaskDir
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss_fff"
    $safeRole = ($Role -replace '[^A-Za-z0-9_-]','_')
    $path = Join-Path $root ($stamp + "_" + $taskName + "_" + $AgentId + "_" + $safeRole + ".log")

    $parts = @()
    $parts += "Time: " + (Get-Date -Format o)
    $parts += "Task: " + $taskName
    $parts += "Role: " + $Role
    $parts += "Agent: " + (Get-AITeamAgentFriendlyName $AgentId)
    $parts += "FailureClass: " + $FailureClass
    $parts += ""
    $parts += "MESSAGE"
    $parts += (Protect-AITeamLogText $Message)

    if (-not [string]::IsNullOrWhiteSpace($StderrPath) -and (Test-Path -LiteralPath $StderrPath)) {
        $parts += ""
        $parts += "STDERR"
        $parts += (Protect-AITeamLogText (Read-Utf8Text $StderrPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($EventsPath) -and (Test-Path -LiteralPath $EventsPath)) {
        $parts += ""
        $parts += "EVENTS / STREAM"
        $parts += (Protect-AITeamLogText (Read-Utf8Text $EventsPath))
    }

    Write-Utf8NoBom $path (($parts -join [Environment]::NewLine) + [Environment]::NewLine)
    return $path
}

function Add-AITeamRoutingLog {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Role,
        [Parameter(Mandatory=$true)][string]$AgentId,
        [Parameter(Mandatory=$true)][string]$Result,
        [string]$Detail=""
    )
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $safeDetail = ($Detail -replace "[\r\n\t]+"," ").Trim()
    if ($safeDetail.Length -gt 240) { $safeDetail = $safeDetail.Substring(0,240) }
    $line = (Get-Date -Format o) + "`t" + $Role + "`t" + $AgentId + "`t" + $Result + "`t" + $safeDetail + [Environment]::NewLine
    [System.IO.File]::AppendAllText($Path,$line,(New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-ClaudeReadOnly {
    param(
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$PromptPath,
        [Parameter(Mandatory=$true)][string]$RawOutputPath,
        [Parameter(Mandatory=$true)][string]$ResultPath,
        [Parameter(Mandatory=$true)][string]$StderrPath,
        [string]$Model="sonnet",
        [int]$MaxTurns=24,
        [string]$Label="Claude read-only"
    )
    $claude = Resolve-ToolPath "claude"
    Remove-Item $RawOutputPath,$ResultPath,$StderrPath -Force -ErrorAction SilentlyContinue
    $systemPrompt = "You are a read-only AITeam analyst/reviewer. You may inspect the current project when the task requires it, but never edit/write/create/delete project files, never commit/push/merge/tag/deploy, never read secrets or unrelated user files. Follow the UTF-8 task on standard input."
    $printPrompt = "Use the UTF-8 standard input as the complete task. Perform read-only analysis and return the requested answer/report."
    if ($systemPrompt.Contains('"') -or $printPrompt.Contains('"') -or $Model.Contains('"')) { throw "Unexpected quote in Claude read-only argument." }
    $argString = @('-p',('"{0}"' -f $printPrompt),'--system-prompt',('"{0}"' -f $systemPrompt),'--output-format','json','--max-turns',[string]$MaxTurns,'--model',('"{0}"' -f $Model),'--permission-mode','dontAsk','--disable-slash-commands','--no-session-persistence') -join ' '
    $proc = Start-Process -FilePath $claude -ArgumentList $argString -WorkingDirectory $WorkingDirectory -RedirectStandardInput $PromptPath -RedirectStandardOutput $RawOutputPath -RedirectStandardError $StderrPath -WindowStyle Hidden -PassThru
    $exit = Wait-ProcessWithProgress -Process $proc -Label $Label
    $raw = if (Test-Path -LiteralPath $RawOutputPath) { Read-Utf8Text $RawOutputPath } else { "" }
    $err = if (Test-Path -LiteralPath $StderrPath) { Read-Utf8Text $StderrPath } else { "" }
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "$Label produced no output.`n$err" }
    if ($null -ne $exit -and $exit -ne 0) { throw "$Label exited with code $exit.`n$err`n$raw" }
    try {
        $outer = $raw | ConvertFrom-Json
        if ($null -ne $outer.PSObject.Properties['is_error'] -and $outer.is_error -eq $true) {
            throw "$Label returned an error result.`n$raw`n$err"
        }
    } catch {
        if ($_.Exception.Message -like "$Label returned an error result.*") { throw }
    }
    Convert-ClaudeOuterResult -RawPath $RawOutputPath -ResultPath $ResultPath
}

function Invoke-CodexImplementer {
    param(
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$PromptPath,
        [Parameter(Mandatory=$true)][string]$OutputPath,
        [Parameter(Mandatory=$true)][string]$EventsPath,
        [Parameter(Mandatory=$true)][string]$StderrPath,
        [ValidateSet("low","medium","high","xhigh")][string]$ReasoningEffort="medium",
        [string]$Label="GPT/Codex implementer"
    )
    $codex = Resolve-ToolPath "codex"
    Remove-Item $OutputPath,$EventsPath,$StderrPath -Force -ErrorAction SilentlyContinue
    $configArg = 'model_reasoning_effort="' + $ReasoningEffort + '"'
    $args = @("--sandbox","workspace-write","--ask-for-approval","never","-c",$configArg,"exec","--json","--output-last-message",$OutputPath,"-")
    $proc = Start-Process -FilePath $codex -ArgumentList $args -WorkingDirectory $WorkingDirectory -RedirectStandardInput $PromptPath -RedirectStandardOutput $EventsPath -RedirectStandardError $StderrPath -WindowStyle Hidden -PassThru
    $exit = Wait-ProcessWithProgress -Process $proc -Label $Label
    $completed = $false; $eventErrors = 0
    if (Test-Path -LiteralPath $EventsPath) {
        foreach ($line in [System.IO.File]::ReadLines($EventsPath,[System.Text.Encoding]::UTF8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $evt = $line | ConvertFrom-Json
                if ($evt.type -eq "turn.completed") { $completed = $true }
                if ($evt.type -eq "error") { $eventErrors++ }
            } catch {}
        }
    }
    $hasOutput = (Test-Path -LiteralPath $OutputPath) -and ((Get-Item -LiteralPath $OutputPath).Length -gt 0)
    if (-not $hasOutput -or $eventErrors -gt 0 -or (($null -ne $exit -and $exit -ne 0) -and -not $completed)) {
        $err = if (Test-Path -LiteralPath $StderrPath) { Read-Utf8Text $StderrPath } else { "" }
        $eventTail = if (Test-Path -LiteralPath $EventsPath) { Read-Utf8Text $EventsPath } else { "" }
        if ($eventTail.Length -gt 12000) { $eventTail = $eventTail.Substring($eventTail.Length - 12000) }
        throw "$Label failed. Exit=$exit completed=$completed errors=$eventErrors`nSTDERR:`n$err`nEVENTS:`n$eventTail"
    }
}

function Invoke-AITeamRoleWithFallback {
    param(
        [Parameter(Mandatory=$true)][string]$Role,
        [Parameter(Mandatory=$true)][string[]]$Candidates,
        [Parameter(Mandatory=$true)][ValidateSet("readonly","write")][string]$Mode,
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$PromptPath,
        [Parameter(Mandatory=$true)][string]$OutputPath,
        [Parameter(Mandatory=$true)][string]$TaskDir,
        [Parameter(Mandatory=$true)]$AgentsConfig,
        [string]$ReasoningEffort="medium",
        [string]$WorktreeRoot="",
        [string]$RepoSubpath="",
        [switch]$EnsureReadOnly,
        [string]$ValidationPattern="",
        [scriptblock]$ProgressScript=$null
    )

    $routingLog = Join-Path $TaskDir "AGENT_ROUTING.tsv"
    $failures = @()
    $ordered = @($Candidates | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)

    foreach ($agentId in $ordered) {
        $friendly = Get-AITeamAgentFriendlyName $agentId
        if (-not (Test-AITeamAgentUsable -AgentId $agentId -AgentsConfig $AgentsConfig)) {
            $label = Get-AITeamAgentStateLabel $agentId
            Add-AITeamRoutingLog -Path $routingLog -Role $Role -AgentId $agentId -Result "SKIP" -Detail $label
            if ($null -ne $ProgressScript) { & $ProgressScript ("跳過 " + $friendly + "（" + $label + "）") }
            continue
        }

        $safeRole = ($Role -replace '[^A-Za-z0-9_-]','_')
        $base = Join-Path $TaskDir ($safeRole + "_" + $agentId)
        $candidateOutput = $base + ".out.md"
        $stderr = $base + ".stderr.log"
        $events = $base + ".events.jsonl"
        $raw = $base + ".raw.json"

        $maxAttempts = 2
        for ($attempt=1; $attempt -le $maxAttempts; $attempt++) {
            if ($null -ne $ProgressScript) {
                if ($attempt -eq 1) { & $ProgressScript ("使用 " + $friendly) }
                else { & $ProgressScript ($friendly + " 暫時失敗，重試一次") }
            }

            $beforeFingerprint = ""
            $beforeHead = ""
            if ($EnsureReadOnly -and -not [string]::IsNullOrWhiteSpace($WorktreeRoot) -and -not [string]::IsNullOrWhiteSpace($RepoSubpath)) {
                $beforeFingerprint = Get-ProjectChangeFingerprint -WorktreeRoot $WorktreeRoot -RepoSubpath $RepoSubpath
                $beforeHead = (Get-GitOutput -RepoPath $WorktreeRoot -Arguments @("rev-parse","HEAD")).Text.Trim()
            }

            try {
                switch ($agentId) {
                    "codex" {
                        if ($Mode -eq "readonly") {
                            Invoke-CodexReadOnly -WorkingDirectory $WorkingDirectory -PromptPath $PromptPath -OutputPath $candidateOutput -EventsPath $events -StderrPath $stderr -ReasoningEffort $ReasoningEffort -Label ($friendly + " / " + $Role)
                        } else {
                            Invoke-CodexImplementer -WorkingDirectory $WorkingDirectory -PromptPath $PromptPath -OutputPath $candidateOutput -EventsPath $events -StderrPath $stderr -ReasoningEffort $ReasoningEffort -Label ($friendly + " / " + $Role)
                        }
                    }
                    "claude" {
                        $model = [string]$AgentsConfig.agents.claude.model
                        $turns = [int]$AgentsConfig.agents.claude.max_turns
                        if ($Mode -eq "readonly") {
                            Invoke-ClaudeReadOnly -WorkingDirectory $WorkingDirectory -PromptPath $PromptPath -RawOutputPath $raw -ResultPath $candidateOutput -StderrPath $stderr -Model $model -MaxTurns $turns -Label ($friendly + " / " + $Role)
                        } else {
                            Invoke-ClaudeImplementer -WorkingDirectory $WorkingDirectory -PromptPath $PromptPath -RawOutputPath $raw -ResultPath $candidateOutput -StderrPath $stderr -Model $model -MaxTurns $turns -Label ($friendly + " / " + $Role)
                        }
                    }
                    "antigravity" {
                        Invoke-Antigravity -WorkingDirectory $WorkingDirectory -PromptPath $PromptPath -OutputPath $candidateOutput -StderrPath $stderr -Label ($friendly + " / " + $Role)
                        $events = $candidateOutput + ".agy-stream.jsonl"
                    }
                }

                if ($EnsureReadOnly -and -not [string]::IsNullOrWhiteSpace($WorktreeRoot) -and -not [string]::IsNullOrWhiteSpace($RepoSubpath)) {
                    $afterFingerprint = Get-ProjectChangeFingerprint -WorktreeRoot $WorktreeRoot -RepoSubpath $RepoSubpath
                    $afterHead = (Get-GitOutput -RepoPath $WorktreeRoot -Arguments @("rev-parse","HEAD")).Text.Trim()
                    if ($beforeFingerprint -ne $afterFingerprint -or $beforeHead -ne $afterHead) {
                        throw "AITeamSafetyViolation: $friendly modified files during read-only role $Role."
                    }
                }

                if (-not (Test-Path -LiteralPath $candidateOutput) -or (Get-Item -LiteralPath $candidateOutput).Length -eq 0) {
                    throw "AITeamProviderFormatError: $friendly returned no usable output for $Role."
                }

                $candidateText = Read-Utf8Text $candidateOutput
                if (-not [string]::IsNullOrWhiteSpace($ValidationPattern) -and $candidateText -notmatch $ValidationPattern) {
                    throw "AITeamProviderFormatError: $friendly returned an invalid response format for $Role."
                }

                Copy-Item -LiteralPath $candidateOutput -Destination $OutputPath -Force
                Register-AITeamAgentSuccess $agentId
                Add-AITeamRoutingLog -Path $routingLog -Role $Role -AgentId $agentId -Result "PASS"

                return [PSCustomObject]@{
                    Agent = $agentId
                    FriendlyName = $friendly
                    OutputPath = $OutputPath
                }
            } catch {
                $message = $_.Exception.Message
                if ($message.StartsWith("AITeamSafetyViolation:")) {
                    $logPath = Write-AITeamProviderErrorLog -TaskDir $TaskDir -Role $Role -AgentId $agentId -FailureClass "safety" -Message $message -StderrPath $stderr -EventsPath $events
                    Add-AITeamRoutingLog -Path $routingLog -Role $Role -AgentId $agentId -Result "SAFETY_STOP" -Detail ("Provider log: " + $logPath)
                    throw
                }

                $class = Get-AITeamAgentFailureClass $message
                $logPath = Write-AITeamProviderErrorLog -TaskDir $TaskDir -Role $Role -AgentId $agentId -FailureClass $class -Message $message -StderrPath $stderr -EventsPath $events

                # One retry only for transient/format errors. Quota/auth and
                # generic CLI errors immediately fall back to the next provider.
                if ($attempt -lt $maxAttempts -and $class -in @("temporary","format")) {
                    Add-AITeamRoutingLog -Path $routingLog -Role $Role -AgentId $agentId -Result ("RETRY_" + $class.ToUpperInvariant()) -Detail ("Provider log: " + $logPath)
                    Start-Sleep -Seconds 2
                    continue
                }

                Register-AITeamAgentFailure -AgentId $agentId -FailureClass $class
                Add-AITeamRoutingLog -Path $routingLog -Role $Role -AgentId $agentId -Result ("FAIL_" + $class.ToUpperInvariant()) -Detail ("Provider log: " + $logPath)
                $failures += ($friendly + ": " + $class)

                if ($null -ne $ProgressScript) {
                    if ($class -in @("quota","auth")) {
                        & $ProgressScript ($friendly + " 失敗，已標記「" + (Get-AITeamAgentStateLabel $agentId) + "」；改用下一個 AI")
                    } else {
                        & $ProgressScript ($friendly + " 本次失敗（" + $class + "），不會跨任務停用；改用下一個 AI")
                    }
                }
                break
            }
        }
    }

    $availability = @()
    foreach ($id in @("codex","claude","antigravity")) {
        $availability += ((Get-AITeamAgentFriendlyName $id) + "=" + (Get-AITeamAgentStateLabel $id))
    }
    throw "AITeam role '$Role' has no usable AI. $($availability -join '; ') Failures: $($failures -join '; ')"
}

function Get-PlanMarker {
    param([Parameter(Mandatory=$true)][string]$Plan,[Parameter(Mandatory=$true)][string]$Name,[string]$Default="")
    $pattern = '(?im)^\s*' + [regex]::Escape($Name) + '\s*:\s*([^\r\n]+)\s*$'
    $m = [regex]::Match($Plan,$pattern)
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return $Default
}

function Get-Verdict {
    param([Parameter(Mandatory=$true)][string]$Text)
    $m = [regex]::Match($Text,'(?im)^\s*AITeam-Verdict\s*:\s*(PASS|REPAIR|ISSUES)\s*$')
    if ($m.Success) { return $m.Groups[1].Value.ToUpperInvariant() }
    return "UNKNOWN"
}

function Get-NextVersionFallback {
    param([Parameter(Mandatory=$true)][string]$Current)

    if ($Current -match '^(\d+)\.(\d+)\.(\d+)$') {
        return ('{0}.{1}.{2}' -f $Matches[1],$Matches[2],([int]$Matches[3]+1))
    }

    if ($Current -match '^(\d+)\.(\d+)\.(\d+)-rc\.(\d+)$') {
        return ('{0}.{1}.{2}-rc.{3}' -f $Matches[1],$Matches[2],$Matches[3],([int]$Matches[4]+1))
    }

    if ($Current -match '^(\d+)\.(\d+)\.(\d+)-fix(\d+)$') {
        return ('{0}.{1}.{2}-fix{3}' -f $Matches[1],$Matches[2],$Matches[3],([int]$Matches[4]+1))
    }

    throw "Cannot automatically increment version: $Current"
}

function Assert-ProjectOnlyChanges {
    param([Parameter(Mandatory=$true)][string]$WorktreeRoot,[Parameter(Mandatory=$true)][string]$RepoSubpath)
    $prefix = ($RepoSubpath -replace '\\','/').Trim('/') + '/'
    $names = @()
    $tracked = Get-GitOutput -RepoPath $WorktreeRoot -Arguments @('diff','--name-only')
    $staged = Get-GitOutput -RepoPath $WorktreeRoot -Arguments @('diff','--cached','--name-only')
    $untracked = Get-GitOutput -RepoPath $WorktreeRoot -Arguments @('ls-files','--others','--exclude-standard')
    $names += @($tracked.Lines); $names += @($staged.Lines); $names += @($untracked.Lines)
    $outside = @()
    foreach ($n in @($names | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)) {
        $p = ([string]$n -replace '\\','/').Trim()
        if (-not $p.StartsWith($prefix,[System.StringComparison]::OrdinalIgnoreCase)) { $outside += $p }
    }
    if ($outside.Count -gt 0) { throw "Implementation changed files outside the registered project subtree:`n$($outside -join [Environment]::NewLine)" }
}

function Get-ProjectChangeFingerprint {
    param([Parameter(Mandatory=$true)][string]$WorktreeRoot,[Parameter(Mandatory=$true)][string]$RepoSubpath)
    $parts = @()
    $diff = Get-GitOutput -RepoPath $WorktreeRoot -Arguments @('diff','--binary','--',$RepoSubpath)
    $parts += $diff.Text
    $untracked = Get-GitOutput -RepoPath $WorktreeRoot -Arguments @('ls-files','--others','--exclude-standard','--',$RepoSubpath)
    foreach ($rel in @($untracked.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object)) {
        $path = Join-Path $WorktreeRoot ([string]$rel)
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            $parts += (([string]$rel) + ':' + $hash)
        }
    }
    $joined = $parts -join "`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($joined)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','') } finally { $sha.Dispose() }
}

function Run-ProjectVerification {
    param([Parameter(Mandatory=$true)]$Project,[Parameter(Mandatory=$true)][string]$ProjectPath,[Parameter(Mandatory=$true)][string]$WorktreeRoot,[Parameter(Mandatory=$true)][string]$ReportPath)
    $rows = @(); $failed = $false
    $diffCheck = Get-GitOutput -RepoPath $WorktreeRoot -Arguments @('diff','--check') -AllowFailure
    if ($diffCheck.ExitCode -eq 0) { $rows += '[PASS] Git diff --check' } else { $rows += '[FAIL] Git diff --check'; $rows += $diffCheck.Text; $failed=$true }
    foreach ($check in @($Project.verification)) {
        $name = [string]$check.name; $command = [string]$check.command; $required = ($check.required -eq $true); $skip = ($check.skip_if_missing -eq $true)
        $cmd = Get-Command $command -ErrorAction SilentlyContinue
        if ($null -eq $cmd) {
            if ($skip -or -not $required) { $rows += "[SKIP] $name - tool missing: $command"; continue }
            $rows += "[FAIL] $name - tool missing: $command"; $failed=$true; continue
        }
        $args = @($check.args | ForEach-Object { [string]$_ })
        Push-Location $ProjectPath
        $oldPreference = $ErrorActionPreference
        try {
            $old = [Console]::OutputEncoding
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
            $ErrorActionPreference = "Continue"
            $out = @(& $command @args 2>&1)
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldPreference
            [Console]::OutputEncoding = $old
            Pop-Location
        }
        if ($code -eq 0) { $rows += "[PASS] $name" } else { $rows += "[FAIL] $name (exit $code)"; $rows += (@($out)-join [Environment]::NewLine); if ($required) { $failed=$true } }
    }
    Write-Utf8NoBom $ReportPath (($rows -join [Environment]::NewLine)+[Environment]::NewLine)
    return (-not $failed)
}

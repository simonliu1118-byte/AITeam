Set-StrictMode -Version 2.0

function Get-AITeamProjectsPath {
    return (Join-Path $script:AITeamRoot "config\projects.json")
}

function Get-AITeamProjectsData {
    $path = Get-AITeamProjectsPath
    if (-not (Test-Path -LiteralPath $path)) {
        return [PSCustomObject]@{ schema_version=1; projects=@() }
    }
    $data = (Read-Utf8Text $path) | ConvertFrom-Json
    if ($null -eq $data.projects) { $data | Add-Member -NotePropertyName projects -NotePropertyValue @() }
    return $data
}

function Save-AITeamProjectsData {
    param([Parameter(Mandatory=$true)]$Data)
    Write-Utf8NoBom (Get-AITeamProjectsPath) (($Data | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
}

function Get-AITeamGitHubRepoNameFromRemote {
    param([AllowEmptyString()][string]$Remote)
    if ([string]::IsNullOrWhiteSpace($Remote)) { return "" }
    $r = $Remote.Trim()
    if ($r -match '^https?://github\.com/([^/]+)/([^/]+?)(?:\.git)?$') { return ($Matches[1] + "/" + $Matches[2]) }
    if ($r -match '^git@github\.com:([^/]+)/(.+?)(?:\.git)?$') { return ($Matches[1] + "/" + $Matches[2]) }
    if ($r -match '^ssh://git@github\.com/([^/]+)/(.+?)(?:\.git)?$') { return ($Matches[1] + "/" + $Matches[2]) }
    return ""
}

function Get-AITeamDefaultVerification {
    param([Parameter(Mandatory=$true)][string]$ProjectFolder)
    $checks = @()
    if (Test-Path -LiteralPath (Join-Path $ProjectFolder "go.mod")) {
        $checks += [PSCustomObject]@{ name="Go tests"; command="go"; args=@("test","./..."); required=$false; skip_if_missing=$true }
        $checks += [PSCustomObject]@{ name="Go vet"; command="go"; args=@("vet","./..."); required=$false; skip_if_missing=$true }
    }
    return @($checks)
}

function Get-AITeamRepoInventory {
    $items = @()
    $seen = @{}

    $candidates = @()
    $reposRoot = Join-Path $script:AITeamRoot "repos"
    if (Test-Path -LiteralPath $reposRoot -PathType Container) {
        $candidates += @(Get-ChildItem -LiteralPath $reposRoot -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    }

    try {
        $data = Get-AITeamProjectsData
        foreach ($p in @($data.projects)) {
            if ($null -ne $p.PSObject.Properties['repo_path'] -and -not [string]::IsNullOrWhiteSpace([string]$p.repo_path)) {
                $candidates += [string]$p.repo_path
            }
        }
    } catch {}

    foreach ($path in @($candidates | Select-Object -Unique)) {
        try {
            if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
            $top = Get-GitOutput -RepoPath $path -Arguments @("rev-parse","--show-toplevel") -AllowFailure
            if ($top.ExitCode -ne 0) { continue }
            $root = ([System.IO.Path]::GetFullPath($top.Text.Trim())).TrimEnd('\')
            if (-not $root.Equals(([System.IO.Path]::GetFullPath($path)).TrimEnd('\'),[System.StringComparison]::OrdinalIgnoreCase)) { continue }
            $remote = Get-GitOutput -RepoPath $root -Arguments @("config","--get","remote.origin.url") -AllowFailure
            $github = if ($remote.ExitCode -eq 0) { Get-AITeamGitHubRepoNameFromRemote $remote.Text.Trim() } else { "" }
            if ([string]::IsNullOrWhiteSpace($github)) { continue }
            $key = $github.ToLowerInvariant()
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            $items += [PSCustomObject]@{ github_repo=$github; repo_path=$root }
        } catch {}
    }

    return @($items | Sort-Object github_repo)
}


function Get-AITeamKnownReposPath {
    return (Join-Path $script:AITeamRoot "config\repositories.json")
}

function Get-AITeamKnownGitHubRepos {
    $names = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($item in @(Get-AITeamRepoInventory)) {
        $name = [string]$item.github_repo
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $key = $name.ToLowerInvariant()
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $names.Add($name)
            }
        }
    }

    try {
        $data = Get-AITeamProjectsData
        foreach ($project in @($data.projects)) {
            if ($null -ne $project.PSObject.Properties['github_repo']) {
                $name = [string]$project.github_repo
                if (-not [string]::IsNullOrWhiteSpace($name)) {
                    $key = $name.ToLowerInvariant()
                    if (-not $seen.ContainsKey($key)) {
                        $seen[$key] = $true
                        $names.Add($name)
                    }
                }
            }
        }
    } catch {}

    $path = Get-AITeamKnownReposPath
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        try {
            $data = (Read-Utf8Text $path) | ConvertFrom-Json
            foreach ($repo in @($data.repositories)) {
                $name = [string]$repo
                if (-not [string]::IsNullOrWhiteSpace($name)) {
                    $key = $name.ToLowerInvariant()
                    if (-not $seen.ContainsKey($key)) {
                        $seen[$key] = $true
                        $names.Add($name)
                    }
                }
            }
        } catch {}
    }

    return @($names | Sort-Object)
}

function Save-AITeamKnownGitHubRepo {
    param([Parameter(Mandatory=$true)][string]$GitHubRepo)

    $repo = $GitHubRepo.Trim()
    if ($repo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        return
    }

    $all = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($name in @(Get-AITeamKnownGitHubRepos) + @($repo)) {
        if ([string]::IsNullOrWhiteSpace([string]$name)) {
            continue
        }
        $key = ([string]$name).ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            $all.Add([string]$name)
        }
    }

    $obj = [PSCustomObject]@{
        schema_version = 1
        repositories = @($all | Sort-Object)
    }

    Write-Utf8NoBom (Get-AITeamKnownReposPath) (
        ($obj | ConvertTo-Json -Depth 6) + [Environment]::NewLine
    )
}

function Invoke-AITeamGitClone {
    param(
        [Parameter(Mandatory=$true)][string]$GitHubRepo,
        [Parameter(Mandatory=$true)][string]$Destination
    )

    $git = Resolve-ToolPath "git"
    $url = "https://github.com/" + $GitHubRepo + ".git"
    $stdout = Join-Path $env:TEMP ("aiteam_clone_" + [Guid]::NewGuid().ToString("N") + ".out")
    $stderr = $stdout + ".err"
    try {
        $proc = Start-Process -FilePath $git -ArgumentList @("clone","--origin","origin",$url,$Destination) -WorkingDirectory (Split-Path -Parent $Destination) -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
        $proc.WaitForExit()
        $code = $proc.ExitCode
        $text = ""
        if (Test-Path -LiteralPath $stdout) { $text += Read-Utf8Text $stdout }
        if (Test-Path -LiteralPath $stderr) { $text += "`r`n" + (Read-Utf8Text $stderr) }
        if ($code -ne 0) { throw "Git clone 失敗：$text" }
    }
    finally {
        Remove-Item $stdout,$stderr -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-AITeamGitHubRepoLocal {
    param([Parameter(Mandatory=$true)][string]$GitHubRepo)

    $repo = $GitHubRepo.Trim()
    if ($repo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw "GitHub Repo 格式應為 owner/repo，例如 simonliu1118-byte/CYapps。"
    }

    foreach ($item in @(Get-AITeamRepoInventory)) {
        if ([string]$item.github_repo -ieq $repo) { return [string]$item.repo_path }
    }

    $reposRoot = Join-Path $script:AITeamRoot "repos"
    if (-not (Test-Path -LiteralPath $reposRoot -PathType Container)) { New-Item -ItemType Directory -Path $reposRoot -Force | Out-Null }

    $parts = $repo.Split('/')
    $owner = $parts[0]
    $name = $parts[1]
    $dest = Join-Path $reposRoot $name

    if (Test-Path -LiteralPath $dest) {
        $dest = Join-Path $reposRoot ($owner + "--" + $name)
    }
    if (Test-Path -LiteralPath $dest) {
        throw "AITeam 自動分配的本機 Repo 位置已存在但無法辨識：$dest"
    }

    Invoke-AITeamGitClone -GitHubRepo $repo -Destination $dest
    return $dest
}

function Update-AITeamRemoteRefs {
    param([Parameter(Mandatory=$true)][string]$RepoPath)
    $r = Get-GitOutput -RepoPath $RepoPath -Arguments @("fetch","--prune","origin") -AllowFailure
    if ($r.ExitCode -ne 0) { throw "無法更新 GitHub Repo 資訊：$($r.Text)" }
}

function Get-AITeamRemoteDefaultBranch {
    param([Parameter(Mandatory=$true)][string]$RepoPath)
    $head = Get-GitOutput -RepoPath $RepoPath -Arguments @("symbolic-ref","--short","refs/remotes/origin/HEAD") -AllowFailure
    if ($head.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($head.Text)) {
        return ($head.Text.Trim() -replace '^origin/','')
    }
    foreach ($candidate in @("main","master")) {
        $check = Get-GitOutput -RepoPath $RepoPath -Arguments @("rev-parse","--verify",("refs/remotes/origin/" + $candidate)) -AllowFailure
        if ($check.ExitCode -eq 0) { return $candidate }
    }
    $current = Get-GitOutput -RepoPath $RepoPath -Arguments @("branch","--show-current") -AllowFailure
    if ($current.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($current.Text)) { return $current.Text.Trim() }
    throw "無法判斷 GitHub Repo 的預設分支。"
}

function Get-AITeamRepoDirectories {
    param([Parameter(Mandatory=$true)][string]$RepoPath)
    Update-AITeamRemoteRefs -RepoPath $RepoPath
    $branch = Get-AITeamRemoteDefaultBranch -RepoPath $RepoPath
    $remoteRef = "origin/" + $branch
    $r = Get-GitOutput -RepoPath $RepoPath -Arguments @("ls-tree","-d","-r","--name-only",$remoteRef) -AllowFailure
    if ($r.ExitCode -ne 0) { throw "無法讀取 GitHub Repo 目錄結構（$remoteRef）：$($r.Text)" }
    return @(
        $r.Lines |
        ForEach-Object { $_.Trim() } |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            $_ -notmatch '(^|/)\.github(?:/|$)'
        } |
        Sort-Object -Unique
    )
}

function Sync-AITeamRepoWorkingTreeToRemoteDefault {
    param([Parameter(Mandatory=$true)][string]$RepoPath)
    Update-AITeamRemoteRefs -RepoPath $RepoPath
    $branch = Get-AITeamRemoteDefaultBranch -RepoPath $RepoPath
    $remoteRef = "origin/" + $branch

    $dirty = Get-GitOutput -RepoPath $RepoPath -Arguments @("status","--porcelain") -AllowFailure
    if ($dirty.ExitCode -ne 0) { throw "無法檢查本機 Repo 狀態：$($dirty.Text)" }
    if (-not [string]::IsNullOrWhiteSpace($dirty.Text)) {
        throw "AITeam 管理的本機 Repo 目前有未提交變更，為避免覆蓋資料，不會自動同步。請先處理該 Repo 的本機變更。"
    }

    $current = Get-GitOutput -RepoPath $RepoPath -Arguments @("branch","--show-current") -AllowFailure
    $currentBranch = if ($current.ExitCode -eq 0) { $current.Text.Trim() } else { "" }
    if ($currentBranch -ne $branch) {
        $sw = Get-GitOutput -RepoPath $RepoPath -Arguments @("switch",$branch) -AllowFailure
        if ($sw.ExitCode -ne 0) {
            $sw = Get-GitOutput -RepoPath $RepoPath -Arguments @("switch","-c",$branch,"--track",$remoteRef) -AllowFailure
            if ($sw.ExitCode -ne 0) { throw "無法切換至預設分支 $branch：$($sw.Text)" }
        }
    }

    $counts = Get-GitOutput -RepoPath $RepoPath -Arguments @("rev-list","--left-right","--count",("HEAD..." + $remoteRef)) -AllowFailure
    if ($counts.ExitCode -ne 0) { throw "無法比較本機與 GitHub 分支：$($counts.Text)" }
    $parts = @($counts.Text.Trim() -split '\s+')
    $localOnly = 0; $remoteOnly = 0
    if ($parts.Count -ge 2) {
        [void][int]::TryParse($parts[0],[ref]$localOnly)
        [void][int]::TryParse($parts[1],[ref]$remoteOnly)
    }
    if ($localOnly -gt 0) { throw "AITeam 本機 Repo 的 $branch 有尚未存在於 GitHub 的 commit，為避免覆蓋或混線，不會自動同步。" }
    if ($remoteOnly -gt 0) {
        $ff = Get-GitOutput -RepoPath $RepoPath -Arguments @("merge","--ff-only",$remoteRef) -AllowFailure
        if ($ff.ExitCode -ne 0) { throw "無法 fast-forward 到 GitHub 最新 $branch：$($ff.Text)" }
    }
    return $branch
}

function Add-AITeamTreePath {
    param(
        [Parameter(Mandatory=$true)][System.Windows.Forms.TreeNode]$RootNode,
        [Parameter(Mandatory=$true)][string]$Path
    )
    $current = $RootNode
    $acc = @()
    foreach ($part in @($Path -split '/')) {
        if ([string]::IsNullOrWhiteSpace($part)) { continue }
        $acc += $part
        $found = $null
        foreach ($n in @($current.Nodes)) {
            if ([string]$n.Text -eq $part) { $found = $n; break }
        }
        if ($null -eq $found) {
            $found = New-Object System.Windows.Forms.TreeNode($part)
            $found.Tag = ($acc -join '/')
            [void]$current.Nodes.Add($found)
        }
        $current = $found
    }
}

function Select-AITeamTreeSubpath {
    param(
        [Parameter(Mandatory=$true)][System.Windows.Forms.TreeView]$Tree,
        [AllowEmptyString()][string]$Subpath=""
    )
    $target = $Subpath.Trim('/')
    $stack = New-Object System.Collections.Stack
    foreach ($r in @($Tree.Nodes)) { $stack.Push($r) }
    while ($stack.Count -gt 0) {
        $n = $stack.Pop()
        $tag = if ($null -eq $n.Tag) { "" } else { [string]$n.Tag }
        if ($tag -eq $target) {
            $Tree.SelectedNode = $n
            $n.EnsureVisible()
            return
        }
        foreach ($c in @($n.Nodes)) { $stack.Push($c) }
    }
}

function Get-AITeamProjectRegistrationFromRepoSelection {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][string]$GitHubRepo,
        [Parameter(Mandatory=$true)][string]$RepoPath,
        [AllowEmptyString()][string]$RepoSubpath="",
        $Existing=$null
    )

    if ([string]::IsNullOrWhiteSpace($Name)) { throw "請填寫專案名稱。" }
    if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) { throw "找不到本機 Repo：$RepoPath" }

    # 儲存專案時才把 AITeam 管理的本機 clone 安全同步到 GitHub 預設分支。
    $syncedBranch = Sync-AITeamRepoWorkingTreeToRemoteDefault -RepoPath $RepoPath

    $subpath = $RepoSubpath.Trim('/').Replace('\\','/')
    $projectFull = if ([string]::IsNullOrWhiteSpace($subpath)) { $RepoPath } else { Join-Path $RepoPath ($subpath.Replace('/','\\')) }
    if (-not (Test-Path -LiteralPath $projectFull -PathType Container)) { throw "選擇的 Repo 子目錄不存在：$subpath" }

    $defaultBranch = $syncedBranch
    if ([string]::IsNullOrWhiteSpace($defaultBranch)) { $defaultBranch = Get-AITeamRemoteDefaultBranch -RepoPath $RepoPath }

    $versionFile = ""
    if ($null -ne $Existing -and $null -ne $Existing.PSObject.Properties['version_file'] -and -not [string]::IsNullOrWhiteSpace([string]$Existing.version_file)) {
        if (Test-Path -LiteralPath (Join-Path $projectFull ([string]$Existing.version_file)) -PathType Leaf) { $versionFile = [string]$Existing.version_file }
    }
    if ([string]::IsNullOrWhiteSpace($versionFile)) {
        foreach ($candidate in @("VERSION","VERSION.txt","version.txt")) {
            if (Test-Path -LiteralPath (Join-Path $projectFull $candidate) -PathType Leaf) { $versionFile = $candidate; break }
        }
    }

    $tagPrefix = ""
    if ($null -ne $Existing -and $null -ne $Existing.PSObject.Properties['tag_prefix']) { $tagPrefix = [string]$Existing.tag_prefix }
    if ([string]::IsNullOrWhiteSpace($tagPrefix)) { $tagPrefix = (($Name -replace '[^A-Za-z0-9]','').ToLowerInvariant() + "-v") }

    $verification = if ($null -ne $Existing -and $null -ne $Existing.PSObject.Properties['verification']) { @($Existing.verification) } else { @(Get-AITeamDefaultVerification $projectFull) }

    return [PSCustomObject]@{
        name = $Name.Trim()
        project_path = $projectFull
        repo_name = ($GitHubRepo.Split('/')[-1])
        repo_path = $RepoPath
        repo_subpath = $subpath
        github_repo = $GitHubRepo
        default_branch = $defaultBranch
        version_file = $versionFile
        tag_prefix = $tagPrefix
        verification = @($verification)
        active = $true
    }
}

function Repair-AITeamProjectRegistrations {
    $data = Get-AITeamProjectsData
    $changed = $false
    foreach ($p in @($data.projects)) {
        try {
            $physical = Join-Path ([string]$p.repo_path) ([string]$p.repo_subpath)
            if (-not (Test-Path -LiteralPath $physical -PathType Container)) { continue }
            if ($null -eq $p.PSObject.Properties['project_path']) { $p | Add-Member -NotePropertyName project_path -NotePropertyValue $physical; $changed=$true }
            if ($null -eq $p.PSObject.Properties['github_repo']) {
                $remote = Get-GitOutput -RepoPath ([string]$p.repo_path) -Arguments @("config","--get","remote.origin.url") -AllowFailure
                $gh = if ($remote.ExitCode -eq 0) { Get-AITeamGitHubRepoNameFromRemote $remote.Text.Trim() } else { "" }
                $p | Add-Member -NotePropertyName github_repo -NotePropertyValue $gh; $changed=$true
            }
            if ($null -eq $p.PSObject.Properties['version_file']) { $v = if (Test-Path -LiteralPath (Join-Path $physical "VERSION") -PathType Leaf) { "VERSION" } else { "" }; $p | Add-Member -NotePropertyName version_file -NotePropertyValue $v; $changed=$true }
            if ($null -eq $p.PSObject.Properties['tag_prefix']) { $p | Add-Member -NotePropertyName tag_prefix -NotePropertyValue ((([string]$p.name -replace '[^A-Za-z0-9]','').ToLowerInvariant()) + "-v"); $changed=$true }
            if ($null -eq $p.PSObject.Properties['verification']) { $p | Add-Member -NotePropertyName verification -NotePropertyValue @(Get-AITeamDefaultVerification $physical); $changed=$true }
            if ($null -eq $p.PSObject.Properties['active']) { $p | Add-Member -NotePropertyName active -NotePropertyValue $true; $changed=$true }
        } catch {}
    }
    if ($changed) { Save-AITeamProjectsData $data }
}

function Get-AITeamProjectNames {
    $data = Get-AITeamProjectsData
    return @($data.projects | Where-Object { $_.active -ne $false } | ForEach-Object { [string]$_.name })
}

function Show-AITeamProjectEditor {
    param($Existing=$null,[System.Windows.Forms.IWin32Window]$Owner=$null)

    $form = New-Object System.Windows.Forms.Form
    $form.Text = if ($null -eq $Existing) { "AITeam - 新增專案" } else { "AITeam - 修改專案" }
    $form.StartPosition = "CenterParent"
    $form.Size = New-Object System.Drawing.Size(760,620)
    $form.MinimumSize = New-Object System.Drawing.Size(720,580)
    $form.Font = New-Object System.Drawing.Font("Microsoft JhengHei UI",10)

    $nameLabel = New-Object System.Windows.Forms.Label
    $nameLabel.Text = "專案名稱"
    $nameLabel.Location = New-Object System.Drawing.Point(20,18)
    $nameLabel.AutoSize = $true
    $form.Controls.Add($nameLabel)

    $nameBox = New-Object System.Windows.Forms.TextBox
    $nameBox.Location = New-Object System.Drawing.Point(20,42)
    $nameBox.Size = New-Object System.Drawing.Size(280,28)
    if ($null -ne $Existing) { $nameBox.Text = [string]$Existing.name }
    $form.Controls.Add($nameBox)

    $repoLabel = New-Object System.Windows.Forms.Label
    $repoLabel.Text = "GitHub Repo（可選已知 Repo，或直接輸入 owner/repo）"
    $repoLabel.Location = New-Object System.Drawing.Point(20,86)
    $repoLabel.AutoSize = $true
    $form.Controls.Add($repoLabel)

    $repoBox = New-Object System.Windows.Forms.ComboBox
    $repoBox.DropDownStyle = "DropDown"
    $repoBox.Location = New-Object System.Drawing.Point(20,110)
    $repoBox.Size = New-Object System.Drawing.Size(430,30)
    foreach ($repoName in @(Get-AITeamKnownGitHubRepos)) {
        if ($repoBox.Items.IndexOf([string]$repoName) -lt 0) {
            [void]$repoBox.Items.Add([string]$repoName)
        }
    }
    if ($null -ne $Existing -and $null -ne $Existing.PSObject.Properties['github_repo']) { $repoBox.Text = [string]$Existing.github_repo }
    $form.Controls.Add($repoBox)

    $loadRepo = New-Object System.Windows.Forms.Button
    $loadRepo.Text = "載入 Repo"
    $loadRepo.Location = New-Object System.Drawing.Point(465,108)
    $loadRepo.Size = New-Object System.Drawing.Size(105,34)
    $form.Controls.Add($loadRepo)

    $localLabel = New-Object System.Windows.Forms.Label
    $localLabel.Text = "本機位置：尚未載入（由 AITeam 自動分配，不需手動選擇）"
    $localLabel.Location = New-Object System.Drawing.Point(20,150)
    $localLabel.Size = New-Object System.Drawing.Size(700,28)
    $form.Controls.Add($localLabel)

    $treeLabel = New-Object System.Windows.Forms.Label
    $treeLabel.Text = "Repo 內的專案位置（若整個 Repo 就是一個專案，選最上方『Repo 根目錄』）"
    $treeLabel.Location = New-Object System.Drawing.Point(20,184)
    $treeLabel.AutoSize = $true
    $form.Controls.Add($treeLabel)

    $tree = New-Object System.Windows.Forms.TreeView
    $tree.Location = New-Object System.Drawing.Point(20,210)
    $tree.Size = New-Object System.Drawing.Size(700,285)
    $tree.Anchor = "Top,Bottom,Left,Right"
    $tree.HideSelection = $false
    $form.Controls.Add($tree)

    $selectedLabel = New-Object System.Windows.Forms.Label
    $selectedLabel.Text = "目前選擇：尚未選擇"
    $selectedLabel.Location = New-Object System.Drawing.Point(20,505)
    $selectedLabel.Size = New-Object System.Drawing.Size(700,28)
    $selectedLabel.Anchor = "Bottom,Left,Right"
    $form.Controls.Add($selectedLabel)

    $save = New-Object System.Windows.Forms.Button
    $save.Text = "儲存"
    $save.Location = New-Object System.Drawing.Point(515,540)
    $save.Size = New-Object System.Drawing.Size(95,36)
    $save.Anchor = "Bottom,Right"
    $form.Controls.Add($save)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "取消"
    $cancel.Location = New-Object System.Drawing.Point(625,540)
    $cancel.Size = New-Object System.Drawing.Size(95,36)
    $cancel.Anchor = "Bottom,Right"
    $form.Controls.Add($cancel)

    $script:ProjectEditorResult = $null
    $script:EditorRepoPath = ""

    $loadAction = {
        try {
            $gh = $repoBox.Text.Trim()
            if ([string]::IsNullOrWhiteSpace($gh)) { throw "請先選擇或輸入 GitHub Repo。" }
            $repoPath = Ensure-AITeamGitHubRepoLocal -GitHubRepo $gh
            Save-AITeamKnownGitHubRepo -GitHubRepo $gh
            if ($repoBox.Items.IndexOf($gh) -lt 0) {
                [void]$repoBox.Items.Add($gh)
            }
            $script:EditorRepoPath = $repoPath
            $dirs = @(Get-AITeamRepoDirectories -RepoPath $repoPath)
            $remoteBranch = Get-AITeamRemoteDefaultBranch -RepoPath $repoPath
            $localLabel.Text = "本機位置：" + $repoPath + "（AITeam 管理）｜目錄來源：origin/" + $remoteBranch

            $tree.BeginUpdate(); $tree.Nodes.Clear()
            $rootNode = New-Object System.Windows.Forms.TreeNode("Repo 根目錄")
            $rootNode.Tag = ""
            [void]$tree.Nodes.Add($rootNode)
            foreach ($dir in $dirs) { Add-AITeamTreePath -RootNode $rootNode -Path $dir }
            $rootNode.Expand()
            $tree.EndUpdate()

            $wanted = ""
            if ($null -ne $Existing -and [string]$Existing.github_repo -ieq $gh) { $wanted = [string]$Existing.repo_subpath }
            Select-AITeamTreeSubpath -Tree $tree -Subpath $wanted
            if ($null -eq $tree.SelectedNode) { $tree.SelectedNode = $rootNode }
            $selectedLabel.Text = "目前選擇：" + $(if ([string]::IsNullOrWhiteSpace([string]$tree.SelectedNode.Tag)) { "Repo 根目錄" } else { [string]$tree.SelectedNode.Tag })
        } catch {
            $tree.Nodes.Clear(); $script:EditorRepoPath = ""
            $localLabel.Text = "載入失敗：" + $_.Exception.Message
        }
    }

    $loadRepo.Add_Click({ & $loadAction })
    $repoBox.Add_KeyDown({ if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Enter) { & $loadAction } })
    $tree.Add_AfterSelect({
        if ($null -ne $tree.SelectedNode) {
            $sub = if ($null -eq $tree.SelectedNode.Tag) { "" } else { [string]$tree.SelectedNode.Tag }
            $selectedLabel.Text = "目前選擇：" + $(if ([string]::IsNullOrWhiteSpace($sub)) { "Repo 根目錄" } else { $sub })
        }
    })

    $save.Add_Click({
        try {
            if ([string]::IsNullOrWhiteSpace($nameBox.Text)) { throw "請填寫專案名稱。" }
            if ([string]::IsNullOrWhiteSpace($repoBox.Text)) { throw "請選擇 GitHub Repo。" }
            if ([string]::IsNullOrWhiteSpace($script:EditorRepoPath)) { throw "請先按『載入 Repo』。" }
            if ($null -eq $tree.SelectedNode) { throw "請選擇 Repo 內的專案位置。" }
            $sub = if ($null -eq $tree.SelectedNode.Tag) { "" } else { [string]$tree.SelectedNode.Tag }
            $script:ProjectEditorResult = Get-AITeamProjectRegistrationFromRepoSelection -Name $nameBox.Text.Trim() -GitHubRepo $repoBox.Text.Trim() -RepoPath $script:EditorRepoPath -RepoSubpath $sub -Existing $Existing
            $form.Close()
        } catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,"AITeam",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        }
    })

    $cancel.Add_Click({ $form.Close() })
    if ($null -ne $Existing) { $form.Add_Shown({ & $loadAction }) }

    if ($null -ne $Owner) { [void]$form.ShowDialog($Owner) } else { [void]$form.ShowDialog() }
    return $script:ProjectEditorResult
}

function Show-AITeamProjectManager {
    param([System.Windows.Forms.IWin32Window]$Owner=$null)

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "AITeam - 專案管理"
    $form.StartPosition = "CenterParent"
    $form.Size = New-Object System.Drawing.Size(860,480)
    $form.MinimumSize = New-Object System.Drawing.Size(800,440)
    $form.Font = New-Object System.Drawing.Font("Microsoft JhengHei UI",10)

    $note = New-Object System.Windows.Forms.Label
    $note.Text = "AITeam 專案以 GitHub Repo + Repo 子目錄為來源；本機 clone 位置由 AITeam 自動管理。『移除』只取消登錄，不刪 source / Repo / GitHub。"
    $note.Location = New-Object System.Drawing.Point(16,14)
    $note.Size = New-Object System.Drawing.Size(810,44)
    $form.Controls.Add($note)

    $list = New-Object System.Windows.Forms.ListView
    $list.View = "Details"; $list.FullRowSelect=$true; $list.GridLines=$true; $list.HideSelection=$false
    $list.Location = New-Object System.Drawing.Point(16,64)
    $list.Size = New-Object System.Drawing.Size(810,310)
    $list.Anchor = "Top,Bottom,Left,Right"
    [void]$list.Columns.Add("專案",130)
    [void]$list.Columns.Add("GitHub Repo",245)
    [void]$list.Columns.Add("Repo 子目錄",275)
    [void]$list.Columns.Add("版本檔",120)
    $form.Controls.Add($list)

    $add = New-Object System.Windows.Forms.Button; $add.Text="新增"; $add.Location=New-Object System.Drawing.Point(16,390); $add.Size=New-Object System.Drawing.Size(90,34); $add.Anchor="Bottom,Left"; $form.Controls.Add($add)
    $edit = New-Object System.Windows.Forms.Button; $edit.Text="修改"; $edit.Location=New-Object System.Drawing.Point(116,390); $edit.Size=New-Object System.Drawing.Size(90,34); $edit.Anchor="Bottom,Left"; $form.Controls.Add($edit)
    $remove = New-Object System.Windows.Forms.Button; $remove.Text="移除登錄"; $remove.Location=New-Object System.Drawing.Point(216,390); $remove.Size=New-Object System.Drawing.Size(110,34); $remove.Anchor="Bottom,Left"; $form.Controls.Add($remove)
    $close = New-Object System.Windows.Forms.Button; $close.Text="關閉"; $close.Location=New-Object System.Drawing.Point(736,390); $close.Size=New-Object System.Drawing.Size(90,34); $close.Anchor="Bottom,Right"; $form.Controls.Add($close)

    function Refresh-ProjectList {
        $list.Items.Clear()
        $data = Get-AITeamProjectsData
        foreach ($p in @($data.projects)) {
            $item = New-Object System.Windows.Forms.ListViewItem([string]$p.name)
            $gh = if ($null -ne $p.PSObject.Properties['github_repo']) { [string]$p.github_repo } else { [string]$p.repo_name }
            [void]$item.SubItems.Add($gh)
            $sub = if ([string]::IsNullOrWhiteSpace([string]$p.repo_subpath)) { "(Repo 根目錄)" } else { [string]$p.repo_subpath }
            [void]$item.SubItems.Add($sub)
            $vf = if ($null -ne $p.PSObject.Properties['version_file']) { [string]$p.version_file } else { "" }
            [void]$item.SubItems.Add($vf)
            $item.Tag = [string]$p.name
            [void]$list.Items.Add($item)
        }
    }

    function Get-SelectedProject {
        if ($list.SelectedItems.Count -eq 0) { return $null }
        $name = [string]$list.SelectedItems[0].Tag
        $data = Get-AITeamProjectsData
        return @($data.projects | Where-Object { $_.name -eq $name }) | Select-Object -First 1
    }

    $add.Add_Click({
        $new = Show-AITeamProjectEditor -Owner $form
        if ($null -eq $new) { return }
        $data = Get-AITeamProjectsData
        if (@($data.projects | Where-Object { $_.name -ieq $new.name }).Count -gt 0) { [System.Windows.Forms.MessageBox]::Show("已有同名專案：$($new.name)","AITeam") | Out-Null; return }
        $data.projects = @($data.projects) + @($new); Save-AITeamProjectsData $data; Refresh-ProjectList
    })

    $edit.Add_Click({
        $old = Get-SelectedProject
        if ($null -eq $old) { [System.Windows.Forms.MessageBox]::Show("請先選擇要修改的專案。","AITeam") | Out-Null; return }
        $updated = Show-AITeamProjectEditor -Existing $old -Owner $form
        if ($null -eq $updated) { return }
        $data = Get-AITeamProjectsData
        if (@($data.projects | Where-Object { $_.name -ieq $updated.name -and $_.name -ine $old.name }).Count -gt 0) { [System.Windows.Forms.MessageBox]::Show("已有同名專案：$($updated.name)","AITeam") | Out-Null; return }
        $newProjects=@(); foreach ($p in @($data.projects)) { if ([string]$p.name -eq [string]$old.name) { $newProjects += $updated } else { $newProjects += $p } }
        $data.projects=@($newProjects); Save-AITeamProjectsData $data; Refresh-ProjectList
    })

    $remove.Add_Click({
        $old = Get-SelectedProject
        if ($null -eq $old) { [System.Windows.Forms.MessageBox]::Show("請先選擇要移除的專案。","AITeam") | Out-Null; return }
        $msg = "只會從 AITeam 專案清單移除「$($old.name)」。`r`n`r`n不會刪除任何原始碼、Git Repo、GitHub Repo 或其他電腦資料。`r`n`r`n確定移除登錄？"
        if ([System.Windows.Forms.MessageBox]::Show($msg,"AITeam",[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Warning) -ne [System.Windows.Forms.DialogResult]::Yes) { return }
        $data=Get-AITeamProjectsData; $data.projects=@($data.projects | Where-Object { $_.name -ne $old.name }); Save-AITeamProjectsData $data; Refresh-ProjectList
    })

    $close.Add_Click({ $form.Close() })
    Refresh-ProjectList
    if ($null -ne $Owner) { [void]$form.ShowDialog($Owner) } else { [void]$form.ShowDialog() }
}

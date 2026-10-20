using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 自動更新（跟 CYERPAutoInput 同一套做法）：下載比較新的正式 Release、核對 SHA-256、
/// 解壓到暫存區，等 AITeam 關掉之後把新的檔案蓋上去。家裡的資料（data、repos、history……）
/// 不在更新包裡，不會被動到。
/// </summary>
internal sealed class AppUpdater
{
    private const string Program = "AITeam";
    private static readonly HttpClient Http = CreateClient();
    private readonly string _runtimeRoot;

    public AppUpdater(string runtimeRoot) => _runtimeRoot = runtimeRoot;

    public static Version? CurrentVersion => AppUpdateCheck.CurrentVersion(
        typeof(AppUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Program, CurrentVersion?.ToString(3) ?? "0.0.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>有比較新的 Release 就下載、核對、解壓，回傳可以蓋上去的資料夾；沒有就回 null。</summary>
    public async Task<(ReleaseOffer Offer, string Folder)?> PrepareAsync(CancellationToken cancellationToken, Action<ReleaseOffer>? found = null)
    {
        if (CurrentVersion is not { } current) return null;

        var json = await Http.GetStringAsync(AppUpdateCheck.ReleasesUrl, cancellationToken);
        if (AppUpdateCheck.Newest(json, current) is not { } offer) return null;
        found?.Invoke(offer);

        var work = Path.Combine(_runtimeRoot, "temp", $"update-V{offer.Version.ToString(3)}");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        var zip = Path.Combine(work, offer.ZipName);
        var checksum = await Http.GetStringAsync(offer.ChecksumUrl, cancellationToken);
        var expected = AppUpdateCheck.ExpectedSha256(checksum, offer.ZipName)
            ?? throw new InvalidDataException("Release 的 SHA-256 檔裡找不到這個 zip 的雜湊。");

        await using (var source = await Http.GetStreamAsync(offer.ZipUrl, cancellationToken))
        await using (var target = File.Create(zip))
            await source.CopyToAsync(target, cancellationToken);

        string actual;
        await using (var stream = File.OpenRead(zip))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (actual != expected)
            throw new InvalidDataException($"下載的更新檔 SHA-256 不符（應為 {expected[..12]}，實際 {actual[..12]}），不會使用。");

        var unpacked = Path.Combine(work, "unpacked");
        ZipFile.ExtractToDirectory(zip, unpacked);
        var folder = Path.Combine(unpacked, offer.PackageFolder);
        if (!File.Exists(Path.Combine(folder, Program + ".exe")))
            throw new InvalidDataException($"更新檔裡沒有 {offer.PackageFolder}/{Program}.exe。");

        return (offer, folder);
    }

    /// <summary>更新腳本把結果留在這裡，下一次啟動時讀（暫存區不在更新包裡）。</summary>
    public string OutcomePath => Path.Combine(_runtimeRoot, "temp", "update-result.txt");

    /// <summary>上一次更新的結果，只讀一次：讀完就刪掉。</summary>
    public UpdateOutcome? TakeOutcome()
    {
        try
        {
            if (!File.Exists(OutcomePath)) return null;
            var outcome = UpdateOutcome.Parse(File.ReadAllText(OutcomePath));
            File.Delete(OutcomePath);
            return outcome;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 啟動一個顯示「正在更新」的小視窗（PowerShell 腳本），等 AITeam 結束之後：先備份會被蓋掉的檔案，
    /// 再把新版蓋上去（檔案被鎖住會重試）；蓋不上去就把備份還原，舊版照樣能開。
    /// 結果寫在 <see cref="OutcomePath"/>。<paramref name="restart"/> 時不論成功與否都會重新開啟 AITeam。
    /// 呼叫端接著要把 AITeam 關掉。
    /// </summary>
    public void ApplyAfterExit(string folder, Version version, bool restart)
    {
        var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exe = Environment.ProcessPath ?? Path.Combine(target, Program + ".exe");
        var work = Path.GetDirectoryName(Path.GetDirectoryName(folder)!)!;
        var script = Path.Combine(work, "apply-update.ps1");
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

        File.WriteAllText(script, ApplyScript
            .Replace("@PID@", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("@SOURCE@", Quote(folder))
            .Replace("@TARGET@", Quote(target))
            .Replace("@EXE@", Quote(exe))
            .Replace("@WORK@", Quote(work))
            .Replace("@OUTCOME@", Quote(OutcomePath))
            .Replace("@PROGRAM@", Quote(Program))
            .Replace("@VERSION@", Quote(version.ToString(3)))
            .Replace("@RESTART@", restart ? "$true" : "$false")
            .Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));

        // 直接啟動 PowerShell 並帶 CreateNoWindow：主控台視窗不會出現，只看得到腳本自己的「正在更新」小視窗。
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = work
        });
    }

    /// <summary><see cref="ApplyAfterExit"/> 執行的腳本；@NAME@ 會被替換成加了引號的值。</summary>
    private const string ApplyScript = """
        $ErrorActionPreference = 'Stop'
        $pidToWait = @PID@
        $source = @SOURCE@
        $target = @TARGET@
        $exe = @EXE@
        $work = @WORK@
        $outcome = @OUTCOME@
        $program = @PROGRAM@
        $version = @VERSION@
        $restart = @RESTART@
        $form = $null
        function Pump { if ($form) { [System.Windows.Forms.Application]::DoEvents() } }
        function Say([string]$text) { if ($form) { $label.Text = $text; Pump } }
        function WaitMs([int]$ms) { $until = (Get-Date).AddMilliseconds($ms); while ((Get-Date) -lt $until) { Pump; Start-Sleep -Milliseconds 40 } }
        function Retry([scriptblock]$action, [int]$times) {
          for ($i = 1; $i -le $times; $i++) { try { & $action; return } catch { if ($i -eq $times) { throw }; WaitMs 1000 } }
        }
        try {
          Add-Type -AssemblyName System.Windows.Forms, System.Drawing
          [System.Windows.Forms.Application]::EnableVisualStyles()
          $form = New-Object System.Windows.Forms.Form
          $form.Text = "$program 更新"
          $form.FormBorderStyle = 'FixedDialog'; $form.ControlBox = $false; $form.TopMost = $true
          $form.StartPosition = 'CenterScreen'; $form.ShowInTaskbar = $true
          $form.ClientSize = New-Object System.Drawing.Size(440, 130)
          $form.Font = New-Object System.Drawing.Font('Microsoft JhengHei UI', 10)
          $form.BackColor = [System.Drawing.Color]::FromArgb(244, 247, 250)
          $label = New-Object System.Windows.Forms.Label
          $label.SetBounds(22, 18, 396, 50)
          $bar = New-Object System.Windows.Forms.ProgressBar
          $bar.SetBounds(22, 80, 396, 18); $bar.Style = 'Marquee'; $bar.MarqueeAnimationSpeed = 30
          $form.Controls.AddRange(@($label, $bar))
          $label.Text = "正在更新 $program 到 V$version，請稍候…`n請不要關機或開啟本程式。"
          $form.Show(); $form.Activate(); Pump
        } catch { $form = $null }

        $ok = $false; $detail = ''
        try {
          $process = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
          $deadline = (Get-Date).AddSeconds(120)
          while ($process -and -not $process.HasExited -and (Get-Date) -lt $deadline) { Pump; Start-Sleep -Milliseconds 40 }
          $files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
          $backup = Join-Path $work 'backup'
          $saved = New-Object System.Collections.Generic.List[string]
          foreach ($file in $files) {
            $relative = $file.FullName.Substring($source.Length).TrimStart('\', '/')
            $current = Join-Path $target $relative
            if (Test-Path -LiteralPath $current -PathType Leaf) {
              $copy = Join-Path $backup $relative
              New-Item -ItemType Directory -Force -Path (Split-Path $copy) | Out-Null
              Retry { Copy-Item -LiteralPath $current -Destination $copy -Force } 10
              $saved.Add($relative)
            }
            Pump
          }
          if ($form) { $bar.Style = 'Continuous'; $bar.Maximum = [Math]::Max(1, $files.Count); $bar.Value = 0 }
          try {
            $n = 0
            foreach ($file in $files) {
              $relative = $file.FullName.Substring($source.Length).TrimStart('\', '/')
              $destination = Join-Path $target $relative
              New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
              Retry { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force } 30
              $n++; if ($form) { $bar.Value = $n }; Pump
            }
            $ok = $true
          } catch {
            $detail = "複製失敗：$($_.Exception.Message)"
            Say "更新失敗，正在還原原本的版本…"
            try {
              foreach ($relative in $saved) { Retry { Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination (Join-Path $target $relative) -Force } 10 }
              $detail += "；已還原原本的版本"
            } catch { $detail += "；還原也失敗：$($_.Exception.Message)" }
          }
        } catch {
          if (-not $detail) { $detail = "更新前準備失敗：$($_.Exception.Message)" }
        }
        try {
          New-Item -ItemType Directory -Force -Path (Split-Path $outcome) | Out-Null
          Set-Content -LiteralPath $outcome -Value @($(if ($ok) { 'ok' } else { 'failed' }), $version, $detail) -Encoding UTF8
        } catch { }
        if ($ok) { Say "更新完成，正在開啟 $program V$version…"; WaitMs 1200 }
        else { Say "更新失敗，正在開啟原本的版本…`n可能需要手動更新。"; WaitMs 3000 }
        if ($restart) { try { Start-Process -FilePath $exe -WorkingDirectory $target } catch { } }
        if ($form) { $form.Close() }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        """;
}

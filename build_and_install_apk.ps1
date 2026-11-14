<#
    一键构建并安装 MateSoul Android APK
    用法：双击同目录下的「一键打包安装APK.cmd」，或在 PowerShell 中运行本脚本。
    可选参数示例：
        .\build_and_install_apk.ps1 -NoLaunch          # 只构建安装，不自动启动
        .\build_and_install_apk.ps1 -SkipBuild         # 跳过构建，直接安装现有 APK
        .\build_and_install_apk.ps1 -UnityExe "D:\Unity\2022.3.38f1\Editor\Unity.exe"
#>
[CmdletBinding()]
param(
    [string]$UnityExe  = "D:\Unity\2022.3.38f1\Editor\Unity.exe",
    [string]$AdbPath   = "D:\op\adb.exe",
    [string]$PackageId = "com.matesoul.game",
    [string]$Activity  = "com.matesoul.game/com.unity3d.player.UnityPlayerActivity",
    [switch]$NoLaunch,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$project = Join-Path $root 'MateSoul'
$builds  = Join-Path $root 'Builds'
$stamp   = Get-Date -Format 'yyyyMMdd_HHmmss'
$log     = Join-Path $builds ("build_" + $stamp + ".log")
$apk     = Join-Path $builds 'MateSoul_Framework.apk'

function Write-Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Write-Note($msg) { Write-Host "    $msg" -ForegroundColor DarkGray }
function Write-Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }
function Abort($msg) {
    Write-Host ""
    Write-Host "[失败] $msg" -ForegroundColor Red
    exit 1
}

# 读取被 Unity 持续写入的日志（允许共享读写）
function Read-BuildLog([string]$path) {
    if (-not (Test-Path $path)) { return @() }
    try {
        $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $text = $sr.ReadToEnd()
        $sr.Dispose(); $fs.Dispose()
        return ($text -split "`r?`n")
    } catch {
        return @()
    }
}

# 调用 adb：adb 会把正常提示写到 stderr，全局 Stop 模式会误判为致命错误
function Invoke-Adb([string[]]$Command) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $AdbPath @Command 2>&1
        return ($out | Out-String)
    } finally {
        $ErrorActionPreference = $old
    }
}

Write-Host "MateSoul 一键打包并安装" -ForegroundColor White
Write-Host "工程: $project" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 1. 环境检查
Write-Step "1/5 检查环境"
if (-not (Test-Path $UnityExe)) { Abort "找不到 Unity：$UnityExe（可用 -UnityExe 参数指定）" }
if (-not (Test-Path $project))  { Abort "找不到工程目录：$project" }
if (-not (Test-Path $AdbPath))  { Abort "找不到 adb：$AdbPath（可用 -AdbPath 参数指定）" }
if (-not (Test-Path $builds))   { New-Item -ItemType Directory -Path $builds | Out-Null }

$unityRunning = Get-Process Unity -ErrorAction SilentlyContinue
if ($unityRunning) {
    $ids = ($unityRunning | ForEach-Object { $_.Id }) -join ', '
    Abort "检测到 Unity 正在运行（PID: $ids）。它若占着本工程，构建会立刻失败（project already open），请先关闭 Unity。"
}
$lockFile = Join-Path $project 'Temp\UnityLockfile'
if (Test-Path $lockFile) {
    Remove-Item $lockFile -Force -ErrorAction SilentlyContinue
    if (Test-Path $lockFile) { Write-Warn "存在残留工程锁且无法删除：$lockFile" }
    else { Write-Warn "已清理上次异常退出残留的 UnityLockfile" }
}
Write-Ok "Unity：$UnityExe"

# ------------------------------------------------- 2. 预热 SDK 组件清单（防卡死）
Write-Step "2/5 预热 Android SDK 清单"
$editor = Split-Path $UnityExe -Parent
$jdk    = Join-Path $editor 'Data\PlaybackEngines\AndroidPlayer\OpenJDK'
$sdk    = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT }
          elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME }
          else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$java    = Join-Path $jdk 'bin\java.exe'
$cmdline = Join-Path $sdk 'cmdline-tools\latest'

if ((Test-Path $java) -and (Test-Path $cmdline)) {
    $oldOpt = $env:JAVA_TOOL_OPTIONS
    # 关键：不强制 IPv4 时 sdkmanager 的 HTTPS 请求可能长时间挂起
    $env:JAVA_TOOL_OPTIONS = '-Djava.net.preferIPv4Stack=true'
    try {
        $p = Start-Process -FilePath $java -ArgumentList @(
            "-Dcom.android.sdklib.toolsdir=$cmdline\bin\..",
            '-classpath', "$cmdline\bin\..\lib\sdkmanager-classpath.jar",
            'com.android.sdklib.tool.sdkmanager.SdkManagerCli', '--list'
        ) -RedirectStandardOutput (Join-Path $env:TEMP 'matesoul_sdklists.txt') `
          -RedirectStandardError  (Join-Path $env:TEMP 'matesoul_sdklists_err.txt') `
          -PassThru -NoNewWindow

        if (-not $p.WaitForExit(150000)) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            Abort "SDK 组件清单拉取超时：本机到 Android SDK 源不通。继续构建会在「Detecting Android SDK」阶段无限挂起，已中止。请检查代理/VPN 后重试。"
        } else {
            Write-Ok "SDK 清单就绪（$sdk）"
        }
    } catch {
        Write-Warn "预热失败，跳过：$($_.Exception.Message)"
    } finally {
        if ($oldOpt) { $env:JAVA_TOOL_OPTIONS = $oldOpt }
        else { Remove-Item Env:JAVA_TOOL_OPTIONS -ErrorAction SilentlyContinue }
    }
} else {
    Write-Warn "未找到内置 JDK 或 cmdline-tools，跳过预热。"
}

# ------------------------------------------------------------------ 3. 构建
Write-Step "3/5 构建 APK（通常 8-15 分钟，请勿关闭窗口）"
if ($SkipBuild) {
    if (-not (Test-Path $apk)) { Abort "指定了 -SkipBuild，但未找到已有 APK：$apk" }
    Write-Warn "已按 -SkipBuild 跳过构建，使用现有 APK"
} else {
Write-Note "日志：$log"

$unityArgs = @(
    '-batchmode', '-quit', '-buildTarget', 'Android',
    '-projectPath', $project,
    '-executeMethod', 'MateSoul.Editor.GameSceneBuilder.BuildCurrentSceneAndroidRelease',
    '-logFile', $log
)

$sw = [Diagnostics.Stopwatch]::StartNew()

# 关键：通过 WMI 让 Unity 脱离当前进程树启动。
# 若直接用 Start-Process 启动，Unity 的 IL Post Processor 会反复报
# "Connectivity with IL Post Processor runner cannot be established yet" 而卡死。
$quoted  = foreach ($a in $unityArgs) { if ($a -match '\s') { '"' + $a + '"' } else { $a } }
$cmdLine = '"' + $UnityExe + '" ' + ($quoted -join ' ')
$launch  = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine      = $cmdLine
    CurrentDirectory = $project
}
if ($launch.ReturnValue -ne 0 -or -not $launch.ProcessId) {
    Abort "无法启动 Unity（返回码 $($launch.ReturnValue)）"
}
$unityPid = [int]$launch.ProcessId
Write-Note "Unity 进程 PID：$unityPid"

$printed  = 0
$lastProgress = ''
$lastGrowth = Get-Date
$lastBeat = Get-Date
$timeoutMin = 45

$filter = 'DisplayProgressbar|Build Finished|error CS|Exception|Build Failed|HandleProjectAlreadyOpen'
while (Get-Process -Id $unityPid -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds 3
    $lines = Read-BuildLog $log
    if ($lines.Count -gt $printed) {
        for ($i = $printed; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $filter) { Write-Note $lines[$i].Trim() }
            if ($lines[$i] -match 'DisplayProgressbar') { $lastProgress = $lines[$i].Trim() }
        }
        $printed = $lines.Count
        $lastGrowth = Get-Date
        $lastBeat = Get-Date
    } elseif (((Get-Date) - $lastBeat).TotalSeconds -ge 30) {
        Write-Note ("仍在构建… 已 " + [int]$sw.Elapsed.TotalMinutes + " 分钟")
        $lastBeat = Get-Date
    }
    # 已知卡点：SDK 联网检测正常只需几秒，超过 3 分钟无新日志即判定挂起
    if ($lastProgress -match 'Detecting Android SDK' -and ((Get-Date) - $lastGrowth).TotalSeconds -gt 180) {
        Stop-Process -Id $unityPid -Force -ErrorAction SilentlyContinue
        Abort "构建卡在「Detecting Android SDK」超过 3 分钟：Unity 调用的 sdkmanager 联网挂起（非代码问题）。已终止。请检查代理/VPN 后重试。日志：$log"
    }
    if ($sw.Elapsed.TotalMinutes -gt $timeoutMin) {
        Stop-Process -Id $unityPid -Force -ErrorAction SilentlyContinue
        Abort "构建超过 $timeoutMin 分钟仍未结束，已终止。请查看日志：$log"
    }
}
Start-Sleep -Seconds 2
$sw.Stop()
Write-Note ("构建耗时 " + [int]$sw.Elapsed.TotalMinutes + " 分 " + $sw.Elapsed.Seconds + " 秒")

$finalLines = Read-BuildLog $log
$logText    = $finalLines -join "`n"
if ($logText -match 'HandleProjectAlreadyOpenInAnotherInstance') {
    Abort "工程被另一个 Unity 实例占用（project already open）。请关闭所有 Unity 后重试。日志：$log"
}
if ($logText -notmatch 'Build Finished, Result: Success') {
    Write-Host ""
    $bad = $finalLines | Select-String -Pattern 'error CS|Exception:|Build Failed|FAILED|Aborting batchmode|Exiting batchmode' -CaseSensitive | Select-Object -Last 10
    if ($bad) {
        Write-Host "构建错误摘要：" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host ("    " + $_.Line.Trim()) -ForegroundColor Red }
    }
    Abort "构建失败，完整日志：$log"
}
if (-not (Test-Path $apk)) { Abort "构建报告成功，但未找到 APK：$apk" }
$apkInfo = Get-Item $apk
Write-Ok ("APK 就绪：" + [math]::Round($apkInfo.Length / 1MB, 1) + " MB（" + $apkInfo.LastWriteTime.ToString("HH:mm:ss") + "）")
}

# ------------------------------------------------------------ 4. 安装到设备
Write-Step "4/5 安装到设备"
$devicesRaw = Invoke-Adb @('devices')
$devLines = @(($devicesRaw -split "`r?`n") | Where-Object { $_ -match '^\S+\s+device\s*$' })
if (-not $devLines) {
    if ($devicesRaw -match 'unauthorized') { Abort "设备未授权：请在头显里点「允许 USB 调试」后重试。" }
    Abort "没有检测到已连接的设备，请检查 USB 线。adb devices 输出：`n$devicesRaw"
}
$serial = ($devLines[0] -split '\s+')[0]
if ($devLines.Count -gt 1) { Write-Warn "检测到多台设备，使用第一台：$serial" }
Write-Ok "设备：$serial"

$res = Invoke-Adb @('-s', $serial, 'install', '-r', $apk)
Write-Note $res
if ($res -match 'signatures do not match|INSTALL_FAILED_UPDATE_INCOMPATIBLE') {
    Write-Warn "旧版本由不同签名打包，需先卸载（应用数据会清空）。"
    Invoke-Adb @('-s', $serial, 'uninstall', $PackageId) | Out-Null
    $res = Invoke-Adb @('-s', $serial, 'install', '-r', $apk)
    Write-Note $res
}
if ($res -notmatch 'Success') { Abort "安装失败：`n$res" }
Write-Ok "安装成功"

# ------------------------------------------------------- 5. 启动并检查崩溃
Write-Step "5/5 启动应用并检查"
$pkgInfo = Invoke-Adb @('-s', $serial, 'shell', "dumpsys package $PackageId")
foreach ($k in 'versionName', 'lastUpdateTime') {
    $m = [regex]::Match($pkgInfo, "$k=(\S+)")
    if ($m.Success) { Write-Ok ("{0} = {1}" -f $k, $m.Groups[1].Value) }
}

if ($NoLaunch) {
    Write-Ok "已按 -NoLaunch 跳过启动"
} else {
    Invoke-Adb @('-s', $serial, 'logcat', '-c') | Out-Null
    Invoke-Adb @('-s', $serial, 'shell', "am force-stop $PackageId") | Out-Null
    Invoke-Adb @('-s', $serial, 'shell', "am start -n $Activity") | Out-Null
    Start-Sleep -Seconds 12

    $running = Invoke-Adb @('-s', $serial, 'shell', "ps -A | grep $PackageId")
    if ($running -match $PackageId) {
        Write-Ok "进程已运行"
    } else {
        Write-Warn "未检测到进程（可能被系统回收或启动失败）"
    }
    $fatal = (Invoke-Adb @('-s', $serial, 'logcat', '-d', '-t', '500', '-v', 'brief') -split "`r?`n") |
             Select-String -Pattern 'FATAL EXCEPTION|E AndroidRuntime' | Select-Object -First 5
    if ($fatal) {
        Write-Host ""
        Write-Host "检测到崩溃日志：" -ForegroundColor Red
        $fatal | ForEach-Object { Write-Host ("    " + $_.Line.Trim()) -ForegroundColor Red }
        exit 1
    } else {
        Write-Ok "无崩溃日志"
    }
}

Write-Host ""
Write-Host "全部完成：新版本已安装到设备 $serial" -ForegroundColor Green
exit 0

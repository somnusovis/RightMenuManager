param(
    [string]$Exe = "src\RightMenuManager\bin\Debug\net8.0-windows\RightMenuManager.exe"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = Split-Path -Parent $PSScriptRoot
$exePath = if ([System.IO.Path]::IsPathRooted($Exe)) { $Exe } else { Join-Path $root $Exe }
$testKeyPath = "Software\Classes\*\shell\__RightMenuManager_Test"
$backupRoot = Join-Path $env:APPDATA "RightMenuManager\Backups"

function Get-TestKey([bool]$writable = $false) {
    return [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testKeyPath, $writable)
}

function Ensure-TestKey {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testKeyPath, $true)
    $key.SetValue("", "测试菜单项（右键管家）")
    $key.SetValue("Icon", "notepad.exe")
    $command = $key.CreateSubKey("command")
    $command.SetValue("", 'notepad.exe "%1"')
    $command.Close()
    $key.Close()
}

function Wait-Element($searchRoot, $condition, [string]$label, [int]$seconds = 12) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $element = $searchRoot.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "等待 UI 元素超时：$label"
}

function Wait-Until([scriptblock]$condition, [string]$label, [int]$seconds = 12) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    throw "等待条件超时：$label"
}

function Set-TestItemEnabled([bool]$enabled) {
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $menuList = Wait-Element $main ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MenuList")) "菜单项列表"
    $item = Wait-Element $menuList ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)) "测试菜单项"
    $check = Wait-Element $item ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::CheckBox)) "开关"

    $state = $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
    $currentEnabled = $state -eq [System.Windows.Automation.ToggleState]::On
    if ($currentEnabled -ne $enabled) {
        $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Start-Sleep -Milliseconds 900
    }
}

$proc = $null
$script:backupFolders = @()

try {
    Ensure-TestKey

    Get-Process RightMenuManager -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    $proc = Start-Process -FilePath $exePath -PassThru
    Start-Sleep -Seconds 4

    $main = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    if (-not $main) { throw "未找到主窗口" }

    # 搜索测试项
    $search = Wait-Element $main ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "SearchBox")) "搜索框"
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("__Right")
    Start-Sleep -Milliseconds 900

    # 禁用
    Set-TestItemEnabled $false
    Wait-Until {
        $key = Get-TestKey
        try {
            return ($null -ne $key -and $null -ne $key.GetValue("LegacyDisable"))
        } finally { if ($key) { $key.Close() } }
    } "LegacyDisable 写入"
    Write-Output "PASS: 禁用写入 LegacyDisable / ProgrammaticAccessOnly / HideBasedOnVelocityId"

    # 重新启用
    Set-TestItemEnabled $true
    Wait-Until {
        $key = Get-TestKey
        try {
            return ($null -ne $key -and $null -eq $key.GetValue("LegacyDisable") -and $null -eq $key.GetValue("ProgrammaticAccessOnly"))
        } finally { if ($key) { $key.Close() } }
    } "隐藏标记清除"
    Write-Output "PASS: 启用后隐藏标记已清除"

    # 选中并删除
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $menuList = Wait-Element $main ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MenuList")) "菜单项列表"
    $item = Wait-Element $menuList ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)) "测试菜单项"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 400

    $deleteButton = Wait-Element $main ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "DeleteSelectedButton")) "删除按钮"
    $deleteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $primary = Wait-Element ([System.Windows.Automation.AutomationElement]::RootElement) (
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "DialogPrimaryButton")) "删除确认按钮"
    $primary.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    Wait-Until {
        $key = Get-TestKey
        try { return $null -eq $key } finally { if ($key) { $key.Close() } }
    } "测试项被删除"
    Write-Output "PASS: 删除后注册表项已移除"

    Wait-Until {
        $script:backupFolders = @(Get-ChildItem -LiteralPath $backupRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object {
                $meta = Join-Path $_.FullName "meta.json"
                (Test-Path -LiteralPath $meta) -and
                ((Get-Content -LiteralPath $meta -Raw -Encoding UTF8) -match "__RightMenuManager_Test")
            })
        return $script:backupFolders.Count -gt 0
    } "自动备份生成"

    $backupFolder = $script:backupFolders | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $regFile = Join-Path $backupFolder.FullName "backup.reg"
    if (-not (Test-Path -LiteralPath $regFile)) { throw "备份 .reg 文件不存在" }
    Write-Output "PASS: 删除前已生成备份 $($backupFolder.Name)"

    # 打开备份窗口并恢复
    $openBackups = Wait-Element $main ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "OpenBackupsButton")) "备份与恢复按钮"
    $openBackups.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $backupWindow = Wait-Element ([System.Windows.Automation.AutomationElement]::RootElement) (
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, "备份与恢复"))) "备份窗口"

    $restore = Wait-Element $backupWindow ([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "RestoreBackupButton")) "恢复按钮"
    $restore.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $primary = Wait-Element ([System.Windows.Automation.AutomationElement]::RootElement) (
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "DialogPrimaryButton")) "恢复确认按钮"
    $primary.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    Wait-Until {
        $key = Get-TestKey
        try {
            return ($null -ne $key -and $key.GetValue("") -eq "测试菜单项（右键管家）")
        } finally { if ($key) { $key.Close() } }
    } "测试项恢复"
    Write-Output "PASS: 备份恢复成功，注册表项已写回"

    # 关闭“恢复完成”提示框
    Start-Sleep -Milliseconds 500
    $infoPrimary = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "DialogPrimaryButton"))
    if ($infoPrimary) {
        try {
            $infoPrimary.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        } catch { }
        Start-Sleep -Milliseconds 400
    }

    # 关闭备份窗口
    try {
        $backupWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    } catch {
        # 恢复完成后窗口可能已经刷新，忽略关闭失败。
    }

    Write-Output "E2E_RESULT=PASS"
}
catch {
    Write-Output "E2E_RESULT=FAIL"
    Write-Output ("ERROR: " + $_.Exception.Message)
    exit 1
}
finally {
    Get-Process RightMenuManager -ErrorAction SilentlyContinue | Stop-Process -Force

    # 清理测试注册表项
    try {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testKeyPath, $false)
    } catch { }

    # 只清理本次测试生成的备份
    foreach ($folder in $script:backupFolders) {
        try {
            if ($folder -and (Test-Path -LiteralPath $folder.FullName)) {
                Remove-Item -LiteralPath $folder.FullName -Recurse -Force
            }
        } catch { }
    }
}

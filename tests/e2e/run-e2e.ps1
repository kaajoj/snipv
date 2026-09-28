# End-to-end test for snipv. Drives the real app through UI Automation and the
# real keyboard/clipboard, so DO NOT touch mouse/keyboard while it runs (~1 min).
#
# Covers: add (with shortcut capture), global hotkey -> clipboard, reserved-
# shortcut block, reassigning a taken shortcut (the previous holder is parked
# without one), edit, hotkey re-registration after edit, remove, close-to-tray
# and single instance.
#
# Usage: powershell -ExecutionPolicy Bypass -File tests\e2e\run-e2e.ps1

param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\bin\Release\net10.0-windows10.0.19041.0\win-x64\snipv.exe'),
    [string]$SnippetsJson = "$env:LOCALAPPDATA\snipv\snippets.json"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
}
"@

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]::Descendants
$CT = [System.Windows.Automation.ControlType]
$script:failures = @()

function Assert($condition, $message) {
    if ($condition) { Write-Host "  OK  $message" -ForegroundColor Green }
    else { Write-Host "  FAIL $message" -ForegroundColor Red; $script:failures += $message }
}
function Cond($prop, $value) { New-Object System.Windows.Automation.PropertyCondition($prop, $value) }
function FindName($root, $name) { $root.FindFirst($TS, (Cond $AE::NameProperty $name)) }
function FindType($root, $type) { @($root.FindAll($TS, (Cond $AE::ControlTypeProperty $type))) }
function Get-Edits($win) { (FindType $win $CT::Edit) | Sort-Object { $_.Current.BoundingRectangle.Top } }
function Set-Value($el, $v) { ($el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($v) }
function Get-Value($el) { ($el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value }
function Click($el) { ($el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
function Press-Chord([byte[]]$keys, [int]$holdMs = 350) {
    foreach ($k in $keys) { [Win]::keybd_event($k, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
    Start-Sleep -Milliseconds $holdMs
    $rev = $keys.Clone(); [array]::Reverse($rev)
    foreach ($k in $rev) { [Win]::keybd_event($k, 0, 0x2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
}
# Read through .NET: Get-Content decodes a BOM-less UTF-8 file as ANSI, and
# piping ConvertFrom-Json straight into @() keeps the whole array as a single
# element on PowerShell 5.1 - both quietly make every assertion here meaningless.
function Get-Json {
    $parsed = [System.IO.File]::ReadAllText($SnippetsJson) | ConvertFrom-Json
    return @($parsed)
}
function Get-Snippet($shortcut) { @(Get-Json | Where-Object { $_.Shortcut -eq $shortcut })[0] }
function Find-ListItemByText($win, $text) {
    foreach ($item in (FindType $win $CT::ListItem)) {
        if ($item.FindFirst($TS, (Cond $AE::NameProperty $text))) { return $item }
    }
    return $null
}
# A cold start right after a build can take well over ten seconds, so wait for
# the window to actually exist instead of guessing how long that takes.
function Wait-Process-Window([int]$timeoutSec = 45) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $p = Get-Process snipv -ErrorAction SilentlyContinue
        if ($p -and $p.MainWindowHandle -ne 0) {
            $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (Cond $AE::ProcessIdProperty $p.Id))
            if ($w -and (FindName $w 'Add Snippet')) { return @($p, $w) }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}
function Wait-For([scriptblock]$condition, [int]$timeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

$testShortcut = 'Ctrl+Alt+F9'   # safe pool, never conflicts
$VK = @{ Ctrl = 0x11; Alt = 0x12; C = 0x43; F9 = 0x78 }

# ---------- setup ----------
Stop-Process -Name snipv -Force -Confirm:$false -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 1000
$savedClipboard = $null
try { $savedClipboard = Get-Clipboard -Raw } catch {}

# A run that fails midway leaves its snippet behind, and on the next run that
# snippet already owns the test shortcut - the app then asks whether to reassign
# it, and the open dialog disables everything this script clicks next. Clear it
# out while the app is still down.
if (Test-Path $SnippetsJson) {
    $kept = @(Get-Json | Where-Object { $_.Content -notlike 'E2E-*' })
    if ($kept.Count -ne (Get-Json).Count) {
        Write-Host "  (removing leftovers from an earlier run)"
        # Built by hand: ConvertTo-Json writes a bare object, not a list, when
        # only one entry is left.
        $body = ($kept | ForEach-Object { ConvertTo-Json -InputObject $_ -Depth 5 -Compress }) -join ','
        [System.IO.File]::WriteAllText($SnippetsJson, "[$body]", (New-Object System.Text.UTF8Encoding($false)))
    }
}

Start-Process $ExePath
$started = Wait-Process-Window
if (-not $started) { throw "app window not found via UIA" }
$proc, $win = $started
$hwnd = $proc.MainWindowHandle
$wshell = New-Object -ComObject wscript.shell
[void]$wshell.AppActivate($proc.Id)
Start-Sleep -Milliseconds 500

# ---------- 1. add a snippet (with shortcut capture) ----------
Write-Host "1. add snippet"
$edits = Get-Edits $win
Set-Value $edits[0] 'E2E-SNIPPET'
$edits[1].SetFocus(); Start-Sleep -Milliseconds 500
Press-Chord @($VK.Ctrl, $VK.Alt, $VK.F9)
Start-Sleep -Milliseconds 500
Assert ((Get-Value $edits[1]) -eq $testShortcut) "shortcut captured as $testShortcut"
Click (FindName $win 'Add Snippet')
Start-Sleep -Milliseconds 800
Assert ((Get-Snippet $testShortcut).Content -eq 'E2E-SNIPPET') "snippet persisted to json"

# ---------- 2. global hotkey pastes via clipboard ----------
Write-Host "2. global hotkey"
Set-Clipboard -Value 'sentinel'
Start-Sleep -Milliseconds 300
Press-Chord @($VK.Ctrl, $VK.Alt, $VK.F9)
Start-Sleep -Milliseconds 1200
Assert ((Get-Clipboard -Raw) -eq 'E2E-SNIPPET') "clipboard received snippet content"

# ---------- 3. reserved shortcut is blocked ----------
Write-Host "3. reserved shortcut blocked"
$edits = Get-Edits $win
Set-Value $edits[0] 'SHOULD-NOT-SAVE'
$edits[1].SetFocus(); Start-Sleep -Milliseconds 500
Press-Chord @($VK.Ctrl, $VK.C)
Start-Sleep -Milliseconds 500
Click (FindName $win 'Add Snippet')
Start-Sleep -Milliseconds 800
$okBtn = FindName $win 'OK'
Assert ($null -ne $okBtn) "block dialog appeared"
if ($okBtn) { Click $okBtn; Start-Sleep -Milliseconds 400 }
Assert ($null -eq (Get-Snippet 'Ctrl+C')) "Ctrl+C was not saved"

# ---------- 4. reassigning a taken shortcut parks the previous holder ----------
Write-Host "4. reassign parks the previous snippet"
$edits = Get-Edits $win
Set-Value $edits[0] 'E2E-SNIPPET-2'
$edits[1].SetFocus(); Start-Sleep -Milliseconds 500
Press-Chord @($VK.Ctrl, $VK.Alt, $VK.F9)
Start-Sleep -Milliseconds 500
Click (FindName $win 'Add Snippet')
Start-Sleep -Milliseconds 800
$reassignBtn = FindName $win 'Reassign'
Assert ($null -ne $reassignBtn) "reassign dialog appeared"
if ($reassignBtn) { Click $reassignBtn; Start-Sleep -Milliseconds 800 }
Assert ((Get-Snippet $testShortcut).Content -eq 'E2E-SNIPPET-2') "shortcut moved to the new snippet"
$parked = @(Get-Json | Where-Object { $_.Content -eq 'E2E-SNIPPET' })[0]
Assert (($null -ne $parked) -and [string]::IsNullOrEmpty($parked.Shortcut)) "previous snippet kept, without a shortcut"

# drop the parked entry again so only the test shortcut is left behind
$item = Find-ListItemByText $win 'E2E-SNIPPET'
if ($item) {
    ($item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 500
    Click (FindName $win 'Remove Selected')
    Start-Sleep -Milliseconds 800
    $confirmBtn = FindName $win 'Remove'
    if ($confirmBtn) { Click $confirmBtn; Start-Sleep -Milliseconds 800 }
}
Assert ($null -eq (Get-Json | Where-Object { $_.Content -eq 'E2E-SNIPPET' })) "parked entry removed again"

# ---------- 5. edit the snippet ----------
Write-Host "5. edit snippet"
$item = Find-ListItemByText $win $testShortcut
Assert ($null -ne $item) "list item found"
($item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
Start-Sleep -Milliseconds 600
$edits = Get-Edits $win
Assert ((Get-Value $edits[0]) -eq 'E2E-SNIPPET-2') "editor loaded selected snippet"
Set-Value $edits[0] 'E2E-SNIPPET-EDITED'
Start-Sleep -Milliseconds 300
$saveBtn = FindName $win 'Save Changes'
Assert ($null -ne $saveBtn) "'Save Changes' button visible in edit mode"
if ($saveBtn) { Click $saveBtn; Start-Sleep -Milliseconds 800 }
Assert ((Get-Snippet $testShortcut).Content -eq 'E2E-SNIPPET-EDITED') "edited content persisted"

# ---------- 6. hotkey re-registered after edit ----------
Write-Host "6. hotkey after edit"
Set-Clipboard -Value 'sentinel2'
Start-Sleep -Milliseconds 300
Press-Chord @($VK.Ctrl, $VK.Alt, $VK.F9)
Start-Sleep -Milliseconds 1200
Assert ((Get-Clipboard -Raw) -eq 'E2E-SNIPPET-EDITED') "hotkey delivers edited content"

# ---------- 7. remove the snippet ----------
Write-Host "7. remove snippet"
$item = Find-ListItemByText $win $testShortcut
($item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
Start-Sleep -Milliseconds 500
Click (FindName $win 'Remove Selected')
Start-Sleep -Milliseconds 800
$confirmBtn = FindName $win 'Remove'
Assert ($null -ne $confirmBtn) "remove confirmation dialog appeared"
if ($confirmBtn) { Click $confirmBtn; Start-Sleep -Milliseconds 800 }
Assert ($null -eq (Get-Snippet $testShortcut)) "snippet removed from json"

# ---------- 8. close-to-tray and single instance ----------
Write-Host "8. tray + single instance"
[void][Win]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
Start-Sleep -Milliseconds 1500
$alive = $null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
Assert ($alive -and -not [Win]::IsWindowVisible($hwnd)) "close hides to tray, process stays alive"
Start-Process $ExePath
$restored = Wait-For { [Win]::IsWindowVisible($hwnd) }
# The second instance hands over and exits, and it is still alive for a moment
# after the first one has already shown its window.
$single = Wait-For { @(Get-Process snipv -ErrorAction SilentlyContinue).Count -eq 1 }
Assert $single "second launch did not spawn a second process"
Assert $restored "second launch restored the window"

# ---------- cleanup ----------
Stop-Process -Name snipv -Force -Confirm:$false -ErrorAction SilentlyContinue
if ($null -ne $savedClipboard) { Set-Clipboard -Value $savedClipboard } else { Set-Clipboard -Value '' }

Write-Host ""
if ($script:failures.Count -eq 0) { Write-Host "ALL E2E TESTS PASSED" -ForegroundColor Green; exit 0 }
else { Write-Host "$($script:failures.Count) FAILURE(S):" -ForegroundColor Red; $script:failures | ForEach-Object { Write-Host " - $_" }; exit 1 }

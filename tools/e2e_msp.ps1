# GlintWin E2E (MSP channel) v5: Alt+Shift TIP cycle + click focus + app-log oracle
$ErrorActionPreference = 'Continue'
$outDir = 'D:\Development\ZCode_workplace\.ime-probe'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W32E6 {
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  public static bool SogouCompVisible() {
    bool found = false;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(64); GetClassName(h, sb, 64);
      if (sb.ToString() == "SoPY_Comp") { found = true; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
[W32E6]::SetProcessDPIAware() | Out-Null

$logFile = Get-ChildItem "$env:LOCALAPPDATA\GlintWin\logs\glint-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
function Log-Hits {
  if (-not $logFile) { return 0 }
  return (Select-String -Path $logFile.FullName -Pattern 'strict=True chunks=\[[^\]]' -ErrorAction SilentlyContinue).Count
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'glint-e2e-msp'
$form.TopMost = $true; $form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(150, 150)
$form.Size = New-Object System.Drawing.Size(700, 420)
$tb = New-Object System.Windows.Forms.TextBox
$tb.Multiline = $true; $tb.Dock = 'Fill'
$tb.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 14)
$form.Controls.Add($tb)
$form.Show()
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 600

function Click-Center($ctrl) {
  $p = $ctrl.PointToScreen([System.Drawing.Point]::new($ctrl.Width / 2, $ctrl.Height / 2))
  [void][W32E6]::SetCursorPos([int]$p.X, [int]$p.Y)
  [W32E6]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
  [W32E6]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
function Shot([string]$name) {
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($vs.X, $vs.Y, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $outDir $name)); $g.Dispose(); $bmp.Dispose()
  Write-Output ("shot " + $name)
}
function AltShift {
  [W32E6]::keybd_event(0x12,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
  [W32E6]::keybd_event(0x10,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
  [W32E6]::keybd_event(0x10,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
  [W32E6]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 900
}
function Win-Space {
  [W32E6]::keybd_event(0x5B,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [W32E6]::keybd_event(0x20,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [W32E6]::keybd_event(0x20,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [W32E6]::keybd_event(0x5B,0,2,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 1200
}
function Tap-Shift {
  [W32E6]::keybd_event(0x10,0,0,[UIntPtr]::Zero); [W32E6]::keybd_event(0x10,0,2,[UIntPtr]::Zero)
}

$baseline = Log-Hits
Write-Output ("baseline strict-hits: " + $baseline)

$ok = $false
for ($r = 1; $r -le 6; $r++) {
  Write-Output ("round " + $r)
  Win-Space
  Click-Center $tb
  Start-Sleep -Milliseconds 400
  Tap-Shift
  Start-Sleep -Milliseconds 300
  $tb.Text = ''
  [System.Windows.Forms.SendKeys]::SendWait('nihao')
  Start-Sleep -Milliseconds 700
  if ($tb.Text -match 'nihao') {
    # raw letters = EN mode -> shift back to CN and retype
    Tap-Shift
    Start-Sleep -Milliseconds 300
    $tb.Text = ''
    [System.Windows.Forms.SendKeys]::SendWait('nihao')
  }
  Start-Sleep -Milliseconds 1500
  Shot ("e2e_msp5_r" + $r + ".png")
  $hits = Log-Hits
  Write-Output ("  strict-hits now: " + $hits + " box=[" + $tb.Text + "]")
  if ($hits -gt $baseline) { $ok = $true; Write-Output '  MSP CHANNEL HIT confirmed'; break }
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  [System.Windows.Forms.SendKeys]::SendWait('{BS 20}')
  Start-Sleep -Milliseconds 300
}

if ($ok) {
  Click-Center $tb
  Start-Sleep -Milliseconds 200
  $tb.Text = ''
  [System.Windows.Forms.SendKeys]::SendWait('xuexi')
  Start-Sleep -Milliseconds 1500
  Shot 'e2e_msp5_xuexi.png'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  [System.Windows.Forms.SendKeys]::SendWait('{BS 20}')
}

# restore Sogou as the active IME
for ($i = 0; $i -lt 4; $i++) {
  if ([W32E6]::SogouCompVisible()) { break }
  AltShift
  Click-Center $tb
  Start-Sleep -Milliseconds 250
  $tb.Text = ''
  [System.Windows.Forms.SendKeys]::SendWait('a')
  Start-Sleep -Milliseconds 600
  if ([W32E6]::SogouCompVisible()) { Write-Output 'restored: sogou active'; break }
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  [System.Windows.Forms.SendKeys]::SendWait('{BS 5}')
}
$form.Close()
Write-Output ("E2E MSP v5 DONE ok=" + $ok)

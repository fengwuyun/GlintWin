# GlintWin E2E 测试：自建输入框、模拟真实打字、截图验证悬浮条
$ErrorActionPreference = 'Continue'
$outDir = 'D:\Development\ZCode_workplace\.ime-probe'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W32E {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[W32E]::SetProcessDPIAware() | Out-Null

$form = New-Object System.Windows.Forms.Form
$form.Text = 'glint-e2e'
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(150, 150)
$form.Size = New-Object System.Drawing.Size(700, 420)
$tb = New-Object System.Windows.Forms.TextBox
$tb.Multiline = $true; $tb.Dock = 'Fill'
$tb.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 14)
$form.Controls.Add($tb)
$form.Show(); $form.Activate()
[System.Windows.Forms.Application]::DoEvents()

function Shot([string]$name) {
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($vs.X, $vs.Y, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $outDir $name)); $g.Dispose(); $bmp.Dispose()
  Write-Output ("shot " + $name)
}
function Clear2 {
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  [System.Windows.Forms.SendKeys]::SendWait('{BS 30}')
}

for ($i = 0; $i -lt 6; $i++) {
  if ([W32E]::GetForegroundWindow() -eq $form.Handle) { break }
  [W32E]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [W32E]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
  [void][W32E]::SetForegroundWindow($form.Handle); $form.Activate()
  [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 300
}

$tb.Focus() | Out-Null

[System.Windows.Forms.SendKeys]::SendWait('nihaoshijie')
Start-Sleep -Milliseconds 1100
Shot 'e2e_1.png'
Start-Sleep -Milliseconds 900
Shot 'e2e_2.png'
Clear2
Start-Sleep -Milliseconds 600

[System.Windows.Forms.SendKeys]::SendWait('xuexi')
Start-Sleep -Milliseconds 1100
Shot 'e2e_3.png'
Clear2
$form.Close()
Write-Output 'E2E DONE'

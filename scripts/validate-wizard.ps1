param([Parameter(Mandatory)][string]$Setup, [Parameter(Mandatory)][string]$HolderExe, [Parameter(Mandatory)][string]$Root)
# Interactive wizard walk-through: screenshot of every page, buttons pressed with BM_CLICK on their window handles (no
# mouse/keyboard simulation), install into a throw-away folder. Safety: during the run a test FRAME (HolderExe, own --data
# folder) holds the main instance mutex, so a FRAME started by the wizard's "Start FRAME" option only shows "FRAME is
# already running" and exits before touching any data (FRAME checks the mutex before it opens its data folder). The
# option is also unticked (space on the check list) and the result is recorded. Silent uninstall at the end.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Drawing;
public static class W {
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
  struct RECT { public int L, T, R, B; }
  public static void Save(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r); using (var b = new Bitmap(r.R - r.L, r.B - r.T)) { using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); } b.Save(path); }
  }
}
'@ -ReferencedAssemblies System.Drawing
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
New-Item -ItemType Directory -Force $Root | Out-Null
$dir = Join-Path $Root 'Programs\FRAME'

# Safety holder (main FRAME mutex, isolated data)
$holderData = Join-Path $Root 'holder-data'; New-Item -ItemType Directory -Force "$holderData\captures" | Out-Null
@{ OutputDirectory = "$holderData\captures"; Onboarded = $true; ReduceMotion = $true } | ConvertTo-Json | Set-Content -LiteralPath "$holderData\settings.json"

$p = Start-Process -FilePath $Setup -ArgumentList @("/DIR=$dir", "/LOG=$Root\wizard-setup.log") -PassThru
function Window { for ($i = 0; $i -lt 120; $i++) { $w = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Setup - FRAME'))); if ($w) { return $w }; Start-Sleep -Milliseconds 250 }; throw 'Wizard window not found' }
function Element($w, $name) { $w.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq $name -and $_.Current.ClassName -eq 'TNewButton' } | Select-Object -First 1 }
function Click($e) { [void][W]::SendMessage([IntPtr]$e.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }
$pages = @(); $launchState = 'not shown'
for ($step = 1; $step -le 12; $step++) {
    $w = Window; Start-Sleep -Milliseconds 900
    $all = $w.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $text = ($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ' | '
    $png = Join-Path $Root ("wizard-{0:00}.png" -f $step); [W]::Save([IntPtr]$w.Current.NativeWindowHandle, $png)
    $pages += [ordered]@{ step = $step; text = $text.Substring(0, [Math]::Min(500, $text.Length)).Replace("`r", ' ').Replace("`n", ' ') }
    $finish = Element $w 'Finish'
    if ($finish) {
        # Setup checks the mutex when it starts; from here on a held mutex only affects a FRAME the wizard starts.
        $holder = Start-Process -FilePath $HolderExe -ArgumentList @('--data', $holderData) -PassThru; Start-Sleep -Seconds 6
        $list = $all | Where-Object { $_.Current.ClassName -eq 'TNewCheckListBox' } | Select-Object -First 1
        if ($list) { $h = [IntPtr]$list.Current.NativeWindowHandle; [void][W]::PostMessage($h, 0x0100, [IntPtr]0x20, [IntPtr]::Zero); [void][W]::PostMessage($h, 0x0101, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 600; [W]::Save([IntPtr]$w.Current.NativeWindowHandle, (Join-Path $Root 'wizard-finish-unticked.png')); $launchState = 'space sent to the check list' }
        Click $finish; break
    }
    $next = Element $w 'Install'; if (-not $next) { $next = Element $w 'Next' }
    if (-not $next) { throw "No Next/Install button on step $step" }
    $isInstall = $next.Current.Name -eq 'Install'; Click $next
    if ($isInstall) { for ($i = 0; $i -lt 120 -and -not (Element (Window) 'Finish'); $i++) { Start-Sleep -Milliseconds 500 } }
}
$p.WaitForExit(60000) | Out-Null; Start-Sleep -Seconds 4
$launched = @(Get-Process FRAME -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir*" })
# A FRAME started by the wizard would show "FRAME is already running" (mutex held); close that message and the process.
$msg = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'FRAME')))
$sawAlreadyRunning = [bool]($msg -and ($msg.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like '*already running*' }))
$launched | Stop-Process -Force -ErrorAction SilentlyContinue
$installed = Test-Path "$dir\FRAME.exe"
Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue
$uninstaller = (Get-ChildItem $dir -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if ($uninstaller) { $u = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -PassThru; $u.WaitForExit(120000) | Out-Null; Start-Sleep -Seconds 5 }
[ordered]@{ installed = $installed; launchCheckbox = $launchState; frameStartedByWizard = $launched.Count; startedFrameStoppedByMutex = $sawAlreadyRunning; removed = -not (Test-Path "$dir\FRAME.exe"); pages = $pages } | ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $Root 'wizard.json')


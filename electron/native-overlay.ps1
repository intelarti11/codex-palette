param(
  [ValidateSet('watch', 'apply', 'labels')][string]$Mode,
  [int]$OverlayPid = 0,
  [int]$ModelIndex = 0,
  [int]$EffortIndex = 0
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CodexOverlayNative {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint source, uint target, bool attach);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int command);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@

function Get-CodexProcess {
  Get-Process -Name ChatGPT -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -like '*OpenAI.Codex_*' } |
    Select-Object -First 1
}

function Focus-Codex([System.Diagnostics.Process]$Process) {
  $foreground = [CodexOverlayNative]::GetForegroundWindow()
  [uint32]$unused = 0
  $sourceThread = [CodexOverlayNative]::GetWindowThreadProcessId($foreground, [ref]$unused)
  $targetThread = [CodexOverlayNative]::GetWindowThreadProcessId($Process.MainWindowHandle, [ref]$unused)
  [CodexOverlayNative]::AttachThreadInput($sourceThread, $targetThread, $true) | Out-Null
  [CodexOverlayNative]::ShowWindowAsync($Process.MainWindowHandle, 9) | Out-Null
  [CodexOverlayNative]::BringWindowToTop($Process.MainWindowHandle) | Out-Null
  [CodexOverlayNative]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
  [CodexOverlayNative]::AttachThreadInput($sourceThread, $targetThread, $false) | Out-Null
}

function Click-At([int]$X, [int]$Y) {
  [CodexOverlayNative]::SetCursorPos($X, $Y) | Out-Null
  [CodexOverlayNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  [CodexOverlayNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Find-CodexElement {
  param(
    [int]$ProcessId,
    [System.Windows.Automation.ControlType]$ControlType,
    [string]$Name,
    [switch]$Exact,
    [int]$TimeoutMilliseconds = 2500
  )

  $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
  do {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
      $ProcessId
    )
    $elements = $root.FindAll(
      [System.Windows.Automation.TreeScope]::Descendants,
      $processCondition
    )
    for ($index = 0; $index -lt $elements.Count; $index++) {
      $element = $elements.Item($index)
      if ($element.Current.ControlType -ne $ControlType) { continue }
      $matches = if ($Exact) {
        $element.Current.Name -eq $Name
      } else {
        $element.Current.Name -match $Name
      }
      if ($matches) { return $element }
    }
    Start-Sleep -Milliseconds 80
  } while ([DateTime]::UtcNow -lt $deadline)

  throw "Controle Codex introuvable: $Name"
}

function Try-FindCodexElement {
  param(
    [int]$ProcessId,
    [System.Windows.Automation.ControlType]$ControlType,
    [string]$Name,
    [int]$TimeoutMilliseconds = 350
  )
  try {
    return Find-CodexElement $ProcessId $ControlType $Name -TimeoutMilliseconds $TimeoutMilliseconds
  } catch {
    return $null
  }
}

function Click-Element([System.Windows.Automation.AutomationElement]$Element) {
  $bounds = $Element.Current.BoundingRectangle
  if ($bounds.IsEmpty -or $bounds.Width -le 0 -or $bounds.Height -le 0) {
    throw 'Le controle Codex est invisible.'
  }
  Click-At ([int]($bounds.X + ($bounds.Width / 2))) ([int]($bounds.Y + ($bounds.Height / 2)))
  Start-Sleep -Milliseconds 280
}

function Activate-Element([System.Windows.Automation.AutomationElement]$Element) {
  try {
    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
  } catch {
    $bounds = $Element.Current.BoundingRectangle
    if ($bounds.IsEmpty -or $bounds.Width -le 0 -or $bounds.Height -le 0) {
      throw 'Le controle Codex est invisible.'
    }
    Click-At ([int]($bounds.X + ($bounds.Width / 2))) ([int]($bounds.Y + ($bounds.Height / 2)))
  }
  Start-Sleep -Milliseconds 420
}

function Get-PrimaryText([System.Windows.Automation.AutomationElement]$Element) {
  $textCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text
  )
  $texts = $Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
  for ($index = 0; $index -lt $texts.Count; $index++) {
    $name = $texts.Item($index).Current.Name.Trim()
    if ($name) { return $name }
  }
  return $Element.Current.Name.Trim()
}

function Get-EffortLabels([int]$ProcessId, [string]$CurrentEffort) {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $processCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
    $ProcessId
  )
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $processCondition)
  $menus = @()
  for ($index = 0; $index -lt $all.Count; $index++) {
    $element = $all.Item($index)
    if ($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu) {
      $menus += $element
    }
  }

  $itemCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::MenuItem
  )
  foreach ($menu in $menus) {
    $items = $menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    if ($items.Count -lt 4 -or $items.Count -gt 5) { continue }
    $entries = @()
    for ($index = 0; $index -lt $items.Count; $index++) {
      $item = $items.Item($index)
      $entries += [pscustomobject]@{
        y = $item.Current.BoundingRectangle.Y
        label = Get-PrimaryText $item
      }
    }
    $labels = @($entries | Sort-Object y | ForEach-Object { $_.label })
    if ($labels -contains $CurrentEffort) { return $labels }
  }
  throw 'Les libelles natifs des puissances sont introuvables.'
}

function Get-NativeSelectorBounds([System.Diagnostics.Process]$Process) {
  try {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Button
    )
    $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
    for ($index = 0; $index -lt $buttons.Count; $index++) {
      $button = $buttons.Item($index)
      if ($button.Current.Name -notmatch '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s') { continue }
      $bounds = $button.Current.BoundingRectangle
      if ($bounds.IsEmpty) { return $null }
      return @{
        x = [int][Math]::Round($bounds.X)
        y = [int][Math]::Round($bounds.Y)
        width = [int][Math]::Round($bounds.Width)
        height = [int][Math]::Round($bounds.Height)
      }
    }
  } catch {
    return $null
  }
  return $null
}

if ($Mode -eq 'watch') {
  $previous = ''
  while ($true) {
    $codex = Get-CodexProcess
    $foreground = [CodexOverlayNative]::GetForegroundWindow()
    [uint32]$foregroundPid = 0
    [CodexOverlayNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid) | Out-Null
    $visible = $null -ne $codex -and ($foregroundPid -eq $codex.Id -or $foregroundPid -eq $OverlayPid)
    $selector = if ($null -ne $codex) { Get-NativeSelectorBounds $codex } else { $null }
    $state = @{
      found = $null -ne $codex
      visible = $visible
      selector = $selector
    } | ConvertTo-Json -Compress
    if ($state -ne $previous) {
      [Console]::Out.WriteLine($state)
      [Console]::Out.Flush()
      $previous = $state
    }
    Start-Sleep -Milliseconds 350
  }
}

$codex = Get-CodexProcess
if ($null -eq $codex) { throw 'La fenêtre officielle de Codex est introuvable.' }
$modelNames = @('5.6 Sol', '5.6 Terra', '5.6 Luna', '5.5', '5.4', '5.4 Mini')

if ($Mode -eq 'labels') {
  Focus-Codex $codex
  Start-Sleep -Milliseconds 180

  $selector = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::Button) '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s'
  $currentModel = $modelNames | Where-Object { $selector.Current.Name.StartsWith($_ + ' ') } | Select-Object -First 1
  if (-not $currentModel) { throw 'Le modele natif courant est introuvable.' }
  $currentEffort = $selector.Current.Name.Substring($currentModel.Length).Trim()

  $effortMenuPattern = [Regex]::Escape($currentEffort) + '$'
  $effortMenu = Try-FindCodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $effortMenuPattern
  if (-not $effortMenu) {
    Click-Element $selector
    $effortMenu = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $effortMenuPattern
  }
  Activate-Element $effortMenu
  $efforts = @(Get-EffortLabels $codex.Id $currentEffort)
  Click-Element $selector
  if ($efforts.Count -eq 4) { $efforts += 'Ultra' }

  @{ ok = $true; efforts = $efforts } | ConvertTo-Json -Compress
  exit 0
}

if ($ModelIndex -lt 0 -or $ModelIndex -gt 5) { throw 'Index de modèle invalide.' }
if ($EffortIndex -lt 0 -or $EffortIndex -gt 4) { throw 'Index d’effort invalide.' }

$supportedEfforts = @(
  @(0, 1, 2, 3, 4),
  @(0, 1, 2, 3, 4),
  @(0, 1, 2, 3),
  @(0, 1, 2, 3),
  @(0, 1, 2, 3),
  @(0, 1, 2, 3)
)
if ($supportedEfforts[$ModelIndex] -notcontains $EffortIndex) {
  throw 'Cette puissance n est pas disponible pour ce modele.'
}

Focus-Codex $codex
Start-Sleep -Milliseconds 180

$effortNames = @(
  ('L' + [char]0x00E9 + 'ger'),
  'Moyen',
  ([char]0x00C9 + 'lev' + [char]0x00E9),
  ('Tr' + [char]0x00E8 + 's ' + [char]0x00E9 + 'lev' + [char]0x00E9),
  'Ultra'
)

# Locate the native controls by accessibility name so the overlay can be moved freely.
$selector = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::Button) '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s'
Click-Element $selector
$modelMenu = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) '^Mod.le\s'
Activate-Element $modelMenu
$model = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $modelNames[$ModelIndex] -Exact
Activate-Element $model

$expectedModel = '^' + [Regex]::Escape($modelNames[$ModelIndex]) + '\s'
$null = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::Button) $expectedModel -TimeoutMilliseconds 3500

$effortMenu = Try-FindCodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) '^Effort\s' 500
if (-not $effortMenu) {
  $selector = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::Button) '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s'
  Click-Element $selector
  $effortMenu = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) '^Effort\s'
}
Activate-Element $effortMenu
$effortPattern = '^' + [Regex]::Escape($effortNames[$EffortIndex]) + '(\s|$)'
$effort = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $effortPattern
Activate-Element $effort

$expectedSelection = '^' + [Regex]::Escape($modelNames[$ModelIndex]) + '\s+' + [Regex]::Escape($effortNames[$EffortIndex]) + '$'
$confirmed = Find-CodexElement $codex.Id ([System.Windows.Automation.ControlType]::Button) $expectedSelection -TimeoutMilliseconds 3500

@{ ok = $true; selection = $confirmed.Current.Name } | ConvertTo-Json -Compress

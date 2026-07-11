param(
  [ValidateSet('watch', 'apply', 'labels', 'speed')][string]$Mode,
  [int]$OverlayPid = 0,
  [int]$ModelIndex = 0,
  [int]$EffortIndex = 0,
  [int]$SpeedIndex = -1
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$script:ModelNames = @('5.6 Sol', '5.6 Terra', '5.6 Luna', '5.5', '5.4', '5.4 Mini')

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CodexOverlayNative {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
'@

function Get-Codex {
  Get-Process -Name ChatGPT -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -like '*OpenAI.Codex_*' } |
    Select-Object -First 1
}

function Get-Pattern($Element, $Pattern) {
  $value = $null
  if ($Element.TryGetCurrentPattern($Pattern, [ref]$value)) { return $value }
  return $null
}

function Open-Silent($Element) {
  $pattern = Get-Pattern $Element ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  if ($pattern) {
    $pattern = [System.Windows.Automation.ExpandCollapsePattern]$pattern
    if ($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::LeafNode) {
      if ($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) { $pattern.Expand() }
      Start-Sleep -Milliseconds 240
      return
    }
  }
  $pattern = Get-Pattern $Element ([System.Windows.Automation.InvokePattern]::Pattern)
  if ($pattern) { ([System.Windows.Automation.InvokePattern]$pattern).Invoke(); Start-Sleep -Milliseconds 240; return }
  $pattern = Get-Pattern $Element ([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
  if ($pattern) { ([System.Windows.Automation.LegacyIAccessiblePattern]$pattern).DoDefaultAction(); Start-Sleep -Milliseconds 240; return }
  throw "The control '$($Element.Current.Name)' has no silent UI Automation action."
}

function Select-Silent($Element) {
  $pattern = Get-Pattern $Element ([System.Windows.Automation.InvokePattern]::Pattern)
  if ($pattern) { ([System.Windows.Automation.InvokePattern]$pattern).Invoke(); Start-Sleep -Milliseconds 300; return }
  $pattern = Get-Pattern $Element ([System.Windows.Automation.SelectionItemPattern]::Pattern)
  if ($pattern) { ([System.Windows.Automation.SelectionItemPattern]$pattern).Select(); Start-Sleep -Milliseconds 300; return }
  $pattern = Get-Pattern $Element ([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
  if ($pattern) { ([System.Windows.Automation.LegacyIAccessiblePattern]$pattern).DoDefaultAction(); Start-Sleep -Milliseconds 300; return }
  throw "The option '$($Element.Current.Name)' has no silent UI Automation action."
}

function Close-Silent($Element) {
  if (-not $Element) { return }
  $pattern = Get-Pattern $Element ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  if ($pattern) {
    $pattern = [System.Windows.Automation.ExpandCollapsePattern]$pattern
    if ($pattern.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
      $pattern.Collapse(); Start-Sleep -Milliseconds 150
    }
  }
}

function Get-Elements([int]$ProcessId, $ControlType) {
  $condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId
  )
  $all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants, $condition
  )
  $result = @()
  for ($i = 0; $i -lt $all.Count; $i++) {
    if ($all.Item($i).Current.ControlType -eq $ControlType) { $result += $all.Item($i) }
  }
  return @($result)
}

function Test-Visible($Element) {
  try { return -not $Element.Current.IsOffscreen -and -not $Element.Current.BoundingRectangle.IsEmpty }
  catch { return $false }
}

function Find-Element([int]$ProcessId, $ControlType, [string]$Name, [switch]$Exact, [int]$Timeout = 2500) {
  $deadline = [DateTime]::UtcNow.AddMilliseconds($Timeout)
  do {
    foreach ($element in @(Get-Elements $ProcessId $ControlType)) {
      $matches = if ($Exact) { $element.Current.Name -eq $Name } else { $element.Current.Name -match $Name }
      if ($matches) { return $element }
    }
    Start-Sleep -Milliseconds 70
  } while ([DateTime]::UtcNow -lt $deadline)
  throw "Codex control not found: $Name"
}

function Normalize([string]$Value, [switch]$Effort) {
  $value = ($Value -replace '\s+', ' ').Trim()
  if ($Effort -and $value -match '^Ultra(?:\s|$)') { return 'Ultra' }
  return $value
}

function Get-Texts($Element) {
  $condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text
  )
  $nodes = $Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
  $values = @()
  for ($i = 0; $i -lt $nodes.Count; $i++) {
    $value = Normalize $nodes.Item($i).Current.Name
    if ($value -and $values -notcontains $value) { $values += $value }
  }
  return @($values)
}

function Get-Label($Element, [switch]$Effort) {
  $texts = @(Get-Texts $Element)
  $value = if ($texts.Count) { $texts[0] } else { $Element.Current.Name }
  return Normalize $value -Effort:$Effort
}

function Get-Context([int]$ProcessId) {
  $selector = Find-Element $ProcessId ([System.Windows.Automation.ControlType]::Button) '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s'
  $name = Normalize $selector.Current.Name
  $model = $script:ModelNames | Where-Object { $name.StartsWith($_ + ' ') } | Select-Object -First 1
  if (-not $model) { throw 'The current model could not be identified.' }
  $effort = $name.Substring($model.Length).Trim()
  Open-Silent $selector

  $submenus = @()
  foreach ($item in @(Get-Elements $ProcessId ([System.Windows.Automation.ControlType]::MenuItem))) {
    if (-not (Test-Visible $item)) { continue }
    $pattern = Get-Pattern $item ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($pattern -and ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::LeafNode) {
      $submenus += $item
    }
  }

  $modelMenu = $submenus | Where-Object { (Normalize $_.Current.Name) -match [Regex]::Escape($model) } | Select-Object -First 1
  $effortMenu = $submenus | Where-Object {
    $_ -ne $modelMenu -and (Normalize $_.Current.Name) -match ([Regex]::Escape($effort) + '$')
  } | Select-Object -First 1
  if (-not $modelMenu -or -not $effortMenu) { throw 'The native model or reasoning submenu is not exposed.' }

  return [pscustomobject]@{
    selector = $selector; model = $model; effort = $effort
    submenus = $submenus; modelMenu = $modelMenu; effortMenu = $effortMenu
  }
}

function Get-MenuOptions([int]$ProcessId, $Submenu, [int]$Minimum, [int]$Maximum, [switch]$Effort) {
  Close-Silent $Submenu
  $before = @{}
  foreach ($menu in @(Get-Elements $ProcessId ([System.Windows.Automation.ControlType]::Menu))) {
    if (Test-Visible $menu) { $before[($menu.GetRuntimeId() -join '.')] = $true }
  }
  Open-Silent $Submenu

  $deadline = [DateTime]::UtcNow.AddMilliseconds(2000)
  do {
    $fallback = $null
    foreach ($menu in @(Get-Elements $ProcessId ([System.Windows.Automation.ControlType]::Menu))) {
      if (-not (Test-Visible $menu)) { continue }
      $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem
      )
      $nodes = $menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
      $entries = @()
      for ($i = 0; $i -lt $nodes.Count; $i++) {
        $item = $nodes.Item($i)
        if (-not (Test-Visible $item)) { continue }
        $bounds = $item.Current.BoundingRectangle
        $entries += [pscustomobject]@{ item = $item; x = $bounds.X; y = $bounds.Y; label = Get-Label $item -Effort:$Effort }
      }
      $entries = @($entries | Sort-Object y, x)
      if ($entries.Count -lt $Minimum -or $entries.Count -gt $Maximum) { continue }
      $result = [pscustomobject]@{
        items = @($entries | ForEach-Object { $_.item })
        labels = @($entries | ForEach-Object { $_.label })
      }
      if (-not $before.ContainsKey(($menu.GetRuntimeId() -join '.'))) { return $result }
      $fallback = $result
    }
    if ($fallback) { return $fallback }
    Start-Sleep -Milliseconds 70
  } while ([DateTime]::UtcNow -lt $deadline)
  throw 'The submenu options are not exposed.'
}

function Get-SelectedIndex($Owner, $Items, $Labels) {
  for ($i = 0; $i -lt $Items.Count; $i++) {
    $pattern = Get-Pattern $Items[$i] ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pattern -and ([System.Windows.Automation.SelectionItemPattern]$pattern).Current.IsSelected) { return $i }
    $pattern = Get-Pattern $Items[$i] ([System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern -and ([System.Windows.Automation.TogglePattern]$pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { return $i }
  }
  $name = Normalize $Owner.Current.Name
  $texts = @(Get-Texts $Owner)
  for ($i = 0; $i -lt $Labels.Count; $i++) {
    if ($name -match ([Regex]::Escape($Labels[$i]) + '$') -or $texts -contains $Labels[$i]) { return $i }
  }
  return -1
}

function Get-GroupLabel($Owner, $Labels) {
  foreach ($text in @(Get-Texts $Owner)) { if ($Labels -notcontains $text) { return $text } }
  $name = Normalize $Owner.Current.Name
  foreach ($label in @($Labels | Sort-Object Length -Descending)) {
    $trimmed = ($name -replace ('\s*' + [Regex]::Escape($label) + '$'), '').Trim(' ', ':', '-', '·')
    if ($trimmed -ne $name -and $trimmed) { return $trimmed }
  }
  return ''
}

function New-Speed($Owner, $Control, $Options) {
  return [pscustomobject]@{
    owner = $Owner; control = $Control; items = $Options.items; labels = $Options.labels
    label = Get-GroupLabel $Control $Options.labels
    selectedIndex = Get-SelectedIndex $Control $Options.items $Options.labels
  }
}

function Get-Speed([int]$ProcessId, $Context) {
  foreach ($submenu in @($Context.submenus)) {
    if ($submenu -eq $Context.modelMenu -or $submenu -eq $Context.effortMenu) { continue }
    try {
      $options = Get-MenuOptions $ProcessId $submenu 2 2
      if ($options.labels.Count -eq 2) { return New-Speed $Context.selector $submenu $options }
    } catch {}
  }

  Close-Silent $Context.selector
  $selector = Find-Element $ProcessId ([System.Windows.Automation.ControlType]::Button) '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s'
  $bounds = $selector.Current.BoundingRectangle
  $centerX = $bounds.X + $bounds.Width / 2
  $centerY = $bounds.Y + $bounds.Height / 2
  $candidates = @()
  foreach ($button in @(Get-Elements $ProcessId ([System.Windows.Automation.ControlType]::Button))) {
    if ($button -eq $selector -or -not (Test-Visible $button)) { continue }
    $pattern = Get-Pattern $button ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $pattern -or ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::LeafNode) { continue }
    $rect = $button.Current.BoundingRectangle
    $dx = [Math]::Abs(($rect.X + $rect.Width / 2) - $centerX)
    $dy = [Math]::Abs(($rect.Y + $rect.Height / 2) - $centerY)
    if ($dx -le 520 -and $dy -le 140) { $candidates += [pscustomobject]@{ button = $button; distance = $dx + 3 * $dy } }
  }
  foreach ($candidate in @($candidates | Sort-Object distance)) {
    try {
      $options = Get-MenuOptions $ProcessId $candidate.button 2 2
      if ($options.labels.Count -eq 2) { return New-Speed $candidate.button $candidate.button $options }
    } catch { Close-Silent $candidate.button }
  }
  throw 'The native two-position speed selector is not exposed.'
}

function Get-SelectorBounds($Process) {
  try {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Button
    )
    $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    for ($i = 0; $i -lt $buttons.Count; $i++) {
      $button = $buttons.Item($i)
      if ($button.Current.Name -notmatch '^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s') { continue }
      $rect = $button.Current.BoundingRectangle
      if ($rect.IsEmpty) { return $null }
      return @{ x = [int]$rect.X; y = [int]$rect.Y; width = [int]$rect.Width; height = [int]$rect.Height }
    }
  } catch {}
  return $null
}

if ($Mode -eq 'watch') {
  $previous = ''
  while ($true) {
    $codex = Get-Codex
    $foreground = [CodexOverlayNative]::GetForegroundWindow()
    [uint32]$foregroundPid = 0
    [CodexOverlayNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid) | Out-Null
    $state = @{
      found = $null -ne $codex
      visible = $null -ne $codex -and ($foregroundPid -eq $codex.Id -or $foregroundPid -eq $OverlayPid)
      selector = if ($codex) { Get-SelectorBounds $codex } else { $null }
    } | ConvertTo-Json -Compress
    if ($state -ne $previous) { [Console]::Out.WriteLine($state); [Console]::Out.Flush(); $previous = $state }
    Start-Sleep -Milliseconds 350
  }
}

$codex = Get-Codex
if (-not $codex) { throw 'The official Codex window could not be found.' }

if ($Mode -eq 'labels') {
  $context = Get-Context $codex.Id
  $effortOptions = Get-MenuOptions $codex.Id $context.effortMenu 4 5 -Effort
  $efforts = @($effortOptions.labels)
  Close-Silent $context.selector
  if ($efforts.Count -eq 4) { $efforts += 'Ultra' }

  $speedLabel = ''; $speeds = @(); $selected = -1
  try {
    $context = Get-Context $codex.Id
    $speed = Get-Speed $codex.Id $context
    $speedLabel = $speed.label; $speeds = @($speed.labels); $selected = $speed.selectedIndex
    Close-Silent $speed.owner
  } catch { try { Close-Silent $context.selector } catch {} }

  @{ ok = $true; efforts = $efforts; speedLabel = $speedLabel; speeds = $speeds; speedIndex = $selected } |
    ConvertTo-Json -Compress
  exit 0
}

if ($Mode -eq 'speed') {
  if ($SpeedIndex -lt 0 -or $SpeedIndex -gt 1) { throw 'Invalid speed index.' }
  $context = Get-Context $codex.Id
  $speed = Get-Speed $codex.Id $context
  $label = $speed.labels[$SpeedIndex]
  Select-Silent $speed.items[$SpeedIndex]

  $confirmed = $false
  $deadline = [DateTime]::UtcNow.AddMilliseconds(3500)
  do {
    try {
      $current = Get-Speed $codex.Id (Get-Context $codex.Id)
      $confirmed = $current.selectedIndex -eq $SpeedIndex -or (Normalize $current.control.Current.Name) -match ([Regex]::Escape($label) + '$')
      Close-Silent $current.owner
      if ($confirmed) { break }
    } catch {}
    Start-Sleep -Milliseconds 100
  } while ([DateTime]::UtcNow -lt $deadline)
  if (-not $confirmed) { throw 'Codex did not confirm the requested speed.' }
  @{ ok = $true; speedIndex = $SpeedIndex; speed = $label; inputMode = 'uia-silent' } | ConvertTo-Json -Compress
  exit 0
}

if ($ModelIndex -lt 0 -or $ModelIndex -gt 5 -or $EffortIndex -lt 0 -or $EffortIndex -gt 4) { throw 'Invalid selection index.' }
$supported = @(@(0,1,2,3,4), @(0,1,2,3,4), @(0,1,2,3), @(0,1,2,3), @(0,1,2,3), @(0,1,2,3))
if ($supported[$ModelIndex] -notcontains $EffortIndex) { throw 'This reasoning level is unavailable for the model.' }

$context = Get-Context $codex.Id
Open-Silent $context.modelMenu
Select-Silent (Find-Element $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $script:ModelNames[$ModelIndex] -Exact)
$null = Find-Element $codex.Id ([System.Windows.Automation.ControlType]::Button) ('^' + [Regex]::Escape($script:ModelNames[$ModelIndex]) + '\s') -Timeout 3500

$context = Get-Context $codex.Id
$options = Get-MenuOptions $codex.Id $context.effortMenu 4 5 -Effort
if ($EffortIndex -ge $options.items.Count) { throw 'The requested reasoning level is unavailable.' }
$effort = $options.labels[$EffortIndex]
Select-Silent $options.items[$EffortIndex]
$expected = '^' + [Regex]::Escape($script:ModelNames[$ModelIndex]) + '\s+' + [Regex]::Escape($effort) + '$'
$confirmed = Find-Element $codex.Id ([System.Windows.Automation.ControlType]::Button) $expected -Timeout 3500
@{ ok = $true; selection = $confirmed.Current.Name; inputMode = 'uia-silent' } | ConvertTo-Json -Compress

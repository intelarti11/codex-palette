param(
  [ValidateSet('watch', 'apply', 'labels-fast', 'labels', 'speed', 'cdp', 'enable-cdp')][string]$Mode,
  [int]$OverlayPid = 0,
  [int]$ModelIndex = 0,
  [int]$EffortIndex = 0,
  [int]$SpeedIndex = -1,
  [string]$ModelNamesJson = '[]'
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
}
'@

function Get-Codex {
  Get-Process -Name ChatGPT -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -like '*OpenAI.Codex_*' } |
    Select-Object -First 1
}

if ($Mode -eq 'cdp') {
  $candidate = Get-CimInstance Win32_Process -Filter "Name = 'ChatGPT.exe'" |
    Where-Object { $_.CommandLine -notmatch '--type=' -and $_.ExecutablePath -like '*OpenAI.Codex_*' } |
    Select-Object -First 1
  $match = if ($candidate) {
    [Regex]::Match([string]$candidate.CommandLine, '--remote-debugging-port(?:=|\s+)(\d+)')
  } else {
    $null
  }
  $port = if ($match -and $match.Success) { [int]$match.Groups[1].Value } else { $null }
  @{ ok = $null -ne $port; port = $port } | ConvertTo-Json -Compress
  exit 0
}

if ($Mode -eq 'enable-cdp') {
  $debugProcess = Get-CimInstance Win32_Process -Filter "Name = 'ChatGPT.exe'" |
    Where-Object { $_.CommandLine -notmatch '--type=' -and $_.ExecutablePath -like '*OpenAI.Codex_*' -and $_.CommandLine -match '--remote-debugging-port(?:=|\s+)(\d+)' } |
    Select-Object -First 1
  if ($debugProcess) {
    $match = [Regex]::Match([string]$debugProcess.CommandLine, '--remote-debugging-port(?:=|\s+)(\d+)')
    @{ ok = $true; port = [int]$match.Groups[1].Value; reused = $true } | ConvertTo-Json -Compress
    exit 0
  }
  $codex = Get-Codex
  if ($codex) {
    $null = $codex.CloseMainWindow()
    try { Wait-Process -Id $codex.Id -Timeout 6 -ErrorAction Stop } catch {
      Get-Process -Name ChatGPT -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like '*OpenAI.Codex_*' } |
        Stop-Process -Force
      Start-Sleep -Seconds 2
    }
  }
  $package = Get-AppxPackage OpenAI.Codex | Select-Object -First 1
  if (-not $package) { throw 'The official Codex package could not be found.' }
  $executable = Join-Path $package.InstallLocation 'app\ChatGPT.exe'
  if (-not (Test-Path $executable)) { throw 'The official Codex executable could not be found.' }
  $port = Get-Random -Minimum 41000 -Maximum 49000
  Start-Process $executable -ArgumentList @(
    '--remote-debugging-address=127.0.0.1',
    "--remote-debugging-port=$port"
  )
  @{ ok = $true; port = $port } | ConvertTo-Json -Compress
  exit 0
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

function Get-ExpandableMenuItems([int]$ProcessId) {
  $submenus = @()
  foreach ($item in @(Get-Elements $ProcessId ([System.Windows.Automation.ControlType]::MenuItem))) {
    if (-not (Test-Visible $item)) { continue }
    $pattern = Get-Pattern $item ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($pattern -and ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::LeafNode) {
      $submenus += $item
    }
  }
  return @($submenus)
}

function Find-Selector([int]$ProcessId) {
  $candidates = @()
  foreach ($button in @(Get-Elements $ProcessId ([System.Windows.Automation.ControlType]::Button))) {
    if (-not (Test-Visible $button)) { continue }
    $pattern = Get-Pattern $button ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $pattern -or ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::LeafNode) { continue }
    $rect = $button.Current.BoundingRectangle
    $name = Normalize $button.Current.Name
    $score = $rect.Y
    if ($name -match '^\d+(?:\.\d+)+(?:\s|$)') { $score += 100000 }
    $candidates += [pscustomobject]@{ button = $button; score = $score }
  }

  foreach ($candidate in @($candidates | Sort-Object score -Descending)) {
    try {
      Open-Silent $candidate.button
      $submenus = @(Get-ExpandableMenuItems $ProcessId)
      $name = Normalize $candidate.button.Current.Name
      if ($submenus.Count -ge 2 -or ($submenus.Count -ge 1 -and $name -match '^\d+(?:\.\d+)+(?:\s|$)')) { return $candidate.button }
      Close-Silent $candidate.button
    } catch { try { Close-Silent $candidate.button } catch {} }
  }
  throw 'The native model selector could not be discovered.'
}

function Get-Context([int]$ProcessId, $KnownSelector = $null) {
  $selector = if ($KnownSelector) { $KnownSelector } else { Find-Selector $ProcessId }
  $name = Normalize $selector.Current.Name
  Open-Silent $selector
  $submenus = @(Get-ExpandableMenuItems $ProcessId)
  $model = ''; $effort = ''; $modelMenu = $null; $effortMenu = $null

  for ($pass = 0; $pass -lt 2 -and (-not $modelMenu -or -not $effortMenu); $pass++) {
    foreach ($submenu in $submenus) {
      $label = Get-Label $submenu
      if (-not $label) { continue }
      $words = @($label -split ' ')
      for ($start = 0; $start -lt $words.Count; $start++) {
        $value = ($words[$start..($words.Count - 1)] -join ' ')
        if (-not $modelMenu -and $name.StartsWith($value + ' ')) { $model = $value; $modelMenu = $submenu }
        if (-not $effortMenu -and $name.EndsWith(' ' + $value)) { $effort = $value; $effortMenu = $submenu }
      }
    }
    if ($modelMenu -and $effortMenu) { break }
    $namedSubmenus = @($submenus | Where-Object { $_.Current.Name })
    if ($pass -eq 0 -and $namedSubmenus.Count -eq 1) {
      Open-Silent $namedSubmenus[0]
      $submenus = @(Get-ExpandableMenuItems $ProcessId)
    } else { break }
  }
  if (-not $modelMenu -or -not $effortMenu) {
    Close-Silent $selector
    throw 'The native model or reasoning submenu is not exposed.'
  }

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

function Find-ClosedSelector($Process, [string[]]$KnownModels = @()) {
  try {
    $selectorCandidates = @()
    foreach ($candidate in @(Get-Elements $Process.Id ([System.Windows.Automation.ControlType]::Button))) {
      if (-not (Test-Visible $candidate)) { continue }
      $rect = $candidate.Current.BoundingRectangle
      $candidateName = Normalize $candidate.Current.Name
      $knownMatch = $KnownModels | Where-Object { $candidateName -eq $_ -or $candidateName.StartsWith($_ + ' ') } | Select-Object -First 1
      $fallbackMatch = $candidateName -match '^\d+(?:\.\d+)+(?:\s|$)'
      if (($knownMatch -or $fallbackMatch) -and $rect.Width -ge 60 -and $rect.Width -le 320 -and $rect.Height -ge 20 -and $rect.Height -le 50) {
        $selectorCandidates += [pscustomobject]@{ button = $candidate; y = $rect.Y; x = $rect.X }
      }
    }
    return ($selectorCandidates | Sort-Object y, x -Descending | Select-Object -First 1).button
  } catch { [Console]::Error.WriteLine("Selector discovery unavailable: $($_.Exception.Message)") }
  return $null
}

function Get-SelectorBounds($Process, $Selector = $null, [string[]]$KnownModels = @()) {
  try {
    $button = if ($Selector) { $Selector } else { Find-ClosedSelector $Process -KnownModels $KnownModels }
    if (-not $button) { return $null }
    $rect = $button.Current.BoundingRectangle
    if ($rect.IsEmpty) { return $null }
    return @{
      x = [int]$rect.X
      y = [int]$rect.Y
      width = [int]$rect.Width
      height = [int]$rect.Height
      name = Normalize $button.Current.Name
    }
  } catch { [Console]::Error.WriteLine("Selector bounds unavailable: $($_.Exception.Message)") }
  return $null
}

if ($Mode -eq 'watch') {
  $previous = ''
  $knownModels = @()
  $cachedCodex = $null
  $cachedProcessId = 0
  $cachedSelector = $null
  try { $knownModels = @($ModelNamesJson | ConvertFrom-Json) } catch {}
  while ($true) {
    $codex = $cachedCodex
    try {
      if ($codex -and $codex.HasExited) { $codex = $null }
    } catch { $codex = $null }
    if (-not $codex) {
      $codex = Get-Codex
      $cachedCodex = $codex
    }
    $selectorBounds = $null
    if ($codex) {
      if ($cachedProcessId -ne $codex.Id) {
        $cachedProcessId = $codex.Id
        $cachedSelector = $null
      }
      if (-not $cachedSelector) { $cachedSelector = Find-ClosedSelector $codex -KnownModels $knownModels }
      if ($cachedSelector) {
        $selectorBounds = Get-SelectorBounds $codex -Selector $cachedSelector -KnownModels $knownModels
        if (-not $selectorBounds) { $cachedSelector = $null }
      }
    } else {
      $cachedCodex = $null
      $cachedProcessId = 0
      $cachedSelector = $null
    }
    $foreground = [CodexOverlayNative]::GetForegroundWindow()
    [uint32]$foregroundPid = 0
    [CodexOverlayNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid) | Out-Null
    $state = @{
      found = $null -ne $codex
      visible = $null -ne $codex -and ($foregroundPid -eq $codex.Id -or $foregroundPid -eq $OverlayPid)
      selector = $selectorBounds
    } | ConvertTo-Json -Compress
    if ($state -ne $previous) { [Console]::Out.WriteLine($state); [Console]::Out.Flush(); $previous = $state }
    Start-Sleep -Milliseconds 350
  }
}

$codex = Get-Codex
if (-not $codex) { throw 'The official Codex window could not be found.' }

if ($Mode -eq 'labels-fast') {
  $context = $null
  try {
    $context = Get-Context $codex.Id
    $modelOptions = Get-MenuOptions $codex.Id $context.modelMenu 1 20
    $effortOptions = Get-MenuOptions $codex.Id $context.effortMenu 4 5 -Effort
  } finally { if ($context) { Close-Silent $context.selector } }
  @{
    ok = $true
    models = @($modelOptions.labels)
    efforts = @($effortOptions.labels)
    supportedEfforts = @()
    speedLabel = ''
    speeds = @()
    speedIndex = -1
  } | ConvertTo-Json -Compress
  exit 0
}

if ($Mode -eq 'labels') {
  $context = Get-Context $codex.Id
  $originalModel = $context.model
  $modelOptions = Get-MenuOptions $codex.Id $context.modelMenu 1 20
  $models = @($modelOptions.labels)
  $effortOptions = Get-MenuOptions $codex.Id $context.effortMenu 4 5 -Effort
  $efforts = @($effortOptions.labels)
  Close-Silent $context.selector
  $supportedEfforts = @()
  foreach ($model in $models) {
    $indices = @()
    try {
      $context = Get-Context $codex.Id
      Open-Silent $context.modelMenu
      Select-Silent (Find-Element $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $model -Exact)
      $context = Get-Context $codex.Id
      $options = Get-MenuOptions $codex.Id $context.effortMenu 1 10 -Effort
      for ($i = 0; $i -lt $options.labels.Count; $i++) {
        $globalIndex = [Array]::IndexOf($efforts, $options.labels[$i])
        if ($globalIndex -ge 0) { $indices += $globalIndex }
      }
      Close-Silent $context.selector
    } catch { try { Close-Silent $context.selector } catch {} }
    $supportedEfforts += ,@($indices)
  }
  try {
    $context = Get-Context $codex.Id
    Open-Silent $context.modelMenu
    Select-Silent (Find-Element $codex.Id ([System.Windows.Automation.ControlType]::MenuItem) $originalModel -Exact)
  } catch {}

  $speedLabel = ''; $speeds = @(); $selected = -1
  try {
    $context = Get-Context $codex.Id
    $speed = Get-Speed $codex.Id $context
    $speedLabel = $speed.label; $speeds = @($speed.labels); $selected = $speed.selectedIndex
    Close-Silent $speed.owner
  } catch { try { Close-Silent $context.selector } catch {} }

  @{ ok = $true; models = $models; efforts = $efforts; supportedEfforts = $supportedEfforts; speedLabel = $speedLabel; speeds = $speeds; speedIndex = $selected } |
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

if ($ModelIndex -lt 0 -or $EffortIndex -lt 0) { throw 'Invalid selection index.' }

$context = Get-Context $codex.Id
$modelOptions = Get-MenuOptions $codex.Id $context.modelMenu 1 20
if ($ModelIndex -ge $modelOptions.items.Count) { throw 'The requested model is unavailable.' }
$model = $modelOptions.labels[$ModelIndex]
Select-Silent $modelOptions.items[$ModelIndex]
$null = Find-Element $codex.Id ([System.Windows.Automation.ControlType]::Button) ('^' + [Regex]::Escape($model) + '\s') -Timeout 3500

$context = Get-Context $codex.Id
$options = Get-MenuOptions $codex.Id $context.effortMenu 4 5 -Effort
if ($EffortIndex -ge $options.items.Count) { throw 'The requested reasoning level is unavailable.' }
$effort = $options.labels[$EffortIndex]
Select-Silent $options.items[$EffortIndex]
$expected = '^' + [Regex]::Escape($model) + '\s+' + [Regex]::Escape($effort) + '$'
$confirmed = Find-Element $codex.Id ([System.Windows.Automation.ControlType]::Button) $expected -Timeout 3500
@{ ok = $true; selection = $confirmed.Current.Name; inputMode = 'uia-silent' } | ConvertTo-Json -Compress

# Shared UI setup for timing tests. Callers snapshot preferences before changing them.
function Find-TestSettingsControl($root, [string]$id) {
    $control = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id))
    if (!$control) { throw "Settings control missing: $id" }
    return $control
}

function Invoke-TestSettingsControl($root, [string]$id) {
    (Find-TestSettingsControl $root $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-TestSegmentVisibility($root, [string]$segment, [int]$normal, [int]$maximized) {
    Invoke-TestSettingsControl $root 'generalNavigationButton'
    $picker = @{ Taskbar='taskbarRectStandIn'; Tray='trayRectStandIn'; Widgets='widgetsRectStandIn' }[$segment]
    Invoke-TestSettingsControl $root $picker
    Invoke-TestSettingsControl $root @('visibilityAlwaysButton','visibilityHoverButton','visibilityHiddenButton')[$normal]
    Invoke-TestSettingsControl $root @('maximizedVisibilityAlwaysButton','maximizedVisibilityHoverButton','maximizedVisibilityHiddenButton')[$maximized]
}

function Set-TestEffect($root, [string]$id, [int]$effect) {
    $combo = Find-TestSettingsControl $root $id
    $expand = $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    try {
        $labels = if ($id -eq 'hoverOpenEffectCombo') { @('None','Reveal vertically','Expand from the center') }
        else { @('None','Hide vertically','Shrink toward the center') }
        # Saved values are Slide=0, Expand=1, None=2; dropdown order differs.
        $index = @(1,2,0)[$effect]
        $items = $combo.FindAll([Windows.Automation.TreeScope]::Descendants,
            (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), ([Windows.Automation.ControlType]::ListItem)))
        # Match labels instead of relying on automation descendant ordering.
        $item = $items | Where-Object { $_.Current.Name -eq $labels[$index] } | Select-Object -First 1
        if (!$item) { throw "Effect choice missing: $($labels[$index])" }
        $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    } finally { $expand.Collapse() }
}

function Set-TestSegmentProfile($root, [string]$segment, $layout, [int]$normal, [int]$maximized) {
    # Enable hover controls while editing, then set the requested final visibility.
    Set-TestSegmentVisibility $root $segment 1 1
    Invoke-TestSettingsControl $root 'appearanceNavigationButton'
    $toggle = (Find-TestSettingsControl $root 'hoverPillCheckBox').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne [bool]$layout.HoverActivationPill) { $toggle.Toggle() }
    (Find-TestSettingsControl $root 'hoverDelayInput').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue([double]$layout.HoverHideDelaySeconds)
    Set-TestEffect $root 'hoverOpenEffectCombo' ([int]$layout.HoverOpenEffect)
    Set-TestEffect $root 'hoverCloseEffectCombo' ([int]$layout.HoverCloseEffect)
    Set-TestSegmentVisibility $root $segment $normal $maximized
}

function New-TestInstantHoverProfile {
    return @{ HoverActivationPill=$false; HoverHideDelaySeconds=0; HoverOpenEffect=2; HoverCloseEffect=2 }
}

function Get-TestMaximizedVisibility($layout, [int]$normal) {
    if ($null -eq $layout.MaximizedVisibility) { return $normal }
    return [int]$layout.MaximizedVisibility
}

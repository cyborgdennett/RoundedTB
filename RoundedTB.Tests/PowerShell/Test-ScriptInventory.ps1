$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$markup = [IO.File]::ReadAllText((Join-Path $repository 'RoundedTB\MainWindow.axaml'))
$names = @([regex]::Matches($markup, 'x:Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$failures = @()
$scripts = @(Get-ChildItem $PSScriptRoot -Filter '*.ps1') + @(Get-ChildItem (Join-Path $repository 'scripts') -Filter '*.ps1') + @(Get-ChildItem (Join-Path $repository 'packaging') -Filter '*.ps1')
foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($error in $errors) { $failures += "$($script.Name): $($error.Message)" }
    if ($script.DirectoryName -ne $PSScriptRoot -or $script.Name -eq 'Test-ScriptInventory.ps1') { continue }
    foreach ($token in $tokens) {
        if ($token.Kind -ne [Management.Automation.Language.TokenKind]::StringLiteral) { continue }
        $value = $token.Value
        # applyButton is intentionally checked for absence by the navigation test.
        if ($value -in @('applyButton', 'NavigationButton')) { continue }
        if ($value -match '^[a-zA-Z][a-zA-Z0-9]*(Button|CheckBox|Input|Combo|ScrollViewer|RectStandIn|Report)$' -and $value -notin $names) {
            $failures += "$($script.Name): unknown settings control '$value'."
        }
    }
    $source = [IO.File]::ReadAllText($script.FullName)
    if ($source -match 'autoHideComboBox|RevealOnMaximize|appVisibilityCombo|trayVisibilityCombo|widgetsVisibilityCombo') {
        $failures += "$($script.Name): references an obsolete settings test interface."
    }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Output "Parsed $($scripts.Count) PowerShell scripts; literal settings control IDs match the current page. No desktop changes performed."

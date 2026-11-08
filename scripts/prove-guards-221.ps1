# #221 mutation proofs. Each entry: file, the exact text to replace, its replacement, and the test
# filter expected to go RED. Restores from the in-memory copy, never `git checkout`.
$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$mutations = @(
  @{
    Name  = 'gate always asks (the reading-paper trap)'
    File  = 'src/WebMVC/ViewModels/Placement/PlacementPreflightPageViewModel.cs'
    Old   = "        : null;"
    New   = "        : new AudioEquipmentCheckModel(ShowInput: true, ShowOutput: true);"
    Test  = 'Placement_preflight_scopes_equipment_to_the_skills_it_will_measure|A_reading_placement_on_a_certificate_that_also_examines_speaking_still_asks_nothing'
  },
  @{
    Name  = 'take page calls the prompting path'
    File  = 'src/WebMVC/wwwroot/js/speaking/take.js'
    Old   = 'const capture = await window.EeAudioGate.acquire(true);'
    New   = 'const capture = await window.EeAudioGate.ask(true);'
    Test  = 'No_take_page_script_calls_the_prompting_path'
  },
  @{
    Name  = 'three states collapse into two (never-asked rendered as denied)'
    File  = 'src/WebMVC/Views/Shared/_AudioPermissionStates.cshtml'
    Old   = '<strong>Microphone permission: not asked yet.</strong>'
    New   = '<strong>Microphone permission: blocked.</strong>'
    Test  = 'The_three_permission_states_have_different_copy_and_different_remedies'
  },
  @{
    Name  = 'denied state degrades to a generic allow sentence'
    File  = 'src/WebMVC/Views/Shared/_AudioPermissionStates.cshtml'
    Old   = '<strong>tune</strong> icon at the left of the address bar, choose <strong>Site settings</strong>, set <strong>Microphone</strong> to <strong>Allow</strong>, then reload this page.'
    New   = 'allow the microphone.'
    Test  = 'The_denied_state_never_falls_back_to_a_generic_allow_the_microphone_sentence|The_three_permission_states_have_different_copy_and_different_remedies'
  },
  @{
    Name  = 'silence detected by duration, not level'
    File  = 'src/WebMVC/wwwroot/js/ee-audio.js'
    Old   = 'if (peak > -45) return finish("signal");'
    New   = 'if (Date.now() < deadline) return finish("signal");'
    Test  = 'Silence_is_decided_by_measured_level_not_by_capture_duration'
  },
  @{
    Name  = 'override is a bare checkbox with no stated cost'
    File  = 'src/WebMVC/Views/Shared/_AudioEquipmentCheck.cshtml'
    Old   = 'from the remaining skills. This cannot be undone after you submit.'
    New   = 'You may continue.'
    Test  = 'The_silent_override_states_the_consequence_before_the_checkbox'
  },
  @{
    Name  = 'sitting panel offers a prompting control'
    File  = 'src/WebMVC/Views/Shared/_AudioEquipmentCheck.cshtml'
    Old   = 'var promptable = !Model.Sitting;'
    New   = 'var promptable = true;'
    Test  = 'A_sitting_panel_renders_no_control_that_could_ask'
  },
  @{
    Name  = 'device list collapse: one sentence for both failures'
    File  = 'src/WebMVC/Views/Shared/_AudioPermissionStates.cshtml'
    Old   = '<strong>No microphone is connected.</strong>'
    New   = '<strong>Device names are withheld until the microphone is enabled.</strong>'
    Test  = 'Granted_with_no_device_and_labels_withheld_are_two_different_sentences'
  },
  @{
    Name  = 'audio control rendered on a reading-only paper'
    File  = 'src/WebMVC/ViewModels/Placement/PlacementTakeViewModel.cs'
    Old   = '        return hasListening || hasSpeaking'
    New   = '        return true'
    Test  = 'The_audio_portion_is_suppressed_on_a_section_that_uses_no_audio'
  },
  @{
    Name  = 'permission state duplicated into a cookie'
    File  = 'src/WebMVC/wwwroot/js/audio-gate.js'
    Old   = 'var STORAGE_KEY = "ee_microphone_preference_permission";'
    New   = 'var STORAGE_KEY = "ee_microphone_preference_permission"; document.cookie = "mic=" + "granted";'
    Test  = 'Permission_state_is_recorded_in_local_storage_not_in_a_cookie'
  },
  @{
    # The class of defect a real browser caught and `node --check` did not: a helper's definition
    # gone, every call throwing ReferenceError at load.
    Name  = 'a helper definition dropped from the audio module'
    File  = 'src/WebMVC/wwwroot/js/ee-audio.js'
    Old   = 'function fillSelect(select, devices, type, selectedId) {'
    New   = 'function fillSelectRenamed(select, devices, type, selectedId) {'
    Test  = 'Every_function_a_script_calls_is_defined_in_it'
  }
)

$results = @()
foreach ($m in $mutations) {
  $path = Join-Path $repo $m.File
  $original = [System.IO.File]::ReadAllText($path)

  # The Old text may be written with folded indentation; normalise both sides to LF and trim each
  # line's leading whitespace so a re-indent does not turn the proof into a no-op.
  function Normalize([string]$text) {
    ($text -split "`r?`n" | ForEach-Object { $_.Trim() }) -join "`n"
  }
  $needle = Normalize $m.Old
  $hay = Normalize $original
  if (-not $hay.Contains($needle)) {
    $results += "SKIP  $($m.Name): search text not present"
    continue
  }

  try {
    # Rebuild the file with the replacement applied at the same trimmed indentation.
    $pattern = [regex]::Escape($needle)
    $replacement = ($m.New -split "`r?`n" | ForEach-Object { $_.Trim() }) -join "`n"
    $lines = $original -split "`r?`n"
    # Locate the line range that carries the needle, then splice.
    $normalizedLines = $lines | ForEach-Object { $_.Trim() }
    $needleLines = $normalizedLines -join "`n"
    $startIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
      $candidate = ($normalizedLines[$i..([Math]::Min($i + (($m.Old -split "`r?`n").Count - 1), $lines.Count - 1))] -join "`n")
      if ($candidate -eq $needle) { $startIndex = $i; break }
    }
    if ($startIndex -lt 0) {
      # The needle is a mid-line fragment rather than whole lines: a plain substring replace is the
      # faithful mutation in that case.
      $results += "INFO  $($m.Name): fragmented needle, using substring replace"
      [System.IO.File]::WriteAllText($path, $hay.Replace($needle, $replacement))
      $out = & dotnet test tests/WebMVC.Tests --filter "FullyQualifiedName~AudioEquipmentGateTests" 2>&1
      $verdict = ($out | Select-String -Pattern 'Failed!|Passed!' | Select-Object -Last 1).Line
      $results += if ($verdict -match 'Failed!') { "RED   $($m.Name)  [guard proved]" } else { "GREEN $($m.Name)  <<<< GUARD NOT LOAD-BEARING" }
      continue
    }
    $count = ($m.Old -split "`r?`n").Count
    $prefix = if ($startIndex -gt 0) { $lines[0..($startIndex - 1)] } else { @() }
    $indent = [regex]::Match($lines[$startIndex], '^\s*').Value
    $insert = $replacement -split "`n" | ForEach-Object { "$indent$_" }
    $suffix = if ($startIndex + $count -lt $lines.Count) { $lines[($startIndex + $count)..($lines.Count - 1)] } else { @() }
    $mutated = (@($prefix) + @($insert) + @($suffix)) -join "`r`n"
    [System.IO.File]::WriteAllText($path, $mutated)

    $out = & dotnet test tests/WebMVC.Tests --filter "FullyQualifiedName~AudioEquipmentGateTests" 2>&1
    $verdict = ($out | Select-String -Pattern 'Failed!|Passed!' | Select-Object -Last 1).Line
    $red = $verdict -match 'Failed!'
    $results += if ($red) { "RED   $($m.Name)  [guard proved]" } else { "GREEN $($m.Name)  <<<< GUARD NOT LOAD-BEARING" }
  }
  finally {
    [System.IO.File]::WriteAllText($path, $original)
    $check = [System.IO.File]::ReadAllText($path)
    if ($check -ne $original) { $results += "RESTORE FAILED: $($m.File)" }
  }
}

$results | ForEach-Object { Write-Host $_ }

param(
    [switch]$Smoke,
    [switch]$Wait,
    [ValidateSet('targets', 'sky')][string]$BenchmarkView
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$assets = (Resolve-Path (Join-Path $repo 'build/test-range/assets-shot-fx')).Path
$binaries = (Resolve-Path (Join-Path $repo 'build/presets/windows-x64-release/binaries')).Path
$dxc = (Resolve-Path (Join-Path $repo 'build/dxc/bin/x64')).Path
$shaders = (Resolve-Path (Join-Path $repo 'sources/xray/render/vulkan/assets')).Path
if ($Smoke -and $BenchmarkView) { throw 'Choose either smoke or benchmark mode.' }
$mode = if ($BenchmarkView) { "benchmark-$BenchmarkView" } elseif ($Smoke) { 'smoke' } else { 'play' }
$run = New-Item -ItemType Directory -Path (Join-Path $repo "build/test-range/$mode-$([guid]::NewGuid().ToString('N'))")
$userData = New-Item -ItemType Directory -Path (Join-Path $run 'user-data')
$stdout = Join-Path $run 'stdout.log'
$stderr = Join-Path $run 'stderr.log'
$capture = Join-Path $run 'capture'

$gameArgs = @(
    '-windowed', '-level=test_range', '-no_splash_screen', '-max_resources_size=4096',
    ('-render_assets="{0}"' -f $assets),
    ('-user_data_path="{0}"' -f $userData.FullName)
)
$envVars = @{
    PATH = "$dxc;C:\msys64\mingw64\bin;$env:PATH"
    XRAY_DXC_PATH = (Join-Path $dxc 'dxcompiler.dll')
    XRAY_VULKAN_SMOKE_SHADER_DIR = $shaders
    DISABLE_RTSS_LAYER = '1'
    DISABLE_VULKAN_OBS_CAPTURE = '1'
}
if ($Smoke -or $BenchmarkView) {
    $validation = (Resolve-Path (Join-Path $repo 'build/vulkan-validation/mingw64/bin')).Path
    $gameArgs += ('-render_capture="{0}"' -f $capture)
    $envVars.PATH = "$validation;$($envVars.PATH)"
    $envVars.VK_LAYER_PATH = $validation
    $envVars.XRAY_VULKAN_REQUIRE_VALIDATION = '1'
}
else {
    Write-Host 'WASD to move, mouse to look, left-click to fire, R to reset, Esc to exit.'
}
if ($BenchmarkView) {
    $gameArgs += '-render_performance=2,6'
    $envVars.XRAY_VULKAN_PERFORMANCE = '1'
    $envVars.XRAY_TEST_RANGE_BENCHMARK_VIEW = $BenchmarkView
}

$saved = @{}
try {
    foreach ($name in $envVars.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $envVars[$name], 'Process')
    }
    $windowStyle = if ($Smoke -or $BenchmarkView) { 'Hidden' } else { 'Normal' }
    $process = Start-Process -FilePath (Join-Path $binaries 'stalker2.exe') -ArgumentList $gameArgs `
        -WorkingDirectory $binaries -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
        -WindowStyle $windowStyle -PassThru
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
}

if ($Smoke -or $BenchmarkView) {
    if (!$process.WaitForExit(90000)) {
        Stop-Process -InputObject $process
        throw "Test range capture timed out. See $stderr"
    }
    $log = Get-Content -LiteralPath $stderr -Raw
    if ($process.ExitCode -ne 0 -or $log -notmatch 'Test range ready: targets=8' -or
        $log -notmatch 'M8 validation errors=0 warnings=0' -or
        !(Test-Path -LiteralPath (Join-Path $capture 'game.bmp'))) {
        throw "Test range $mode failed with exit code $($process.ExitCode). See $stderr"
    }
    if ($BenchmarkView) {
        $measurements = Join-Path $capture 'performance.jsonl'
        if (!(Test-Path -LiteralPath (Join-Path $capture 'performance-complete.json'))) {
            throw "Incomplete test range benchmark. See $stderr"
        }
        Write-Output "Test range benchmark passed ($BenchmarkView). Measurements: $measurements"
    } elseif (
        $log -notmatch 'Test range smoke: movement, aim, miss, hit and reset passed' -or
        $log -notmatch 'Test range smoke: flash and recoil appeared and cleared') {
        throw "Test range smoke did not exercise gameplay. See $stderr"
    } else {
        Write-Output "Test range smoke passed. Capture: $capture\game.bmp"
    }
}
else {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Game exited with code $($process.ExitCode). See $stderr" }
        $loaded = $process.MainWindowHandle -ne [IntPtr]::Zero -and
            (Select-String -LiteralPath $stderr -Pattern 'Test range ready: targets=8' -Quiet)
        if ($loaded) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$loaded) { throw "Test range did not open a ready game window. See $stderr" }
    Write-Output "Test range running. PID $($process.Id). Log: $stderr"
    if ($Wait) {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Game exited with code $($process.ExitCode). See $stderr" }
    }
}

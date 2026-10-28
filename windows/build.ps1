<#
.SYNOPSIS
    Builds, tests and packages TodoApp for Windows (the counterpart of ../build.sh).

.DESCRIPTION
    Needs the .NET SDK 10 (see global.json) and nothing else: no Visual Studio.

      .\build.ps1             Publishes a self-contained Release build to dist\TodoApp\ and zips it as
                              dist\TodoApp-win-x64.zip
      .\build.ps1 -Run        Builds Debug, then starts the app
      .\build.ps1 -Install    Publishes, copies the app to %LOCALAPPDATA%\Programs\TodoApp\ and adds a Start menu
                              shortcut
      .\build.ps1 -Check      Runs the tests

    The build is not signed: the first start shows a SmartScreen warning ("More info", then "Run anyway").
    The script exits with a non-zero code when a step fails.
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Run,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$root = $PSScriptRoot
# dotnet finds global.json (the SDK version and the test runner) from the current folder.
Set-Location $root
$solution = Join-Path $root 'TodoApp.Windows.slnx'
$project = Join-Path $root 'src\TodoApp\TodoApp.csproj'
$dist = Join-Path $root 'dist'
$publishDir = Join-Path $dist 'TodoApp'
$zip = Join-Path $dist 'TodoApp-win-x64.zip'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed (exit code $LASTEXITCODE)" }
}

# A self-contained Release build in dist\TodoApp, and the zip of it.
function Publish-App {
    foreach ($path in $publishDir, $zip) {
        # Only ever clear what this script made inside dist.
        if ($path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path $path)) {
            Remove-Item $path -Recurse -Force
        }
    }

    Invoke-Dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
    Compress-Archive -Path $publishDir -DestinationPath $zip
    Write-Host "Published $publishDir"
    Write-Host "Zipped    $zip"
}

try {
    if ($Check) {
        Invoke-Dotnet test $solution -v q
    }
    elseif ($Run) {
        Invoke-Dotnet build $project -c Debug -v q
        $exe = Join-Path $root 'src\TodoApp\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TodoApp.exe'
        if (-not (Test-Path $exe)) { throw "The build did not produce $exe" }
        Start-Process $exe
    }
    elseif ($Install) {
        Publish-App

        $target = Join-Path $env:LOCALAPPDATA 'Programs\TodoApp'
        $running = @(Get-Process TodoApp -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) })
        if ($running.Count -gt 0) { throw "TodoApp is running from $target. Close it and run the install again." }

        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item (Join-Path $publishDir '*') $target -Recurse

        $exe = Join-Path $target 'TodoApp.exe'
        $shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\TodoApp.lnk'
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
        $link.TargetPath = $exe
        $link.WorkingDirectory = $target
        $link.IconLocation = "$exe,0"
        $link.Description = 'TodoApp'
        $link.Save()
        Write-Host "Installed $target"
        Write-Host "Shortcut  $shortcut"
    }
    else {
        Publish-App
    }
}
catch {
    [Console]::Error.WriteLine("build.ps1: $($_.Exception.Message)")
    exit 1
}

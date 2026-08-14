[CmdletBinding()]
param(
    [ValidateSet("all", "setup", "build", "test")]
    [string]$Mode = "all",
    [switch]$SkipUnityInstall,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$UnityVersion = "2023.2.22f1"
$UnityChangeset = "6b19bf4f8115"
$EnvironmentName = "mosim-rl"
$MinimumFreeGiB = 25

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList
    )

    & $FilePath @ArgumentList | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Find-Conda {
    $Command = Get-Command conda.exe -ErrorAction SilentlyContinue
    if ($null -ne $Command) {
        return $Command.Source
    }

    $Candidates = @(
        (Join-Path $env:USERPROFILE "miniconda3\Scripts\conda.exe"),
        (Join-Path $env:LOCALAPPDATA "miniconda3\Scripts\conda.exe"),
        (Join-Path $env:USERPROFILE "anaconda3\Scripts\conda.exe"),
        (Join-Path $env:ProgramData "miniconda3\Scripts\conda.exe")
    )
    return $Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Find-UnityHub {
    $Candidates = @(
        (Join-Path $env:ProgramFiles "Unity Hub\Unity Hub.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Unity Hub\Unity Hub.exe")
    )
    return $Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Find-UnityEditor {
    $Candidates = @(
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"),
        (Join-Path $env:ProgramFiles "Unity Hub\Editor\$UnityVersion\Editor\Unity.exe")
    )
    if ($null -ne $env:UNITY_EDITOR -and $env:UNITY_EDITOR.Length -gt 0) {
        $Candidates = @($env:UNITY_EDITOR) + $Candidates
    }
    return $Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Install-WingetPackage {
    param([string]$PackageId)

    $Winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($null -eq $Winget) {
        throw "winget is required to install $PackageId automatically. Install Microsoft App Installer and rerun this script."
    }
    Invoke-Checked $Winget.Source @(
        "install", "--id", $PackageId, "--exact", "--silent",
        "--accept-package-agreements", "--accept-source-agreements"
    )
}

function Assert-FreeSpace {
    $DriveName = ([System.IO.Path]::GetPathRoot($RepositoryRoot)).TrimEnd("\").TrimEnd(":")
    $Drive = Get-PSDrive -Name $DriveName
    $FreeGiB = [math]::Floor($Drive.Free / 1GB)
    if ($FreeGiB -lt $MinimumFreeGiB) {
        throw "MoSimulator setup requires at least $MinimumFreeGiB GiB free; $FreeGiB GiB is available on $($Drive.Name):."
    }
    Write-Host "Preflight passed: $FreeGiB GiB free on $($Drive.Name):."
}

function Install-PythonEnvironment {
    $Conda = Find-Conda
    if ($null -eq $Conda) {
        Write-Host "Installing Miniconda with winget..."
        Install-WingetPackage "Anaconda.Miniconda3"
        $Conda = Find-Conda
    }
    if ($null -eq $Conda) {
        throw "Miniconda installation completed, but conda.exe was not found. Open a new terminal and rerun scripts\setup_windows.bat."
    }

    $EnvironmentData = (& $Conda env list --json | ConvertFrom-Json)
    $EnvironmentExists = @($EnvironmentData.envs) | Where-Object {
        (Split-Path $_ -Leaf) -eq $EnvironmentName
    }
    if ($null -eq $EnvironmentExists) {
        Invoke-Checked $Conda @(
            "create", "--name", $EnvironmentName, "--yes", "python=3.11", "pip"
        )
    }

    $EditablePackage = Join-Path $RepositoryRoot "python[test,camera]"
    Invoke-Checked $Conda @(
        "run", "--name", $EnvironmentName, "python", "-m", "pip", "install",
        "--timeout", "600", "--editable", $EditablePackage
    )
    Invoke-Checked $Conda @(
        "run", "--name", $EnvironmentName, "python", "-c",
        "import gymnasium, stable_baselines3, torch, cv2; print('Python environment ready:', gymnasium.__version__, stable_baselines3.__version__, torch.__version__)"
    )
    return $Conda
}

function Install-UnityEditor {
    $Editor = Find-UnityEditor
    if ($null -ne $Editor) {
        if (-not $SkipUnityInstall) {
            $Hub = Find-UnityHub
            if ($null -eq $Hub) {
                Write-Host "Installing Unity Hub with winget..."
                Install-WingetPackage "Unity.UnityHub"
                $Hub = Find-UnityHub
            }
            if ($null -eq $Hub) {
                throw "Unity Hub is required to verify Windows Dedicated Server Build Support."
            }
            Write-Host "Ensuring Windows Dedicated Server Build Support is installed..."
            try {
                Invoke-Checked $Hub @(
                    "--", "--headless", "install-modules", "--version", $UnityVersion,
                    "--module", "windows-server"
                )
            }
            catch {
                Start-Process $Hub
                throw "Unity Hub could not add Windows Dedicated Server Build Support. Add it under Installs > $UnityVersion > Manage > Add modules, then rerun this script. $($_.Exception.Message)"
            }
        }
        return $Editor
    }
    if ($SkipUnityInstall) {
        throw "Unity $UnityVersion was not found and -SkipUnityInstall was supplied."
    }

    $Hub = Find-UnityHub
    if ($null -eq $Hub) {
        Write-Host "Installing Unity Hub with winget..."
        Install-WingetPackage "Unity.UnityHub"
        $Hub = Find-UnityHub
    }
    if ($null -eq $Hub) {
        throw "Unity Hub installation completed, but Unity Hub was not found. Open a new terminal and rerun scripts\setup_windows.bat."
    }

    Write-Host "Installing Unity $UnityVersion and Windows Dedicated Server Build Support..."
    try {
        Invoke-Checked $Hub @(
            "--", "--headless", "install", "--version", $UnityVersion,
            "--changeset", $UnityChangeset, "--module", "windows-server"
        )
    }
    catch {
        Start-Process $Hub
        throw "Unity installation needs attention in Unity Hub. Sign in, activate a license, install Unity $UnityVersion with Windows Dedicated Server Build Support, then rerun this script. $($_.Exception.Message)"
    }

    $Editor = Find-UnityEditor
    if ($null -eq $Editor) {
        Start-Process $Hub
        throw "Unity $UnityVersion was not found after the Hub command. Complete sign-in/license activation in Unity Hub, install that editor version, and rerun this script."
    }
    return $Editor
}

function Build-UnityPlayers {
    param([string]$Editor)

    $BuildRoot = Join-Path $RepositoryRoot "_Build\RL"
    New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null
    $ServerLog = Join-Path $BuildRoot "build-windows-server.log"
    $DevelopmentLog = Join-Path $BuildRoot "build-windows-development.log"

    try {
        Invoke-Checked $Editor @(
            "-batchmode", "-quit", "-projectPath", $RepositoryRoot,
            "-executeMethod", "MoSimRlBuild.BuildWindowsServer", "-logFile", $ServerLog
        )
        Invoke-Checked $Editor @(
            "-batchmode", "-quit", "-projectPath", $RepositoryRoot,
            "-executeMethod", "MoSimRlBuild.BuildWindowsDevelopment", "-logFile", $DevelopmentLog
        )
    }
    catch {
        $Hub = Find-UnityHub
        if ($null -ne $Hub) {
            Start-Process $Hub
        }
        throw "Unity build failed. Check $ServerLog and $DevelopmentLog. If the log reports a license error, activate the license in Unity Hub and rerun the script. $($_.Exception.Message)"
    }

    $Server = Join-Path $BuildRoot "WindowsServer\MoSimRL.exe"
    $Development = Join-Path $BuildRoot "WindowsDevelopment\MoSimRL.exe"
    if (-not (Test-Path $Server) -or -not (Test-Path $Development)) {
        throw "Unity reported success but one or more Windows players were not created. Check the build logs in $BuildRoot."
    }
    [Environment]::SetEnvironmentVariable("MOSIM_EXECUTABLE", $Server, "User")
    $env:MOSIM_EXECUTABLE = $Server
    Write-Host "Windows players ready. MOSIM_EXECUTABLE was saved for the current user."
}

function Test-PythonProject {
    param([string]$Conda)

    $OldPluginSetting = $env:PYTEST_DISABLE_PLUGIN_AUTOLOAD
    $env:PYTEST_DISABLE_PLUGIN_AUTOLOAD = "1"
    try {
        Invoke-Checked $Conda @(
            "run", "--name", $EnvironmentName, "pytest",
            (Join-Path $RepositoryRoot "python\tests"), "-m", "not integration"
        )
    }
    finally {
        $env:PYTEST_DISABLE_PLUGIN_AUTOLOAD = $OldPluginSetting
    }
}

function Test-UnitySmoke {
    param([string]$Conda)

    if ($SkipSmokeTest) {
        return
    }
    Invoke-Checked $Conda @(
        "run", "--name", $EnvironmentName, "mosim-random",
        "--executable", $env:MOSIM_EXECUTABLE, "--steps", "20", "--check-env"
    )
}

Assert-FreeSpace

if ($Mode -eq "setup") {
    $Conda = Install-PythonEnvironment
    [void](Install-UnityEditor)
    Write-Host "Setup complete. Run scripts\setup_windows.bat build after Unity Hub sign-in and license activation."
    exit 0
}

if ($Mode -eq "test") {
    $Conda = Find-Conda
    if ($null -eq $Conda) {
        throw "Conda was not found. Run scripts\setup_windows.bat setup first."
    }
    Test-PythonProject $Conda
    exit 0
}

$Conda = Install-PythonEnvironment
$Editor = Install-UnityEditor
Build-UnityPlayers $Editor
Test-PythonProject $Conda
Test-UnitySmoke $Conda

Write-Host ""
Write-Host "MoSim RL is ready. Useful commands:"
Write-Host "  conda run -n mosim-rl mosim-gamepad"
Write-Host "  conda run -n mosim-rl mosim-random --graphical --steps 2000"
Write-Host "  conda run -n mosim-rl mosim-train --total-timesteps 100000"

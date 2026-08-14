[CmdletBinding()]
param(
    [ValidateSet("build", "test", "smoke")]
    [string]$Mode = "smoke"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Image = if ($env:MOSIM_DOCKER_IMAGE) { $env:MOSIM_DOCKER_IMAGE } else { "mosim-rl:local" }
$UnityServer = if ($env:MOSIM_UNITY_SERVER_DIR) {
    $env:MOSIM_UNITY_SERVER_DIR
} else {
    Join-Path $RepositoryRoot "_Build\RL\LinuxServer"
}

function Invoke-Checked {
    param([string[]]$Arguments)

    & docker.exe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed with exit code $LASTEXITCODE`: docker $($Arguments -join ' ')"
    }
}

if ($null -eq (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop is required and docker.exe was not found."
}
Invoke-Checked @("version")

function Build-TestImage {
    Invoke-Checked @(
        "build", "--platform", "linux/amd64", "--target", "test",
        "--tag", "${Image}-test", $RepositoryRoot
    )
}

function Assert-UnityServer {
    $Executable = Join-Path $UnityServer "MoSimRL.x86_64"
    if (-not (Test-Path $Executable -PathType Leaf)) {
        throw "Linux Unity server not found at $Executable. Install Unity Linux Dedicated Server Build Support and run MoSimRlBuild.BuildLinuxServer before building the runtime image."
    }
}

function Build-RuntimeImage {
    Assert-UnityServer
    Invoke-Checked @(
        "build", "--platform", "linux/amd64", "--target", "runtime",
        "--build-context", "unity_server=$UnityServer",
        "--tag", $Image, $RepositoryRoot
    )
}

function Test-Dependencies {
    Build-TestImage
    Invoke-Checked @("run", "--rm", "--init", "${Image}-test")
}

function Test-Smoke {
    Build-RuntimeImage
    $Runs = Join-Path $RepositoryRoot "runs"
    New-Item -ItemType Directory -Force -Path $Runs | Out-Null
    Invoke-Checked @(
        "run", "--rm", "--init", "--shm-size", "2g",
        "--volume", "${Runs}:/workspace/runs", $Image,
        "mosim-smoke", "--steps", "20"
    )
}

switch ($Mode) {
    "build" { Build-RuntimeImage }
    "test" { Test-Dependencies }
    "smoke" { Test-Dependencies; Test-Smoke }
}

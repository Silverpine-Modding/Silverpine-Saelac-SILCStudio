[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string[]]$Runtime = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SilverpineSilcEditor.csproj')
$version = $project.Project.PropertyGroup.Version
if (($Runtime | Where-Object { $_ -like 'win-*' }) -and
    !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'AssetReader/dist/SilverpineAssetReader.exe'))) {
    throw 'Build AssetReader/build_reader.py on Windows before publishing Windows packages.'
}
Push-Location -LiteralPath $PSScriptRoot
try {
    # Sequential: parallel publishes of one project would race over obj/project.assets.json.
    foreach ($rid in $Runtime) {
        Write-Host "Publishing SILC Studio $version for $rid"
        & dotnet publish SilverpineSilcEditor.csproj -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
            "-p:PublishDir=publish/release-$version/$rid/"
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }
    }
}
finally { Pop-Location }

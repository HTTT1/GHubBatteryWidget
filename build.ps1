$ErrorActionPreference = 'Stop'

$compilerCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'The .NET Framework C# compiler was not found.'
}

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$gac = "$env:WINDIR\Microsoft.NET\assembly"
$windowsBase = Get-ChildItem $gac -Filter WindowsBase.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$presentationCore = Get-ChildItem $gac -Filter PresentationCore.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$presentationFramework = Get-ChildItem $gac -Filter PresentationFramework.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$systemXaml = Get-ChildItem $gac -Filter System.Xaml.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName

& $compiler /nologo /target:winexe /optimize+ `
    "/out:$dist\GHubBatteryWidget.exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "/reference:$windowsBase" `
    "/reference:$presentationCore" `
    "/reference:$presentationFramework" `
    "/reference:$systemXaml" `
    /reference:Microsoft.CSharp.dll `
    (Join-Path $PSScriptRoot 'GHubBatteryWidget.cs')

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

Get-FileHash (Join-Path $dist 'GHubBatteryWidget.exe') -Algorithm SHA256

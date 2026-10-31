param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'RaffleGame\RaffleGame.csproj'
$manifestPath = Join-Path $repo 'RaffleGame\RaffleGame.json'
$solution = Join-Path $repo 'RaffleGame.sln'
$expectedIcon = 'https://raw.githubusercontent.com/reyalexffxiv/RaffleGame/master/images/icon.png'
[xml]$projectXml = Get-Content $project
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
$assemblyVersion = [string]($projectXml.Project.PropertyGroup.AssemblyVersion | Select-Object -First 1)
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($version -ne '1.0.0-beta' -or $assemblyVersion -ne '1.0.0.0') {
    throw 'Unexpected RaffleGame release version.'
}
if ($manifest.InternalName -ne 'RaffleGame' -or $manifest.Name -ne 'Raffle Game' -or
    $manifest.AssemblyVersion -ne $assemblyVersion -or $manifest.DalamudApiLevel -ne 15 -or
    $manifest.IconUrl -ne $expectedIcon) {
    throw 'RaffleGame source manifest disagrees with expected release metadata.'
}
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$output = Join-Path $dist "RaffleGame-v$version.zip"
# Remove an old release ZIP first; a failed build must never leave a publishable stale artifact.
if (Test-Path $output) { Remove-Item $output -Force }
$started = (Get-Date).ToUniversalTime()
Write-Host "Building Raffle Game $version ($Configuration)..."
& dotnet build $solution -c $Configuration -t:Rebuild
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE): no release ZIP created." }
$latest = Join-Path $repo "RaffleGame\bin\x64\$Configuration\RaffleGame\latest.zip"
if (-not (Test-Path $latest) -or (Get-Item $latest).LastWriteTimeUtc -lt $started.AddSeconds(-3)) {
    throw "No freshly created DalamudPackager ZIP at $latest"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($latest)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
    if ($names.Count -ne @($names | Select-Object -Unique).Count) { throw 'Duplicate ZIP entries.' }
    foreach ($required in @('RaffleGame.dll','RaffleGame.deps.json','RaffleGame.json')) {
        if ($names -notcontains $required) { throw "Missing release ZIP root entry: $required" }
    }
    foreach ($name in $names) {
        if ($name.Contains('/') -or $name.Contains('\')) { throw "Unexpected nested release entry: $name" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('RaffleGame.json').Open())
    try { $packedManifest = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    if ($packedManifest.InternalName -ne 'RaffleGame' -or $packedManifest.Name -ne 'Raffle Game' -or
        $packedManifest.AssemblyVersion -ne $assemblyVersion -or $packedManifest.DalamudApiLevel -ne 15 -or
        $packedManifest.IconUrl -ne $expectedIcon) {
        throw 'Packaged manifest failed release metadata checks.'
    }
    $tempDll = Join-Path $env:TEMP "RaffleGame-$([guid]::NewGuid()).dll"
    try {
        $inputStream = $archive.GetEntry('RaffleGame.dll').Open()
        $outStream = [IO.File]::Create($tempDll)
        try { $inputStream.CopyTo($outStream) }
        finally { $outStream.Dispose(); $inputStream.Dispose() }
        $actualAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($tempDll).Version.ToString()
        if ($actualAssemblyVersion -ne $assemblyVersion) {
            throw "Packaged DLL version $actualAssemblyVersion does not match $assemblyVersion"
        }
    }
    finally { Remove-Item $tempDll -ErrorAction SilentlyContinue }
}
finally { $archive.Dispose() }
Copy-Item $latest $output
Write-Host "Verified package: $output" -ForegroundColor Green

param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
$appOutput = Join-Path $artifacts 'distribution'
New-Item -ItemType Directory -Path $appOutput -Force | Out-Null
dotnet run --project (Join-Path $repoRoot 'tests\AwayLock.PolicyTests\AwayLock.PolicyTests.csproj') -c Release -- --export-vectors (Join-Path $artifacts 'policy-vectors.txt')
if ($LASTEXITCODE -ne 0) { throw 'Presence policy checks failed' }
cmake -S (Join-Path $repoRoot 'src\AwayLock.NativeAgent') -B (Join-Path $artifacts 'native') -A x64
if ($LASTEXITCODE -ne 0) { throw 'Configuring native agent failed' }
cmake --build (Join-Path $artifacts 'native') --config Release
if ($LASTEXITCODE -ne 0) { throw 'Building native agent failed' }
& (Join-Path $artifacts 'native\Release\AwayLock.NativePolicyTests.exe') (Join-Path $artifacts 'policy-vectors.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native policy checks failed' }
Copy-Item -LiteralPath (Join-Path $artifacts 'native\Release\AwayLock.Agent.exe') -Destination (Join-Path $appOutput 'AwayLock.Agent.exe') -Force
foreach ($project in @('AwayLock')) {
    $projectPath = Join-Path $repoRoot "src\$project\$project.csproj"
    $publishOutput = Join-Path $artifacts "publish-$project"
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed" }
    Copy-Item -LiteralPath (Join-Path $publishOutput "$project.exe") -Destination (Join-Path $appOutput "$project.exe") -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $appOutput -Force
$notices = Join-Path $appOutput 'Notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $repoRoot 'src\AwayLock\obj\project.assets.json') -Raw | ConvertFrom-Json
$noticePackages = @($assets.libraries.PSObject.Properties.Name)
# Runtime and SDK packs are downloadDependencies rather than libraries in .NET 8.
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($dependency in $framework.Value.downloadDependencies) {
        $version = $dependency.version.Trim('[', ']').Split(',')[0].Trim()
        $noticePackages += $dependency.name + '/' + $version
    }
}
foreach ($package in ($noticePackages | Sort-Object -Unique)) {
    if ($package -notmatch '^Microsoft\.(NETCore\.App\.Runtime\.win-x64|WindowsDesktop\.App\.Runtime\.win-x64|Windows\.SDK\.NET\.Ref)/') { continue }
    $packagePath = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $package.ToLowerInvariant() } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $packagePath) { throw "Notice package not found: $package" }
    $noticeFiles = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^LICENSE(\.TXT)?$|^THIRD.PARTY.NOTICES' })
    # The Windows SDK pack supplies its upstream license URL in its NuGet manifest.
    if ($noticeFiles.Count -eq 0) { $noticeFiles = @(Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File) }
    if ($noticeFiles.Count -eq 0) { throw "No license/notice metadata in $package" }
    foreach ($noticeFile in $noticeFiles) {
        $noticeName = $package.Split('/')[0] + '-' + $noticeFile.Name + '.txt'
        Copy-Item -LiteralPath $noticeFile.FullName -Destination (Join-Path $notices $noticeName) -Force
    }
}
if (@(Get-ChildItem -LiteralPath $notices -File).Count -eq 0) { throw 'No third-party notices collected' }
if (-not $SkipInstaller) {
    $compiler = Join-Path $repoRoot '.tools\inno\tools\ISCC.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        $packageUri = 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg'
        $archive = Join-Path $artifacts 'inno-6.7.3.zip'
        Invoke-WebRequest -Uri $packageUri -OutFile $archive
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'F780898E402FF80612CC8D9FCB8C6E02932BD1CB4C900FFDAA31F9341CFB49F4') { throw 'Inno compiler package integrity check failed' }
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $repoRoot '.tools\inno') -Force
    }
    & $compiler (Join-Path $repoRoot 'installer\AwayLock.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
}
$releaseFiles = Get-ChildItem -LiteralPath $artifacts -File -Filter 'AwayLock-Setup-*.exe'
$checksums = @($releaseFiles | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name })
if ($checksums.Count -gt 0) { $checksums | Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii }
Write-Output "Build outputs are in $artifacts"

param(
    [string]$OutputDirectory,
    [string]$NuGetSource,
    [string]$InnoCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRoot
$taskVersion = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
$taskRuntime = '10.0.13'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRoot "artifacts\releases\$taskVersion" }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskOutput) { throw "Escolha uma pasta nova para preservar os artefatos: $taskOutput" }
if (!(Test-Path -LiteralPath $InnoCompiler)) { throw 'Instale o compilador Inno Setup 6 ou informe -InnoCompiler.' }
$taskRestore = @('restore','src\NexoVideo.App\NexoVideo.App.csproj','-p:RuntimeIdentifier=win-x64','-p:SelfContained=true',"-p:RuntimeFrameworkVersion=$taskRuntime",'--locked-mode')
if ($NuGetSource) { $taskRestore += @('--source',$NuGetSource,'-p:NuGetAudit=false') }
& dotnet @taskRestore
if ($LASTEXITCODE -ne 0) { throw 'Falha no restore do pacote.' }
$taskPublish = Join-Path $taskOutput "NexoVideo-$taskVersion-win-x64"
& dotnet publish src\NexoVideo.App\NexoVideo.App.csproj -c Release -r win-x64 --self-contained true --no-restore "-p:RuntimeFrameworkVersion=$taskRuntime" -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $taskPublish
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação self-contained.' }
foreach ($taskName in @('coreclr.dll','hostfxr.dll','PresentationFramework.dll','e_sqlite3.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $taskPublish $taskName))) { throw "Dependência ausente: $taskName" }
}
Copy-Item -LiteralPath 'docs\USAR_RELEASE.md' -Destination (Join-Path $taskPublish 'LEIA-ME.md')
Copy-Item -LiteralPath 'docs\COLETA_EVIDENCIAS.md' -Destination $taskPublish
Copy-Item -LiteralPath 'distribution\AVISOS-TERCEIROS.txt' -Destination $taskPublish
$taskLicenses = Join-Path $taskPublish 'licenses'
New-Item -ItemType Directory -Path $taskLicenses | Out-Null
Copy-Item -Path 'distribution\licenses\*' -Destination $taskLicenses
Copy-Item -LiteralPath ".packages\microsoft.netcore.app.runtime.win-x64\$taskRuntime\LICENSE.TXT" -Destination (Join-Path $taskLicenses 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath ".packages\microsoft.netcore.app.runtime.win-x64\$taskRuntime\THIRD-PARTY-NOTICES.TXT" -Destination (Join-Path $taskLicenses 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath ".packages\microsoft.windowsdesktop.app.runtime.win-x64\$taskRuntime\LICENSE" -Destination (Join-Path $taskLicenses 'windowsdesktop-LICENSE.txt')
Copy-Item -LiteralPath '.packages\sqlite\3.53.4\LICENSE.txt' -Destination (Join-Path $taskLicenses 'sqlite-LICENSE.txt')
Copy-Item -LiteralPath 'src\NexoVideo.App\Assets\Fonts\OFL.txt' -Destination (Join-Path $taskLicenses 'Inter-OFL.txt')
$taskCommit = (& git rev-parse HEAD).Trim()
$taskFiles = @(Get-ChildItem -LiteralPath $taskPublish -Recurse -File | ForEach-Object {
    @{ path = $_.FullName.Substring($taskPublish.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $_.Length }
})
$taskManifest = @{version=$taskVersion; rid='win-x64'; runtime=$taskRuntime; commit=$taskCommit; generated_utc=[DateTime]::UtcNow.ToString('O'); signed=$false; files=$taskFiles}
[IO.File]::WriteAllText((Join-Path $taskPublish 'build-manifest.json'),($taskManifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$taskZip = Join-Path $taskOutput "NexoVideo-$taskVersion-win-x64-portable.zip"
Compress-Archive -LiteralPath $taskPublish -DestinationPath $taskZip -CompressionLevel Optimal
& $InnoCompiler /Qp "/DAppVersion=$taskVersion" "/DPublishDir=$taskPublish" "/DReleaseDir=$taskOutput" installer\NexoVideo.iss
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação do instalador.' }
$taskSums = Get-ChildItem -LiteralPath $taskOutput -File | Where-Object { $_.Extension -in @('.exe','.zip') } | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $taskOutput 'SHA256SUMS.txt'),[string[]]$taskSums,[Text.UTF8Encoding]::new($false))
Write-Output "Pacotes criados em $taskOutput"

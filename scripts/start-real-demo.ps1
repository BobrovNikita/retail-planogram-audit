param(
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [ValidateRange(1024,65535)][int]$Port = 5180,
    [string]$AdminEmail
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataRoot = (Resolve-Path -LiteralPath $DataDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $dataRoot 'setup.json'))) { throw 'Run RetailVision.DemoSetup first.' }
$names = @('Storage__DataDirectory','Vision__ManifestPath','Vision__ModelNotice','ASPNETCORE_ENVIRONMENT','Admin__Email','Admin__Password','Admin__PasswordFile')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:Storage__DataDirectory = $dataRoot
    $env:Vision__ManifestPath = Join-Path $dataRoot 'model/manifest.json'
    $env:Vision__ModelNotice = 'Experimental HoloSelecta: 8 SKUs. Known benchmark only; new independent photos and manual review required.'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Admin__Email = $null
    $env:Admin__Password = $null
    $env:Admin__PasswordFile = $null
    if ($AdminEmail) {
        $secret = Read-Host 'New administrator password (12+ characters, upper/lowercase, digit and symbol)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        try { $env:Admin__Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secret.Dispose() }
        $env:Admin__Email = $AdminEmail
    }
    Push-Location $repoRoot
    try {
        dotnet run --project src/RetailVision.Web -c Release --no-build --no-launch-profile --urls "http://localhost:$Port"
        if ($LASTEXITCODE -ne 0) { throw "Application exited with code $LASTEXITCODE" }
    } finally { Pop-Location }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}

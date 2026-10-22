[CmdletBinding()]
param(
    [switch]$ConfigureConnection,
    [switch]$ConfigureDemoAccounts,
    [switch]$Migrate,
    [switch]$SeedData,
    [switch]$SeedAccounts,
    [switch]$Run,
    [string]$Url = "http://127.0.0.1:5047"
)

$ErrorActionPreference = 'Stop'

function ConvertFrom-SecureInput {
    param([Parameter(Mandatory)][Security.SecureString]$Value)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE. No database fallback is attempted."
    }
}

function Invoke-DevelopmentPolicyCheck {
    Write-Output 'Validating the application database policy only; this check does not connect to SQL Server.'
    $checkArguments = @('run', '--project', $webProject, '--no-launch-profile', '--', '--check-development-database')
    Invoke-DotNet -Arguments $checkArguments
}

function Get-UserSecretsState {
    param([Parameter(Mandatory)][string]$SecretsFile)

    $values = @{}
    if (Test-Path -LiteralPath $SecretsFile) {
        try {
            $document = Get-Content -LiteralPath $SecretsFile -Raw | ConvertFrom-Json
            foreach ($property in $document.PSObject.Properties) {
                $values[$property.Name] = [string]$property.Value
            }
        }
        catch {
            throw "The local User Secrets file could not be read. It was left unchanged."
        }
    }

    return ,$values
}

function Save-UserSecretsState {
    param(
        [Parameter(Mandatory)][string]$SecretsFile,
        [Parameter(Mandatory)][hashtable]$Values
    )

    $directory = Split-Path -Parent $SecretsFile
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $json = ConvertTo-Json -InputObject $Values -Depth 8
    Set-Content -LiteralPath $SecretsFile -Value $json -Encoding UTF8
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repositoryRoot 'NEXORA.sln'
$webProject = Join-Path $repositoryRoot 'src\NEXORA.Web\NEXORA.Web.csproj'
$infrastructureProject = Join-Path $repositoryRoot 'src\NEXORA.Infrastructure\NEXORA.Infrastructure.csproj'

if (-not (Test-Path -LiteralPath $solution) -or -not (Test-Path -LiteralPath $webProject)) {
    throw "NEXORA solution files were not found at the expected repository paths."
}

$sdk = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdk | Where-Object { $_ -match '^10\.' })) {
    throw ".NET 10 SDK is required. Install the official .NET 10 SDK, then retry."
}

[xml]$projectXml = Get-Content -LiteralPath $webProject -Raw
$userSecretsNode = $projectXml.SelectSingleNode('//UserSecretsId')
$userSecretsId = if ($null -ne $userSecretsNode) { $userSecretsNode.InnerText } else { $null }
if ([string]::IsNullOrWhiteSpace($userSecretsId)) {
    throw "The Web project has no UserSecretsId; local secrets were not changed."
}

$secretsFile = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json"
$configureMode = $ConfigureConnection -or $ConfigureDemoAccounts
$operationMode = $Migrate -or $SeedData -or $SeedAccounts -or $Run
if (($ConfigureConnection -and $ConfigureDemoAccounts) -or ($configureMode -and $operationMode)) {
    throw "Configure secrets in a separate run from migration, seeding, or website startup."
}

Push-Location $repositoryRoot
$oldAspNetEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldDotNetEnvironment = $env:DOTNET_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'

    if ($ConfigureConnection) {
        $secureConnection = Read-Host 'Local SQL connection string (input hidden)' -AsSecureString
        $connectionString = ConvertFrom-SecureInput $secureConnection
        if ([string]::IsNullOrWhiteSpace($connectionString)) {
            throw "A connection string is required. No local secrets were changed."
        }

        $oldDatabaseProfile = $env:Database__Profile
        $oldDefaultConnection = $env:ConnectionStrings__DefaultConnection
        try {
            $env:Database__Profile = 'PersonalDevelopment'
            $env:ConnectionStrings__DefaultConnection = $connectionString
            Invoke-DevelopmentPolicyCheck
        }
        finally {
            $env:Database__Profile = $oldDatabaseProfile
            $env:ConnectionStrings__DefaultConnection = $oldDefaultConnection
        }

        $secrets = Get-UserSecretsState $secretsFile
        $secrets['Database:Profile'] = 'PersonalDevelopment'
        $secrets['ConnectionStrings:DefaultConnection'] = $connectionString
        Save-UserSecretsState $secretsFile $secrets
        $connectionString = $null
        Write-Output "Saved the local PersonalDevelopment profile to this Windows user's ASP.NET Core User Secrets store."
    }
    elseif ($ConfigureDemoAccounts) {
        Invoke-DevelopmentPolicyCheck
        $secrets = Get-UserSecretsState $secretsFile
        foreach ($role in @('Customer', 'Sales', 'Warehouse', 'Admin')) {
            Write-Output "Configure local demo account for $role. Use a non-routable @nexora.test email."
            $email = (Read-Host 'Email').Trim()
            $fullName = (Read-Host 'Display name').Trim()
            $securePassword = Read-Host 'Password (input hidden)' -AsSecureString
            $password = ConvertFrom-SecureInput $securePassword
            if ([string]::IsNullOrWhiteSpace($email) -or [string]::IsNullOrWhiteSpace($fullName) -or [string]::IsNullOrEmpty($password)) {
                throw "All four demo accounts need an email, display name, and password. Existing secrets were not written."
            }

            $secrets["DevelopmentAccounts:${role}:Email"] = $email
            $secrets["DevelopmentAccounts:${role}:FullName"] = $fullName
            $secrets["DevelopmentAccounts:${role}:Password"] = $password
            $password = $null
        }

        Save-UserSecretsState $secretsFile $secrets
        Write-Output 'Saved four local demo account definitions to this Windows user profile. Passwords were not displayed.'
    }
    else {
        Invoke-DevelopmentPolicyCheck
    }

    if ($Migrate) {
        Write-Output 'Applying EF migrations only to the validated member-owned local database.'
        $migrationArguments = @(
            'ef', 'database', 'update',
            '--project', $infrastructureProject,
            '--startup-project', $webProject,
            '--context', 'AppDbContext'
        )
        Invoke-DotNet -Arguments $migrationArguments
    }

    if ($SeedData) {
        $seedDataArguments = @('run', '--project', $webProject, '--no-launch-profile', '--', '--seed-development-data')
        Invoke-DotNet -Arguments $seedDataArguments
    }

    if ($SeedAccounts) {
        $seedAccountArguments = @('run', '--project', $webProject, '--no-launch-profile', '--', '--seed-development-accounts')
        Invoke-DotNet -Arguments $seedAccountArguments
    }

    if ($Run) {
        Write-Output "Starting NEXORA on $Url. Press Ctrl+C in this terminal to stop it."
        $runArguments = @('run', '--project', $webProject, '--no-launch-profile', '--', '--urls', $Url)
        Invoke-DotNet -Arguments $runArguments
    }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $oldAspNetEnvironment
    $env:DOTNET_ENVIRONMENT = $oldDotNetEnvironment
    Pop-Location
}

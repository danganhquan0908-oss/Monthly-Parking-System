$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot '..\MonthlyParkingSystem.Api\MonthlyParkingSystem.Api.csproj'
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "MPS API project was not found at $projectPath"
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$userSecretsId = $project.Project.PropertyGroup.UserSecretsId | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($userSecretsId)) {
    dotnet user-secrets init --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize .NET User Secrets.' }
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $userSecretsId = $project.Project.PropertyGroup.UserSecretsId | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($userSecretsId)) { throw 'UserSecretsId is missing from the API project.' }

$senderAddress = (Read-Host 'Gmail sender address').Trim()
try {
    $null = [System.Net.Mail.MailAddress]::new($senderAddress)
} catch {
    throw 'Enter a valid sender email address.'
}

$secureAppPassword = Read-Host 'Google app password (input is hidden)' -AsSecureString
$passwordPointer = [IntPtr]::Zero
try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureAppPassword)
    $appPassword = ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer) -replace '\s', '')
    if ([string]::IsNullOrWhiteSpace($appPassword)) { throw 'The app password cannot be empty.' }

    $secretsDirectory = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId"
    $secretsPath = Join-Path $secretsDirectory 'secrets.json'
    [System.IO.Directory]::CreateDirectory($secretsDirectory) | Out-Null

    $settings = @{}
    if (Test-Path -LiteralPath $secretsPath) {
        $existing = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json -AsHashtable
        if ($existing -is [System.Collections.IDictionary]) { $settings = $existing }
    }
    if (-not $settings.Contains('Email') -or $settings['Email'] -isnot [System.Collections.IDictionary]) {
        $settings['Email'] = @{}
    }

    $settings['Email']['SmtpHost'] = 'smtp.gmail.com'
    $settings['Email']['SmtpPort'] = 587
    $settings['Email']['SmtpUsername'] = $senderAddress
    $settings['Email']['SmtpPassword'] = $appPassword
    $settings['Email']['FromAddress'] = $senderAddress
    $settings['Email']['FromName'] = 'MPS'

    $json = ConvertTo-Json -InputObject $settings -Depth 20
    [System.IO.File]::WriteAllText($secretsPath, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host 'SMTP settings saved to local .NET User Secrets. The app password was not printed or added to the repository.' -ForegroundColor Green
    Write-Host 'Restart the API in Development to load the new settings.'
} finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    if ($null -ne $secureAppPassword) { $secureAppPassword.Dispose() }
    $appPassword = $null
}

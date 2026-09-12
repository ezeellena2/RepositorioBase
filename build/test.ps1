param (
    [string[]]$ClientFramework = @("angular", "react", "none")
)

$outputPath = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\template-tests"
$results = @()

function AssertLocalizationTemplateShape {
    param (
        [string]$clientFramework,
        [string]$projectPath
    )

    if ($clientFramework -eq "none") {
        return
    }

    $expectsLocalizationJourney = $clientFramework -eq "react"
    $reactOnlyPaths = @(
        "tests/Web.AcceptanceTests/Features/Localization.feature",
        "tests/Web.AcceptanceTests/Pages/LocalizationPage.cs",
        "tests/Web.AcceptanceTests/StepDefinitions/LocalizationStepDefinitions.cs"
    )

    foreach ($relativePath in $reactOnlyPaths) {
        $exists = Test-Path (Join-Path $projectPath $relativePath)
        if ($exists -ne $expectsLocalizationJourney) {
            throw "Unexpected React localization asset shape for $($clientFramework): $relativePath"
        }
    }

    $acceptanceProject = Get-Content (Join-Path $projectPath "tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj") -Raw
    $centralPackages = Get-Content (Join-Path $projectPath "Directory.Packages.props") -Raw
    $hasExternalDataReference = $acceptanceProject.Contains('<PackageReference Include="Reqnroll.ExternalData"')
    $hasExternalDataVersion = $centralPackages.Contains('<PackageVersion Include="Reqnroll.ExternalData"')
    if ($hasExternalDataReference -ne $expectsLocalizationJourney -or $hasExternalDataVersion -ne $expectsLocalizationJourney) {
        throw "Unexpected Reqnroll.ExternalData shape for $clientFramework"
    }
}

function CreateAndTestProject {
    param (
        [string]$clientFramework,
        [string]$database
    )

    $name = "$clientFramework-$database"
    $projectPath = Join-Path $outputPath $name

    try {
        if (Test-Path $projectPath) {
            Write-Host "Removing existing directory: $name"
            Remove-Item -Recurse -Force $projectPath
        }

        Write-Host "Creating project: $name"
        $startTime = Get-Date

        dotnet new ca-sln --client-framework $clientFramework --database $database --name CleanArchitecture --output $projectPath --no-update-check
        if ($LASTEXITCODE -ne 0) { throw "dotnet new ca-sln failed for $name" }
        AssertLocalizationTemplateShape -clientFramework $clientFramework -projectPath $projectPath

        $exitCode = 0
        Push-Location $projectPath
        try {
            Write-Host "Generating command and query item templates: $name"
            Push-Location "./src/Application"
            try {
                dotnet new ca-usecase --name GeneratedCommand --feature-name TemplateGate --usecase-type command --no-update-check
                if ($LASTEXITCODE -ne 0) { throw "dotnet new ca-usecase command failed for $name" }
                dotnet new ca-usecase --name GeneratedQuery --feature-name TemplateGate --usecase-type query --no-update-check
                if ($LASTEXITCODE -ne 0) { throw "dotnet new ca-usecase query failed for $name" }

                $generatedCommand = Get-Content "TemplateGate/Commands/GeneratedCommand/GeneratedCommand.cs" -Raw
                $generatedQuery = Get-Content "TemplateGate/Queries/GeneratedQuery/GeneratedQuery.cs" -Raw
                if (-not $generatedCommand.Contains(".WithErrorCode(ValidationErrorCodes.Required);")) {
                    throw "Generated command does not carry the approved validation error code syntax for $name"
                }
                if (-not $generatedQuery.Contains(".WithErrorCode(ValidationErrorCodes.Required);")) {
                    throw "Generated query does not carry the approved validation error code syntax for $name"
                }
            } finally {
                Pop-Location
            }

            Write-Host "Building: $name"
            dotnet build --configuration Release
            if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }

            if ($clientFramework -ne "none") {
                Write-Host "Building client app: $name"
                Push-Location "./src/Web/ClientApp"
                try {
                    npm ci
                    if ($LASTEXITCODE -ne 0) { throw "npm ci failed for $name" }
                    npm run build
                    if ($LASTEXITCODE -ne 0) { throw "npm build failed for $name" }
                } finally {
                    Pop-Location
                }
            }

            if ($clientFramework -ne "none") {
                Write-Host "Installing Playwright browsers: $name"
                pwsh artifacts/bin/Web.AcceptanceTests/release/playwright.ps1 install --with-deps chromium
                if ($LASTEXITCODE -ne 0) { throw "Playwright install failed for $name" }
            }

            Write-Host "Testing: $name"
            dotnet test --no-build --configuration Release
            if ($LASTEXITCODE -ne 0) { $exitCode = $LASTEXITCODE }
        } finally {
            Pop-Location
        }

        $endTime = Get-Date
        $duration = $endTime - $startTime

        $script:results += [PSCustomObject]@{
            ClientFramework = $clientFramework
            Database        = $database
            ExitCode        = $exitCode
            Status          = if ($exitCode -eq 0) { "Success" } else { "Failure" }
            Duration        = $duration.ToString("c")
        }
    } catch {
        Write-Host "An error occurred while processing: $name"
        Write-Host $_.Exception.Message
        $script:results += [PSCustomObject]@{
            ClientFramework = $clientFramework
            Database        = $database
            ExitCode        = -1
            Status          = "Error"
            Duration        = "00:00:00.0000000"
        }
    }
}

if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath | Out-Null
}

foreach ($cf in $ClientFramework) {
    CreateAndTestProject -clientFramework $cf -database "postgresql"
}

$results | Format-Table -Property ClientFramework, Database, Status, Duration -AutoSize

if ($results | Where-Object { $_.Status -ne "Success" }) {
    exit 1
}

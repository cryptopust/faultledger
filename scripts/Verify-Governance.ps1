[CmdletBinding()]
param(
    [switch]$BootstrapOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Property {
    param(
        [xml]$Document,
        [string]$Name,
        [string]$Expected
    )

    $nodes = @($Document.SelectNodes("/Project/PropertyGroup/$Name"))
    Assert-Condition ($nodes.Count -eq 1) "Expected exactly one property: $Name."
    $node = $nodes[0]
    Assert-Condition ($node.InnerText.Trim() -ceq $Expected) "Unexpected value for $Name; required: $Expected."
    Assert-Condition (-not $node.HasAttribute('Condition')) "Baseline property must be unconditional: $Name."
    Assert-Condition (-not $node.ParentNode.HasAttribute('Condition')) "Baseline property group must be unconditional: $Name."
}

function Assert-GitIgnore {
    param(
        [string]$Path,
        [bool]$ExpectedIgnored
    )

    & git -C $repositoryRoot check-ignore --no-index -q -- $Path
    $exitCode = $LASTEXITCODE
    Assert-Condition ($exitCode -in @(0, 1)) "git check-ignore failed for $Path with exit code $exitCode."
    Assert-Condition (($exitCode -eq 0) -eq $ExpectedIgnored) "Incorrect Git ignore behavior for $Path."
}

function Assert-StableRuleSeverities {
    param(
        [hashtable]$Severities
    )

    $nonNegotiableNumbers = @(1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 14, 15, 16, 19)
    foreach ($number in 1..21) {
        $ruleId = 'FL-RULE-{0:000}' -f $number
        $expectedSeverity = if ($number -in $nonNegotiableNumbers) { 'NON-NEGOTIABLE' } else { 'STRICT' }
        Assert-Condition ($Severities.ContainsKey($ruleId)) "Missing stable rule: $ruleId."
        Assert-Condition ($Severities[$ruleId] -ceq $expectedSeverity) "Unauthorized baseline severity change for $ruleId."
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$null = Get-Command git -ErrorAction Stop
$gitRoot = (& git -C $repositoryRoot rev-parse --show-toplevel | Out-String).Trim()
Assert-Condition ($LASTEXITCODE -eq 0) 'Run this verifier from a Git working tree.'
Assert-Condition ([System.IO.Path]::GetFullPath($gitRoot) -eq $repositoryRoot) 'The governance directory must be the Git repository root.'

$requiredFiles = @(
    'AGENTS.md'
    'CONTRIBUTING.md'
    'SECURITY.md'
    '.editorconfig'
    'Directory.Build.props'
    'Directory.Packages.props'
    'global.json'
    '.gitignore'
    '.gitattributes'
    '.dockerignore'
    '.env.example'
    'docs/engineering/CONSTITUTION.md'
    'docs/engineering/ARCHITECTURE_RULES.md'
    'docs/engineering/TESTING_RULES.md'
    'docs/engineering/DATABASE_RULES.md'
    'docs/engineering/FAILURE_MODEL.md'
    'docs/engineering/CODE_REVIEW_RULES.md'
    'docs/engineering/COMMIT_RULES.md'
    'docs/engineering/DEFINITION_OF_DONE.md'
    'docs/engineering/RULE_INDEX.md'
    'docs/engineering/AGENT_COMPLIANCE_CHECKLIST.md'
    'scripts/Verify-Governance.ps1'
)

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$contents = @{}
foreach ($relativePath in $requiredFiles) {
    $absolutePath = Join-Path $repositoryRoot $relativePath
    Assert-Condition (Test-Path -LiteralPath $absolutePath -PathType Leaf) "Missing required file: $relativePath."
    $bytes = [System.IO.File]::ReadAllBytes($absolutePath)
    Assert-Condition ($bytes.Length -gt 0) "Empty required file: $relativePath."
    Assert-Condition (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) "UTF-8 BOM is not allowed: $relativePath."
    $text = $utf8.GetString($bytes)
    Assert-Condition (-not $text.Contains("`r")) "Expected LF newlines: $relativePath."
    Assert-Condition ($text.EndsWith("`n")) "Missing final newline: $relativePath."
    Assert-Condition (-not [regex]::IsMatch($text, '(?m)[\t ]+$')) "Trailing whitespace: $relativePath."
    $contents[$relativePath] = $text
}

$linkCount = 0
$rootPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($relativePath in $requiredFiles | Where-Object { $_.EndsWith('.md') }) {
    $parent = Split-Path -Parent (Join-Path $repositoryRoot $relativePath)
    foreach ($match in [regex]::Matches($contents[$relativePath], '\[[^\]\r\n]+\]\(([^)\r\n]+)\)')) {
        $target = $match.Groups[1].Value
        if ($target -match '^(https?://|mailto:|#)') {
            continue
        }

        $target = [System.Uri]::UnescapeDataString(($target -split '#', 2)[0])
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $parent $target))
        Assert-Condition ($resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) "Link escapes repository in ${relativePath}: $target."
        Assert-Condition (Test-Path -LiteralPath $resolved -PathType Leaf) "Broken link in ${relativePath}: $target."
        $linkCount++
    }
}

Assert-Condition ($contents['AGENTS.md'].StartsWith('Any coding agent modifying this repository MUST read this entire file before making changes.')) 'AGENTS.md must begin with the mandatory read instruction.'
$definitions = @([regex]::Matches($contents['AGENTS.md'], '(?m)^### (FL-RULE-\d{3}) - .+$') | ForEach-Object { $_.Groups[1].Value })
Assert-Condition ($definitions.Count -eq @($definitions | Select-Object -Unique).Count) 'Duplicate rule definition in AGENTS.md.'

$indexHeader = '| Rule ID | Short rule | Severity | Where defined | How later work can prove compliance |'
Assert-Condition ($contents['docs/engineering/RULE_INDEX.md'].Contains($indexHeader)) 'Rule index schema does not match the documented table format.'
$ruleSeverities = @{}
foreach ($line in ($contents['docs/engineering/RULE_INDEX.md'] -split "`n" | Where-Object { $_ -match '^\| FL-RULE-' })) {
    $cells = @($line.Trim().Trim([char]'|').Split([char]'|') | ForEach-Object { $_.Trim() })
    Assert-Condition ($cells.Count -eq 5) "Rule index row must have five cells: $line."
    $ruleId = $cells[0]
    Assert-Condition ($ruleId -cmatch '^FL-RULE-\d{3}$') "Invalid rule ID: $ruleId."
    Assert-Condition (-not $ruleSeverities.ContainsKey($ruleId)) "Duplicate rule index row: $ruleId."
    Assert-Condition ($cells[2] -cin @('NON-NEGOTIABLE', 'STRICT', 'GUIDELINE')) "Invalid severity for $ruleId."
    Assert-Condition ($cells[1].Length -gt 0 -and $cells[3].Length -gt 0 -and $cells[4].Length -gt 0) "Incomplete rule index row: $ruleId."
    Assert-Condition ($cells[3] -match '\]\(\.\./\.\./AGENTS\.md\)') "Missing authoritative source link for $ruleId."
    $ruleSeverities[$ruleId] = $cells[2]
}

Assert-Condition ($ruleSeverities.Count -eq $definitions.Count) 'Rule definition/index counts do not match.'
foreach ($ruleId in $definitions) {
    Assert-Condition ($ruleSeverities.ContainsKey($ruleId)) "Unindexed rule: $ruleId."
}

Assert-StableRuleSeverities $ruleSeverities

foreach ($relativePath in $requiredFiles | Where-Object { $_.EndsWith('.md') }) {
    foreach ($match in [regex]::Matches($contents[$relativePath], 'FL-RULE-\d{3}')) {
        Assert-Condition ($ruleSeverities.ContainsKey($match.Value)) "Unknown rule reference in ${relativePath}: $($match.Value)."
    }
}

[xml]$build = $contents['Directory.Build.props']
$buildDefaults = [ordered]@{
    TargetFramework = 'net10.0'
    Nullable = 'enable'
    ImplicitUsings = 'enable'
    TreatWarningsAsErrors = 'true'
    EnableNETAnalyzers = 'true'
    AnalysisLevel = 'latest'
    EnforceCodeStyleInBuild = 'true'
    Deterministic = 'true'
}
foreach ($name in $buildDefaults.Keys) {
    Assert-Property $build $name $buildDefaults[$name]
}
Assert-Condition ($build.SelectNodes('//NoWarn | //WarningsNotAsErrors').Count -eq 0) 'Global warning suppression is prohibited.'

[xml]$packages = $contents['Directory.Packages.props']
Assert-Property $packages 'ManagePackageVersionsCentrally' 'true'
Assert-Property $packages 'CentralPackageVersionOverrideEnabled' 'false'

$sdk = ($contents['global.json'] | ConvertFrom-Json).sdk
Assert-Condition ($sdk.version -match '^10\.0\.\d+$') 'global.json must select a stable .NET 10 SDK.'
Assert-Condition ($sdk.rollForward -ceq 'disable') 'SDK roll-forward must not silently change the pinned toolchain.'
Assert-Condition ($sdk.allowPrerelease -eq $false) 'Prerelease SDK selection must be disabled.'

foreach ($setting in @('root = true', 'charset = utf-8', 'end_of_line = lf', 'insert_final_newline = true', 'indent_style = space', 'indent_size = 4', 'csharp_style_namespace_declarations = file_scoped:warning')) {
    Assert-Condition ([regex]::IsMatch($contents['.editorconfig'], '(?m)^' + [regex]::Escape($setting) + '$')) "Missing shared style setting: $setting."
}
Assert-Condition ($contents['.gitattributes'].Contains('* text=auto eol=lf')) 'Shared Git text/LF policy is missing.'

foreach ($path in @('.idea/workspace.xml', 'nested/.idea/encodings.xml', 'bin/check.dll', 'nested/obj/project.assets.json', 'TestResults/check.trx', 'coverage/report.xml', 'developer.user', 'developer.suo', 'sample.sln.iml', 'workspace.xml', '.env', '.env.local', 'nested/.env.local', 'trace.log', 'nested/private.key')) {
    Assert-GitIgnore $path $true
}
foreach ($path in @('.env.example', 'nested/.env.example', 'src/FaultLedger.Infrastructure/Migrations/20300101000000_Initial.cs', 'packages.lock.json', 'Directory.Packages.props')) {
    Assert-GitIgnore $path $false
}

$trackedFiles = @(& git -c core.quotepath=false -C $repositoryRoot ls-files --cached)
Assert-Condition ($LASTEXITCODE -eq 0) 'Unable to inspect tracked files.'
foreach ($path in $trackedFiles) {
    $forbidden = $path -match '(^|/)(\.idea|\.vs|bin|obj|TestResults|coverage)(/|$)|(^|/)workspace\.xml$|\.(user|suo|sln\.iml|pfx|p12|pem|key|log)$'
    $localEnvironment = $path -match '(^|/)\.env($|\.)' -and $path -notmatch '(^|/)\.env\.example$'
    Assert-Condition (-not ($forbidden -or $localEnvironment)) "Forbidden tracked local/secret artifact: $path."
}

$dockerPatterns = @($contents['.dockerignore'] -split "`n" | ForEach-Object { $_.Trim() })
foreach ($pattern in @('.git', '**/.git', '.idea', '**/.idea', 'bin', '**/bin', 'obj', '**/obj', 'TestResults', '**/TestResults', 'coverage', '**/coverage', '.env', '.env.*', '**/.env', '**/.env.*', 'logs', '**/logs', '**/*.key', '**/*.pfx')) {
    Assert-Condition ($pattern -cin $dockerPatterns) "Missing Docker-context exclusion: $pattern."
}
Assert-Condition (-not [regex]::IsMatch($contents['.env.example'], '(?im)^POSTGRES_PASSWORD=(?!CHANGE_ME_LOCAL_ONLY$).+')) 'PostgreSQL example password must be an obvious placeholder.'
Assert-Condition ($contents['.env.example'] -match '(?i)example only' -and $contents['.env.example'] -match '(?i)do not use production secrets') 'Example-only security notice is missing.'

if ($BootstrapOnly) {
    foreach ($directory in @('src', 'tests')) {
        Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $directory))) "Bootstrap must not create $directory/."
    }

    $candidateFiles = @(& git -c core.quotepath=false -C $repositoryRoot ls-files --cached --others --exclude-standard)
    Assert-Condition ($LASTEXITCODE -eq 0) 'Unable to inspect bootstrap file inventory.'
    foreach ($path in $candidateFiles) {
        $applicationFile = $path -match '\.(sln|slnx|csproj|fsproj|vbproj|cs|fs|vb|sql)$|(^|/)(src|tests|migrations)/'
        $containerFile = $path -match '(^|/)(Dockerfile[^/]*|(docker-)?compose[^/]*\.ya?ml)$'
        Assert-Condition (-not ($applicationFile -or $containerFile)) "Application/service scaffolding is not permitted during bootstrap: $path."
    }

    foreach ($document in @($build, $packages)) {
        Assert-Condition ($document.SelectNodes('//PackageVersion | //PackageReference | //GlobalPackageReference').Count -eq 0) 'No package declarations are permitted during bootstrap.'
    }

    foreach ($example in @('POSTGRES_HOST=localhost', 'POSTGRES_PORT=5432', 'POSTGRES_DB=faultledger', 'POSTGRES_USER=faultledger_dev', 'POSTGRES_PASSWORD=CHANGE_ME_LOCAL_ONLY')) {
        Assert-Condition ([regex]::IsMatch($contents['.env.example'], '(?m)^' + [regex]::Escape($example) + '$')) "Missing bootstrap example setting: $example."
    }
}

Write-Output "PASS: $($requiredFiles.Count) governance files; UTF-8/LF/whitespace; $linkCount local links; $($ruleSeverities.Count) indexed rules."
Write-Output 'PASS: strict build/package defaults, SDK pin, shared style, selected Git/Docker hygiene, and example settings.'
if ($BootstrapOnly) {
    Write-Output 'PASS: governance-only scope and zero package declarations.'
}
Write-Output 'LIMIT: structural checks do not prove application behavior, full secret absence, or semantic policy compliance.'

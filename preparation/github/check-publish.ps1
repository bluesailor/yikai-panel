param(
    [string]$Root = '',
    [switch]$ListOnly
)
# Pre-publication leak check. Only Git-visible files in the public repository
# scope are scanned. Runtime data, QA fixtures, databases and build outputs
# must remain excluded.
$ErrorActionPreference = 'Stop'
$problems = @()
$scanned = 0

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $Root = (Resolve-Path $Root).Path
}

$gitRoot = (& git -C $Root rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
    throw "Not a Git repository: $Root"
}
$gitRoot = [IO.Path]::GetFullPath($gitRoot.Trim())
if (-not $gitRoot.Equals([IO.Path]::GetFullPath($Root), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Root must be the repository root: $gitRoot"
}

function Is-Publishable([string]$rel) {
    $parts = $rel.Split('\')
    $top = $parts[0]
    if ($top -eq 'preparation') {
        if ($rel -match '\\(bin|obj)\\') { return $false }
        if ($rel -match '^preparation\\(before-|panel-|brand-|qa-sites|projects-qa-)[^\\]*\\' -or $parts[1] -match '-qa-') { return $false }
        if ($rel -match '^preparation\\ui-[^\\]+\\' -and $rel -match '\.(cs|csproj)$') { return $true }
        if ($parts[1] -eq 'github') { return $true }
        if ($rel -match '^preparation\\release-0\.7\.0\\installer\\') { return $true }
        if ($rel -match '^preparation\\[^\\]+\\tests\\') { return $true }
        if ($parts.Count -eq 2 -and $parts[1] -match '\.(ps1|md|json|py)$') { return $true }
        if ($rel -match '^preparation\\[^\\]+\\checks\\') { return $true }
        if ($rel -match '^preparation\\[^\\]+\\[^\\]+\.(ps1|md|json|py)$') { return $true }
        return $false
    }
    if ($top -eq 'website' -and $rel -match '\\dist\\') { return $false }
    if ($rel -match '\\(bin|obj)\\') { return $false }
    return $true
}

$allowedTop = @(
    '.github', 'src', 'docs', 'design', 'preparation', 'website', 'images',
    'README.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md',
    'LICENSE', 'NOTICE', 'TRADEMARKS.md', 'THIRD-PARTY-NOTICES.md',
    '.gitignore', '.gitattributes'
)

$candidatePaths = @(& git -C $Root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the Git file list.' }

$outside = @($candidatePaths | Where-Object {
    $rel = $_.Replace('/', '\')
    $allowedTop -notcontains $rel.Split('\')[0]
})
if ($outside.Count -gt 0) {
    $problems += "Git files outside the public scope: $($outside -join ', ')"
}

$files = @($candidatePaths | Sort-Object -Unique | ForEach-Object {
    $rel = $_.Replace('/', '\')
    if ($allowedTop -notcontains $rel.Split('\')[0]) { return }
    if (-not (Is-Publishable $rel)) { return }
    $full = Join-Path $Root $rel
    if (Test-Path -LiteralPath $full -PathType Leaf) { Get-Item -LiteralPath $full }
})

if ($ListOnly) {
    $files | ForEach-Object { $_.FullName.Substring($Root.Length + 1) }
    exit 0
}

Write-Host "Scanning: $Root (tracked and non-ignored untracked files)"
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length + 1)
    $scanned++

    if ($file.Extension -in @('.key', '.pem', '.crt', '.pfx', '.p12')) {
        $problems += "Private key or certificate: $rel"
        continue
    }
    $head = ''
    try { $head = Get-Content $file.FullName -TotalCount 20 -Encoding UTF8 -ErrorAction SilentlyContinue | Out-String } catch { }
    if ($head -match 'BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY') { $problems += "Private key content: $rel" }

    if ($file.Name -eq 'panel.json') { $problems += "Runtime panel configuration: $rel" }
    if ($rel -like '*soft\packages\yikaicms*') { $problems += "YikaiCMS source: $rel" }
    if ($file.Extension -in @('.sqlite', '.sqlite3', '.db')) { $problems += "Database file: $rel" }

    $text = ''
    try { if ($file.Length -lt 2MB) { $text = Get-Content $file.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue } } catch { }

    if ($file.Extension -in @('.cs', '.php', '.ps1', '.json', '.md', '.txt', '.csproj', '.py', '.yml', '.yaml')) {
        $found = [regex]::Matches($text, '(?<![\\A-Za-z0-9])[A-Za-z]:\\[^\s"''<>|\\]+\\') | ForEach-Object { $_.Value.Replace('\\','\') } |
            Where-Object { $_ -notlike 'D:\yikai*' -and $_ -notlike 'D:\phpstudy_pro*' -and $_ -notlike 'C:\Windows*' } | Select-Object -Unique
        if ($found) { $problems += ("Local absolute path: {0} -> {1}" -f $rel, ($found -join ', ')) }
    }

    if ($file.Extension -in @('.cs', '.php', '.json', '.ps1', '.yml', '.yaml', '.xml', '.config')) {
        foreach ($pattern in @(('BEGIN ' + 'CERTIFICATE'), 'api[_-]?key\s*=\s*["''][A-Za-z0-9]{16,}', 'secret\s*=\s*["''][A-Za-z0-9]{16,}', 'token\s*=\s*["''][A-Za-z0-9]{20,}')) {
            if ($text -match $pattern) { $problems += "Possible credential ($pattern): $rel" }
        }
    }
}

Write-Host ("Candidate files scanned: {0}" -f $scanned)
if ($problems.Count -eq 0) {
    Write-Host 'No content unsuitable for a public repository was found.'
    exit 0
}
Write-Host ("Found {0} issue(s):" -f $problems.Count)
$problems | Sort-Object -Unique | ForEach-Object { Write-Host ('  - ' + $_) }
exit 1

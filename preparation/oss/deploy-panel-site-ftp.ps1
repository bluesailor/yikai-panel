param(
    [string]$SiteRoot = 'D:\yikai\wwwroot\panel.yikai',
    [string]$CredentialFile = 'D:\phpstudy_pro\docs\panel.yikai\server.txt',
    [string]$FtpHost = 'panel.yikai.cn',
    [switch]$UseTls
)

$ErrorActionPreference = 'Stop'
$files = @('config.php', 'content.php')
$credentials = [IO.File]::ReadAllLines($CredentialFile)
if ($credentials.Length -lt 7 -or [string]::IsNullOrWhiteSpace($credentials[4]) -or [string]::IsNullOrWhiteSpace($credentials[6])) {
    throw 'FTP credential file does not have the expected format.'
}
$networkCredential = [Net.NetworkCredential]::new($credentials[4].Trim(), $credentials[6].Trim())

function New-FtpRequest([string]$name, [string]$method) {
    $uri = 'ftp://' + $FtpHost + '/' + [Uri]::EscapeDataString($name)
    $request = [Net.FtpWebRequest][Net.WebRequest]::Create($uri)
    $request.Method = $method
    $request.Credentials = $networkCredential
    $request.EnableSsl = [bool]$UseTls
    $request.UsePassive = $true
    $request.Timeout = 30000
    $request.ReadWriteTimeout = 30000
    return $request
}

function Read-FtpFile([string]$name) {
    $request = New-FtpRequest $name ([Net.WebRequestMethods+Ftp]::DownloadFile)
    $response = [Net.FtpWebResponse]$request.GetResponse()
    try {
        $stream = $response.GetResponseStream()
        $buffer = [IO.MemoryStream]::new()
        try { $stream.CopyTo($buffer); return $buffer.ToArray() }
        finally { $buffer.Dispose(); $stream.Dispose() }
    }
    finally { $response.Dispose() }
}

function Write-FtpFile([string]$name, [byte[]]$bytes) {
    $request = New-FtpRequest $name ([Net.WebRequestMethods+Ftp]::UploadFile)
    $request.ContentLength = $bytes.Length
    $stream = $request.GetRequestStream()
    try { $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
    $response = [Net.FtpWebResponse]$request.GetResponse()
    $response.Dispose()
}

function Hash-Bytes([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}

$backupRoot = Join-Path $PSScriptRoot ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-before-oss')
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$before = @{}
$after = @{}
foreach ($name in $files) {
    $before[$name] = Read-FtpFile $name
    [IO.File]::WriteAllBytes((Join-Path $backupRoot $name), $before[$name])
    $after[$name] = [IO.File]::ReadAllBytes((Join-Path $SiteRoot $name))
    Write-Output "Backup $name SHA256=$(Hash-Bytes $before[$name])"
}

$uploaded = @()
try {
    foreach ($name in $files) {
        $uploaded += $name
        Write-FtpFile $name $after[$name]
        $readback = Read-FtpFile $name
        if ((Hash-Bytes $readback) -ne (Hash-Bytes $after[$name])) {
            throw "Remote SHA-256 mismatch: $name"
        }
        Write-Output "Verified $name SHA256=$(Hash-Bytes $readback)"
    }
}
catch {
    $failure = $_
    foreach ($name in $uploaded) {
        try {
            Write-FtpFile $name $before[$name]
            $restored = Read-FtpFile $name
            if ((Hash-Bytes $restored) -ne (Hash-Bytes $before[$name])) {
                Write-Warning "Rollback readback mismatch: $name"
            }
        }
        catch { Write-Warning "Rollback failed for $name" }
    }
    throw $failure
}

Write-Output "Remote backup: $backupRoot"

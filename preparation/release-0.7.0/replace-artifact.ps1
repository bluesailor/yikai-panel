param(
    [int]$TimeoutMinutes = 360,
    [string]$Expected = 'C8B8A08AFF70497C766559031F871C2BE28238BCF424BB1DD0FA8EF28BA22957'
)
$ErrorActionPreference = 'Stop'
# 等安装向导关闭后，把修复版放回正式产物位置、重算校验文件，并核对内容哈希。
# 安装程序自身锁着目标文件，直接覆盖会失败，所以轮询等待：默认最多 6 小时，每 15 秒检查一次。
# 注意：param() 必须是脚本第一条语句，前面不能有赋值语句。
$target = 'D:\yikai\packages\YikaiPanel-0.7.0-setup-x64.exe'
$source = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\out\YikaiPanel-0.7.0-setup-x64.exe'
$marker = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\artifact-replaced.txt'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
function Note([string]$text) {
    ("{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $text) | Set-Content $marker -Encoding UTF8
    Write-Host $text
}
while ((Get-Date) -lt $deadline) {
    $busy = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'YikaiPanel-0.7.0-setup-x64*' }
    if ($busy) { Start-Sleep -Seconds 15; continue }
    try {
        Copy-Item $source $target -Force -ErrorAction Stop
        $hash = (Get-FileHash $target -Algorithm SHA256).Hash
        if ($hash -ne $Expected) { Note "replaced but hash mismatch: $hash"; exit 1 }
        Set-Content -Path ($target + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($target)) -Encoding ASCII
        Note "replaced ok; sha256=$hash"
        exit 0
    } catch {
        Start-Sleep -Seconds 15
    }
}
Note "not replaced within the wait window ($TimeoutMinutes minutes)"
exit 1

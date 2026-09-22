$ErrorActionPreference = 'Stop'
# 生成发布包的 SHA-256 校验文件（标准格式：<hash>  <文件名>），供 dmh 用 certutil 或 Get-FileHash 核对。
$file = 'D:\yikai\packages\YikaiPanel-0.7.0-setup-x64.exe'
$name = [IO.Path]::GetFileName($file)
$hash = (Get-FileHash $file -Algorithm SHA256).Hash
$size = (Get-Item $file).Length
Set-Content -Path ($file + '.sha256') -Value ($hash + '  ' + $name) -Encoding ASCII
Write-Host ('file:   ' + $file)
Write-Host ('size:   {0:N0} bytes ({1:N2} MB)' -f $size, ($size / 1MB))
Write-Host ('sha256: ' + $hash)

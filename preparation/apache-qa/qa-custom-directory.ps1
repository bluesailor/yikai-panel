param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 新建项目指定存放位置（含中文和空格路径），在 Nginx / Apache 下都能访问；只使用 $Root 的独立端口。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+$arguments));$p.WaitForExit()
    $msg=if(Test-Path $err){(Get-Content $err -TotalCount 1 -Encoding UTF8)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+$arguments) $msg
}
function Config(){ Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Save($json){ $json | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8 }
function Body([string]$url){ try{ (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 20).Content }catch{ '' } }

$parent=Join-Path $Root "自定义 位置"; $target=Join-Path $parent 'qacustom.yikai'
if(Test-Path $parent){Remove-Item -Recurse -Force $parent}
Panel '--stop'
$json=Config; $json.webServer='nginx'; $json.sites=@($json.sites | Where-Object id -ne 'qacustom_yikai'); Save $json

Panel "--create-site qacustom --template php --database sqlite --php 8.2 --directory `"$target`""
$site=(Config).sites | Where-Object id -eq 'qacustom_yikai'
Check ($site -and $site.directory -eq $target) 'project registered at chosen location' $site.directory
Check (Test-Path (Join-Path $target 'index.php')) 'project files created at chosen location'
Check (!(Test-Path (Join-Path $Root 'wwwroot\qacustom.yikai'))) 'nothing created in default wwwroot'

Panel "--create-site qacustom2 --template php --database sqlite --directory `"$target`"" 1
Check (@((Config).sites | Where-Object id -eq 'qacustom2_yikai').Count -eq 0) 'existing target folder is rejected'

$json=Config; $i=0; foreach($s in $json.sites){ $s.enabled=$true; $s.httpPort=18081+$i; $s.fastCgiPort=19085+$i; $i++ }; Save $json
$url="http://127.0.0.1:$(((Config).sites | Where-Object id -eq 'qacustom_yikai').httpPort)/"
Panel '--start'
Check ((Body $url) -match 'PHP project is ready') 'nginx serves project from chinese path with space'
Panel '--web-server apache'
Check ((Body $url) -match 'PHP project is ready') 'apache serves project from chinese path with space'
Panel '--web-server nginx'
Panel '--stop'

$json=Config; $json.sites=@($json.sites | Where-Object id -ne 'qacustom_yikai'); Save $json
Remove-Item -Recurse -Force $parent
$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification-custom-directory.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"

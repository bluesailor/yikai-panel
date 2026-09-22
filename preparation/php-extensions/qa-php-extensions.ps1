param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# PHP 扩展勾选：启用、关闭、zend_extension、加载失败回滚、必需扩展保护、运行中重启。
# $Root 必须有独立的 soft\php\8.2（真实副本，不是指向正式环境的链接），脚本会修改它的 php.ini。
$ErrorActionPreference='Stop'
if((Get-Item (Join-Path $Root 'soft\php\8.2')).Attributes -match 'ReparsePoint'){ throw 'soft\php\8.2 is a link to a real environment; refusing to edit its php.ini.' }
$report=[System.Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+$arguments));$p.WaitForExit()
    $script:lastError=if(Test-Path $err){[IO.File]::ReadAllText($err)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+$arguments) (($script:lastError -split "`n")[0])
}
function Keys(){ $f=Join-Path $Root 'temp\panel-processes.json'; if(!(Test-Path $f)){return @{}}; $h=@{}; foreach($e in (Get-Content $f -Raw | ConvertFrom-Json)){ $h[$e.Key]=$e.Pid }; $h }
$ini=Join-Path $Root 'soft\php\8.2\php.ini'
$php=Join-Path $Root 'soft\php\8.2\php.exe'
$ext=Join-Path $Root 'soft\php\8.2\ext'
function IniText(){ [IO.File]::ReadAllText($ini) }
function Modules(){ (& $php -c $ini -m 2>$null) -join ' ' }

# 1. 初始状态：面板列出的扩展与 php.ini 一致
Check ((IniText) -match '(?m)^extension=mbstring') 'php.ini starts with mbstring enabled'
Check ((Modules) -match '\bcurl\b') 'php -m lists curl'
Check ((Modules) -notmatch '\bbz2\b') 'bz2 is off before the test'

# 2. 启用一个扩展：写入 php.ini、备份、真的被 PHP 加载
Panel '--php-extensions 8.2 --enable bz2'
Check ((IniText) -match '(?m)^extension=bz2') 'bz2 written as extension=bz2'
Check ((Modules) -match '\bbz2\b') 'bz2 loaded by php'
Check (@(Get-ChildItem (Join-Path $Root 'backups\config') -Filter 'php-8.2-*.ini').Count -ge 1) 'php.ini backed up before saving'

# 3. 关闭一个扩展：注释掉，其他选择保留
Panel '--php-extensions 8.2 --disable exif'
Check ((IniText) -match '(?m)^;extension=exif') 'exif commented out'
Check ((Modules) -notmatch '\bexif\b') 'exif no longer loaded'
Check ((IniText) -match '(?m)^extension=bz2' -and (Modules) -match '\bbz2\b') 'earlier choice kept'

# 4. opcache 必须写成 zend_extension
Panel '--php-extensions 8.2 --enable opcache'
Check ((IniText) -match '(?m)^zend_extension=opcache') 'opcache written as zend_extension'
Check ((Modules) -match 'Zend OPcache') 'opcache loaded'

# 5. 加载失败的扩展：检查不通过，php.ini 保持原样
$before=IniText
[IO.File]::WriteAllBytes((Join-Path $ext 'php_qabroken.dll'),[byte[]](1..64))
Panel '--php-extensions 8.2 --enable qabroken' 1
Check ((IniText) -eq $before) 'php.ini unchanged after a failed extension'
Check ($script:lastError -match 'qabroken') 'error names the failing extension' (($script:lastError -split "`n")[0])
Check ((Modules) -match '\bbz2\b') 'php still starts with the previous configuration'
Remove-Item (Join-Path $ext 'php_qabroken.dll')

# 6. 必需扩展不能关闭，未知扩展被拒绝
Panel '--php-extensions 8.2 --disable mbstring' 1
Check ((IniText) -eq $before -and $script:lastError -match 'mbstring') 'required extension refused'
Panel '--php-extensions 8.2 --enable definitely_not_here' 1
Check ((IniText) -eq $before) 'unknown extension refused'

# 7. 运行中保存：该版本 PHP 重启，站点继续可用
Panel '--service php8.2 --action start'
$first=(Keys)['php-qaext_yikai']
Panel '--php-extensions 8.2 --enable ftp'
$second=(Keys)['php-qaext_yikai']
Check ($first -and $second -and $first -ne $second) "running php restarted ($first -> $second)"
Check ((IniText) -match '(?m)^extension=ftp' -and (Modules) -match '\bftp\b') 'ftp enabled and loaded'
Panel '--service php8.2 --action stop'
Check (-not (Keys).ContainsKey('php-qaext_yikai')) 'php stopped after the test'

# 8. 项目单独启用扩展：只进这个项目的 php.ini，其他项目和版本设置不变
$siteIni=Join-Path $Root 'config\panel-php-qaext_yikai.ini'
$otherIni=Join-Path $Root 'config\panel-php-qaext2_yikai.ini'
function SiteModules([string]$ini){ (& $php -c $ini -m 2>$null) -join ' ' }
Panel '--service php8.2 --action start'
$firstA=(Keys)['php-qaext_yikai']; $firstB=(Keys)['php-qaext2_yikai']
Check ($firstA -and $firstB) "both projects running ($firstA / $firstB)"
Panel '--php-extensions 8.2 --site qaext_yikai --enable gmp'
Check ([IO.File]::ReadAllText($siteIni) -match '(?m)^extension=gmp') 'project ini enables gmp'
Check ([IO.File]::ReadAllText($otherIni) -notmatch '(?m)^extension=gmp') 'other project keeps the version settings'
Check ((IniText) -notmatch '(?m)^extension=gmp') 'version php.ini untouched by the project setting'
Check ((SiteModules $siteIni) -match '\bgmp\b' -and (SiteModules $otherIni) -notmatch '\bgmp\b') 'gmp loaded only for this project'
$secondA=(Keys)['php-qaext_yikai']; $secondB=(Keys)['php-qaext2_yikai']
Check ($secondA -ne $firstA -and $secondB -eq $firstB) "only this project's php restarted ($firstA -> $secondA, other $firstB)"

# 9. 项目单独关闭版本里启用的扩展
Panel '--php-extensions 8.2 --site qaext_yikai --disable intl'
Check ([IO.File]::ReadAllText($siteIni) -match '(?m)^;extension=intl') 'project ini disables intl'
Check ((IniText) -match '(?m)^extension=intl') 'version php.ini still enables intl'
Check ((SiteModules $siteIni) -notmatch '\bintl\b' -and (SiteModules $otherIni) -match '\bintl\b') 'intl off for this project only'
Check ([IO.File]::ReadAllText($siteIni) -match '(?m)^extension=gmp') 'earlier project choice kept'

# 10. 项目级也不能关掉必需扩展
$beforeSite=[IO.File]::ReadAllText($siteIni)
Panel '--php-extensions 8.2 --site qaext_yikai --disable mbstring' 1
Check ([IO.File]::ReadAllText($siteIni) -eq $beforeSite) 'required extension refused for a project'

# 11. 改版本设置后，项目自己的增减仍然保留
Panel '--php-extensions 8.2 --enable soap'
Check ((IniText) -match '(?m)^extension=soap') 'version php.ini enables soap'
Check ([IO.File]::ReadAllText($siteIni) -match '(?m)^extension=soap' -and [IO.File]::ReadAllText($siteIni) -match '(?m)^extension=gmp' -and [IO.File]::ReadAllText($siteIni) -match '(?m)^;extension=intl') 'project keeps its own changes on top of the version'

# 12. 跟随版本设置：清掉项目的增减
Panel '--php-extensions 8.2 --site qaext_yikai --follow-version'
$cfg=Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
$site=$cfg.sites | Where-Object id -eq 'qaext_yikai'
Check (-not $site.extensionsOn -and -not $site.extensionsOff) 'project extension changes cleared'
Check ([IO.File]::ReadAllText($siteIni) -notmatch '(?m)^extension=gmp' -and [IO.File]::ReadAllText($siteIni) -match '(?m)^extension=intl') 'project ini back to the version settings'
Panel '--service php8.2 --action stop'

$out=Join-Path $PSScriptRoot 'verification-php-extensions.json'
$report | ConvertTo-Json -Depth 4 | Set-Content $out -Encoding UTF8
"{0}/{1} passed" -f (@($report | Where-Object passed).Count),$report.Count

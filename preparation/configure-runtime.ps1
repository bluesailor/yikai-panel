param([string]$Root = 'D:\yikai')
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$unixRoot = $rootPath.Replace('\','/')
$httpPort = $null
foreach ($candidate in @(80,8081,8082,8083,8084)) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$candidate)
    try {
        $listener.ExclusiveAddressUse = $true
        $listener.Start()
        $httpPort = $candidate
        break
    } catch { } finally { $listener.Stop() }
}
if ($null -eq $httpPort) { throw 'No available HTTP port in the preparation range' }
function Write-LocalFile([string]$RelativePath, [string]$Content) {
    $target = Join-Path $rootPath $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::WriteAllText($target, $Content, [Text.UTF8Encoding]::new($false))
}
foreach ($directory in @('config','logs','temp/uploads','temp/phpmyadmin','data/mysql57','data/mysql80')) {
    [IO.Directory]::CreateDirectory((Join-Path $rootPath $directory)) | Out-Null
}
Copy-Item -LiteralPath "$rootPath\soft\php\8.5\cacert.pem" -Destination "$rootPath\config\cacert.pem" -Force
foreach ($version in @('8.0','8.2','8.5')) {
    [IO.Directory]::CreateDirectory("$rootPath\temp\sessions-$version") | Out-Null
    $ini = @"
[PHP]
engine=On
short_open_tag=Off
precision=14
output_buffering=4096
expose_php=Off
max_execution_time=120
max_input_time=120
max_input_vars=10000
memory_limit=256M
error_reporting=E_ALL
display_errors=Off
display_startup_errors=Off
log_errors=On
error_log="$unixRoot/logs/php-$version.log"
variables_order="GPCS"
request_order="GP"
register_argc_argv=Off
auto_globals_jit=On
default_charset="UTF-8"
extension_dir="$unixRoot/soft/php/$version/ext"
file_uploads=On
upload_tmp_dir="$unixRoot/temp/uploads"
upload_max_filesize=128M
post_max_size=160M
max_file_uploads=30
allow_url_fopen=On
allow_url_include=Off
cgi.fix_pathinfo=0
date.timezone=Asia/Shanghai
session.save_handler=files
session.save_path="$unixRoot/temp/sessions-$version"
session.use_strict_mode=1
session.cookie_httponly=1
session.cookie_samesite=Lax
curl.cainfo="$unixRoot/config/cacert.pem"
openssl.cafile="$unixRoot/config/cacert.pem"
"@
    foreach ($extension in @('mbstring','fileinfo','curl','openssl','gd','pdo_mysql','pdo_sqlite','sqlite3','zip','mysqli','sodium','intl','exif')) {
        if (Test-Path -LiteralPath "$rootPath\soft\php\$version\ext\php_$extension.dll") {
            $ini += "`nextension=$extension"
        }
    }
    Write-LocalFile "soft/php/$version/php.ini" ($ini + "`n")
}
[IO.Directory]::CreateDirectory("$rootPath\temp\sessions-phpmyadmin") | Out-Null
$managementIni = [IO.File]::ReadAllText("$rootPath\soft\php\8.2\php.ini").Replace('sessions-8.2','sessions-phpmyadmin').Replace('php-8.2.log','phpmyadmin-php.log')
Write-LocalFile 'config/phpmyadmin-php.ini' $managementIni

foreach ($version in @('5.7','8.0')) {
    $instance = if ($version -eq '5.7') { 'mysql57' } else { 'mysql80' }
    $port = if ($version -eq '5.7') { 3307 } else { 3308 }
    $ini = @"
[mysqld]
basedir="$unixRoot/soft/mysql/$version"
datadir="$unixRoot/data/$instance"
port=$port
bind-address=127.0.0.1
character-set-server=utf8mb4
collation-server=utf8mb4_general_ci
max_allowed_packet=128M
log-error="$unixRoot/logs/$instance.log"
pid-file="$unixRoot/temp/$instance.pid"
[client]
host=127.0.0.1
port=$port
default-character-set=utf8mb4
"@
    if ($version -eq '8.0') { $ini = $ini.Replace('[client]', "mysqlx=0`n[client]") }
    Write-LocalFile "config/$instance.ini" ($ini + "`n")
    Write-LocalFile "config/$instance-client.ini" "[client]`nhost=127.0.0.1`nport=$port`nuser=root`npassword=123456`ndefault-character-set=utf8mb4`n"
}
Write-LocalFile 'preparation/mysql-first-start.sql' "ALTER USER 'root'@'localhost' IDENTIFIED BY '123456';`nCREATE DATABASE IF NOT EXISTS yikaicms CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;`n"

$pma = @'
<?php
declare(strict_types=1);
$cfg['blowfish_secret'] = '__SECRET__';
$cfg['Lang'] = 'zh_CN';
$cfg['VersionCheck'] = false;
$cfg['AllowArbitraryServer'] = false;
$cfg['TempDir'] = '__ROOT__/temp/phpmyadmin';
$cfg['ServerDefault'] = 1;
foreach ([1 => ['MySQL 8.0', 3308], 2 => ['MySQL 5.7', 3307]] as $i => [$label, $port]) {
    $cfg['Servers'][$i]['verbose'] = $label;
    $cfg['Servers'][$i]['host'] = '127.0.0.1';
    $cfg['Servers'][$i]['port'] = (string) $port;
    $cfg['Servers'][$i]['auth_type'] = 'config';
    $cfg['Servers'][$i]['user'] = 'root';
    $cfg['Servers'][$i]['password'] = '123456';
    $cfg['Servers'][$i]['AllowNoPassword'] = false;
}
'@
Write-LocalFile 'soft/phpmyadmin/config.inc.php' ($pma.Replace('__ROOT__',$unixRoot).Replace('__SECRET__',[Guid]::NewGuid().ToString('N')) + "`n")

$rules = [IO.File]::ReadAllText("$rootPath\soft\packages\yikaicms\deploy\nginx-server.conf")
$rules = [regex]::Split($rules, '# ={20,}')[0]
$rules = $rules.Replace('127.0.0.1:9000','127.0.0.1:9082')
# Allow the rewritten API index.php to reach the shared PHP handler.
$rules = $rules.Replace('location ^~ /api/v1/ {','location /api/v1/ {')
Write-LocalFile 'config/yikaicms-rewrite.conf' $rules
Copy-Item -LiteralPath "$rootPath\soft\nginx\conf\mime.types" -Destination "$rootPath\config\mime.types" -Force
Copy-Item -LiteralPath "$rootPath\soft\nginx\conf\fastcgi_params" -Destination "$rootPath\config\fastcgi_params" -Force
$nginx = @'
worker_processes 1;
error_log "__ROOT__/logs/nginx-error.log";
pid "__ROOT__/temp/nginx.pid";
events { worker_connections 1024; }
http {
    include "__ROOT__/config/mime.types";
    default_type application/octet-stream;
    access_log "__ROOT__/logs/nginx-access.log";
    sendfile on;
    keepalive_timeout 30;
    client_max_body_size 160m;
    fastcgi_read_timeout 120s;
    server {
        listen 127.0.0.1:__HTTP_PORT__;
        server_name yikaicms.yikai;
        root "__ROOT__/wwwroot/yikaicms.yikai";
        index index.php index.html;
        include "__ROOT__/config/yikaicms-rewrite.conf";
    }
    server {
        listen 127.0.0.1:8877;
        server_name 127.0.0.1 localhost;
        root "__ROOT__/soft/phpmyadmin";
        index index.php;
        location / { try_files $uri $uri/ =404; }
        location ~ \.php$ {
            try_files $uri =404;
            include "__ROOT__/config/fastcgi_params";
            fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
            fastcgi_pass 127.0.0.1:9083;
        }
    }
}
'@
Write-LocalFile 'config/nginx.conf' ($nginx.Replace('__ROOT__',$unixRoot).Replace('__HTTP_PORT__',[string]$httpPort) + "`n")
$defaults = [ordered]@{
    status='components-prepared'; root=$rootPath; domainSuffix='.yikai'; domain='yikaicms.yikai';
    sitePath="$rootPath\wwwroot\yikaicms.yikai"; php='8.2'; database='mysql80';
    mysqlUser='root'; mysqlPassword='123456'; mysql57Port=3307; mysql80Port=3308;
    httpPort=$httpPort; phpMyAdminPort=8877; siteFastCgiPort=9082; adminFastCgiPort=9083;
    hostsApplied=$false; databasesInitialized=$false; siteInstalled=$false
}
Write-LocalFile 'config/defaults.json' ($defaults | ConvertTo-Json)
Write-Output 'Default PHP, MySQL, phpMyAdmin and Nginx rewrite configurations generated. No services started.'

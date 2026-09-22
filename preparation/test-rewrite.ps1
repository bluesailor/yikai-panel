param([string]$Root = 'D:\yikai')
$ErrorActionPreference = 'Stop'
function Get-TestPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = "$Root\temp\rewrite-$testId"
$webRoot = "$testRoot\www"
[IO.Directory]::CreateDirectory("$webRoot\install") | Out-Null
[IO.Directory]::CreateDirectory("$webRoot\api\v1") | Out-Null
$phpCode = '<?php header("Content-Type: application/json"); echo json_encode(["script"=>$_SERVER["SCRIPT_NAME"],"query"=>$_GET]);'
foreach ($name in @('index.php','news.php','article.php','list.php','detail.php','product.php','page.php','history.php','contact.php','job_detail.php','install/index.php','api/v1/index.php')) {
    [IO.File]::WriteAllText((Join-Path $webRoot $name),$phpCode)
}
$httpPort = Get-TestPort
do { $phpPort = Get-TestPort } while ($phpPort -eq $httpPort)
$slashRoot = $Root.Replace('\','/')
$slashTest = $testRoot.Replace('\','/')
$rules = [IO.File]::ReadAllText("$Root\config\yikaicms-rewrite.conf").Replace('127.0.0.1:9082',"127.0.0.1:$phpPort").Replace('include fastcgi_params;',"include `"$slashRoot/config/fastcgi_params`";")
[IO.File]::WriteAllText("$testRoot\rules.conf",$rules,[Text.UTF8Encoding]::new($false))
$config = @"
worker_processes 1;
pid "$slashTest/nginx.pid";
error_log "$slashTest/nginx-error.log";
events { worker_connections 64; }
http {
    access_log off;
    server {
        listen 127.0.0.1:$httpPort;
        server_name yikaicms.yikai;
        root "$slashTest/www";
        index index.php;
        include "$slashTest/rules.conf";
    }
}
"@
[IO.File]::WriteAllText("$testRoot\nginx.conf",$config,[Text.UTF8Encoding]::new($false))
$phpProcess=$null
$nginxProcess=$null
$checks = [Collections.Generic.List[object]]::new()
try {
    $phpProcess=Start-Process -FilePath "$Root\soft\php\8.2\php-cgi.exe" -ArgumentList @('-c',"`"$Root\soft\php\8.2\php.ini`"",'-b',"127.0.0.1:$phpPort") -WindowStyle Hidden -PassThru
    $nginxProcess=Start-Process -FilePath "$Root\soft\nginx\nginx.exe" -ArgumentList @('-p',"`"$slashRoot/soft/nginx/`"",'-c',"`"$slashTest/nginx.conf`"") -WindowStyle Hidden -PassThru
    $ready=$false
    for($attempt=0;$attempt -lt 30;$attempt++) {
        try { $null=Invoke-RestMethod -Uri "http://127.0.0.1:$httpPort/" -TimeoutSec 2; $ready=$true; break } catch { Start-Sleep -Milliseconds 100 }
    }
    if(!$ready) { throw 'Isolated rewrite test server did not become ready' }
    $cases=@(
        @{path='/';script='/index.php';query=@{}},
        @{path='/news.html';script='/news.php';query=@{}},
        @{path='/news/article/123.html';script='/article.php';query=@{id='123'}},
        @{path='/list/12/page/3.html';script='/list.php';query=@{id='12';page='3'}},
        @{path='/product/widget.html';script='/list.php';query=@{slug='product';cat='widget'}},
        @{path='/product/123.html';script='/product.php';query=@{id='123'}},
        @{path='/en/news.html';script='/news.php';query=@{_lang='en'}},
        @{path='/ja/product/123.html';script='/product.php';query=@{id='123';_lang='ja'}},
        @{path='/contact.html';script='/contact.php';query=@{}},
        @{path='/about/team.html';script='/page.php';query=@{parent='about';slug='team'}},
        @{path='/api/v1/contents';script='/api/v1/index.php';query=@{resource='contents'}},
        @{path='/install/index.php';script='/install/index.php';query=@{}}
    )
    foreach($case in $cases) {
        $actual=Invoke-RestMethod -Uri ("http://127.0.0.1:$httpPort"+$case.path) -Headers @{Host='yikaicms.yikai'} -TimeoutSec 5
        if($actual.script -ne $case.script) { throw "Unexpected target for $($case.path): $($actual.script)" }
        foreach($key in $case.query.Keys) { if($actual.query.$key -ne $case.query[$key]) { throw "Unexpected query for $($case.path): $key" } }
        $checks.Add(@{path=$case.path;target=$actual.script;pass=$true})
    }
    $report=@{checkedAt=(Get-Date -Format o);php='8.2.33';scope='Real Nginx and FastCGI rewrite requests against isolated fixtures; no CMS database or hosts changes';checks=$checks.ToArray()}
    [IO.File]::WriteAllText("$Root\preparation\rewrite-verification.json",($report|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    Write-Output "PASS: $($checks.Count) real HTTP rewrite routes"
} finally {
    if(Test-Path -LiteralPath "$testRoot\nginx.pid") {
        & "$Root\soft\nginx\nginx.exe" -p "$slashRoot/soft/nginx/" -c "$slashTest/nginx.conf" -s quit
        if($nginxProcess) { $null=$nginxProcess.WaitForExit(5000) }
    }
    if($phpProcess -and !$phpProcess.HasExited) { $phpProcess.Kill(); $phpProcess.WaitForExit() }
}

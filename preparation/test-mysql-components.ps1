param([string]$Root = 'D:\yikai')
$ErrorActionPreference='Stop'
$results=[Collections.Generic.List[object]]::new()
$probe=@'
$db = new PDO('mysql:host=127.0.0.1;port=' . $argv[1] . ';dbname=yikaicms;charset=utf8mb4', 'root', '123456', [PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION]);
$db->exec('CREATE TABLE IF NOT EXISTS component_probe (id INT PRIMARY KEY, value VARCHAR(100)) CHARACTER SET utf8mb4');
$db->exec('DELETE FROM component_probe');
$q=$db->prepare('INSERT INTO component_probe VALUES (?, ?)');
$q->execute([1, "\u{4e2d}\u{6587}\u{65e5}\u{672c}\u{8a9e}"]);
$value=$db->query('SELECT value FROM component_probe WHERE id=1')->fetchColumn();
if ($value !== "\u{4e2d}\u{6587}\u{65e5}\u{672c}\u{8a9e}") { exit(2); }
echo json_encode(['php'=>PHP_VERSION,'mysql'=>$db->query('SELECT VERSION()')->fetchColumn(),'utf8_roundtrip'=>true]);
'@
foreach($version in @('5.7','8.0')) {
    $testRoot="$Root\temp\mysql-$version-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory("$testRoot\data") | Out-Null
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop()
    $slashRoot=$Root.Replace('\','/')
    $slashTest=$testRoot.Replace('\','/')
    $ini=@"
[mysqld]
basedir="$slashRoot/soft/mysql/$version"
datadir="$slashTest/data"
port=$port
bind-address=127.0.0.1
character-set-server=utf8mb4
collation-server=utf8mb4_general_ci
log-error="$slashTest/mysql.log"
"@
    if($version -eq '8.0') { $ini+="`nmysqlx=0`n" }
    [IO.File]::WriteAllText("$testRoot\my.ini",$ini,[Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath "$Root\preparation\mysql-first-start.sql" -Destination "$testRoot\init.sql"
    $server=$null
    try {
        $init=Start-Process -FilePath "$Root\soft\mysql\$version\bin\mysqld.exe" -ArgumentList @("--defaults-file=`"$testRoot\my.ini`"",'--initialize-insecure') -PassThru -WindowStyle Hidden
        if(!$init.WaitForExit(60000)) { $init.Kill(); throw "MySQL $version initialization timed out" }
        if($init.ExitCode -ne 0) { throw "MySQL $version initialization failed; see $testRoot/mysql.log" }
        $server=Start-Process -FilePath "$Root\soft\mysql\$version\bin\mysqld.exe" -ArgumentList @("--defaults-file=`"$testRoot\my.ini`"","--init-file=`"$testRoot\init.sql`"") -PassThru -WindowStyle Hidden
        $ready=$false
        for($attempt=0;$attempt -lt 100;$attempt++) {
            if($server.HasExited) { throw "MySQL $version stopped; see $testRoot/mysql.log" }
            $tcp=[Net.Sockets.TcpClient]::new()
            try { $tcp.Connect('127.0.0.1',$port); $ready=$true; break } catch { Start-Sleep -Milliseconds 200 } finally {$tcp.Dispose()}
        }
        if(!$ready) { throw "MySQL $version startup timed out" }
        foreach($php in @('8.0','8.2','8.5')) {
            $output=& "$Root\soft\php\$php\php.exe" -c "$Root\soft\php\$php\php.ini" -r $probe $port
            if($LASTEXITCODE -ne 0) { throw "PHP $php / MySQL $version probe failed" }
            $results.Add(($output | ConvertFrom-Json))
            Write-Output "PASS PHP $php / MySQL $version root login and UTF-8 CRUD"
        }
    } finally {
        if($server -and !$server.HasExited) {
            [IO.File]::WriteAllText("$testRoot\client.ini","[client]`nhost=127.0.0.1`nport=$port`nuser=root`npassword=123456`n")
            & "$Root\soft\mysql\$version\bin\mysqladmin.exe" "--defaults-file=$testRoot\client.ini" shutdown
            if(!$server.WaitForExit(15000)) { throw "Test MySQL $version did not shut down; PID $($server.Id)" }
        }
    }
}
$report=@{checkedAt=(Get-Date -Format o);scope='Isolated newly initialized test databases only; default site remains uninstalled';results=$results.ToArray()}
[IO.File]::WriteAllText("$Root\preparation\mysql-verification.json",($report|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))

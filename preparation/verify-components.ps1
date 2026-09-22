param([string]$Root = 'D:\yikai')
$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
$probe = @'
$required = ['pdo','json','mbstring','fileinfo','dom','curl','openssl','gd','zip','simplexml','pdo_mysql','pdo_sqlite','sqlite3','mysqli','sodium'];
$missing = array_values(array_filter($required, fn($e) => !extension_loaded($e)));
$db = new PDO('sqlite::memory:');
$db->exec('CREATE TABLE probe (v TEXT)');
$query = $db->prepare('INSERT INTO probe VALUES (?)');
$query->execute(['Yikai-123']);
$ok = $db->query('SELECT v FROM probe')->fetchColumn() === 'Yikai-123';
echo json_encode(['php'=>PHP_VERSION,'missing'=>$missing,'pdo_drivers'=>PDO::getAvailableDrivers(),'sqlite_roundtrip'=>$ok]);
exit(count($missing) || !$ok ? 1 : 0);
'@
foreach ($version in @('8.0','8.2','8.5')) {
    $output = & "$Root\soft\php\$version\php.exe" -c "$Root\soft\php\$version\php.ini" -r $probe
    if ($LASTEXITCODE -ne 0) { throw "PHP $version verification failed: $output" }
    $results.Add(($output | ConvertFrom-Json))
}
foreach ($version in @('5.7','8.0')) {
    $output = & "$Root\soft\mysql\$version\bin\mysqld.exe" --no-defaults --version
    if ($LASTEXITCODE -ne 0) { throw "MySQL $version version probe failed" }
    $results.Add(@{component="mysql-$version";versionOutput="$output";pass=$true})
}
& "$Root\soft\php\8.2\php.exe" -c "$Root\config\phpmyadmin-php.ini" -l "$Root\soft\phpmyadmin\config.inc.php"
if ($LASTEXITCODE -ne 0) { throw 'phpMyAdmin config failed lint' }
& "$Root\soft\nginx\nginx.exe" -p ($Root.Replace('\','/') + '/soft/nginx/') -c ($Root.Replace('\','/') + '/config/nginx.conf') -t
if ($LASTEXITCODE -ne 0) { throw 'Nginx configuration failed validation' }
$results.Add(@{component='nginx-config';pass=$true})
$results.Add(@{component='phpmyadmin-config';pass=$true})
$report = @{checkedAt=(Get-Date -Format o);scope='component loading, SQLite in-memory CRUD and configuration syntax only';results=$results.ToArray()}
[IO.File]::WriteAllText("$Root\preparation\verification.json", ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 8

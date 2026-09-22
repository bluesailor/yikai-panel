<?php
declare(strict_types=1);
const YIKAI_ROOT = __DIR__ . '/../..';
// panel.json 由面板以 UTF-8（无 BOM）写入；这里顺手去掉可能存在的 BOM——
// 第三方工具（例如 PowerShell 的 Set-Content -Encoding UTF8）改写配置时会加 BOM，
// json_decode 遇到 BOM 直接报 Syntax error，会让面板启动、改端口等全线失败且报错难懂。
$configText = (string)file_get_contents(YIKAI_ROOT . '/config/panel.json');
if (str_starts_with($configText, "\xEF\xBB\xBF")) $configText = substr($configText, 3);
$config = json_decode($configText,true,512,JSON_THROW_ON_ERROR);
$translations = require __DIR__.'/lang.php';
$context=explode('/',trim((string)($_SERVER['PATH_INFO']??''),'/'));
$language = (string)($_GET['lang'] ?? $context[1] ?? 'zh');
if (!isset($translations[$language])) $language='zh';
if (PHP_SAPI !== 'cli') {
    if (!in_array($_SERVER['REMOTE_ADDR'] ?? '', ['127.0.0.1','::1'],true)) {http_response_code(403);exit;}
    if (!defined('YIKAI_ADMINER')) {
        // session.save_path 是随包 php.ini 里写死的 <root>/temp/sessions-phpmyadmin，PHP 不会自建这个目录。
        // 缺了它 session 根本起不来（session_start 返回 false），CSRF 每次请求都重新生成，
        // 备份 / 恢复 / 执行 SQL 全会被 CSRF 校验挡掉。面板启动时也会建这个目录，这里再兜一次底。
        $sessionDir = (string)ini_get('session.save_path');
        if ($sessionDir !== '' && !is_dir($sessionDir)) @mkdir($sessionDir, 0777, true);
        session_name('YikaiDatabase');
        session_start();
        $_SESSION['csrf'] ??= bin2hex(random_bytes(24));
    }
}
function h(mixed $value): string {return htmlspecialchars((string)$value,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}
function t(string $key): string {global $translations,$language;return $translations[$language][$key] ?? $key;}
function site(): array {
    global $config;
    global $context;
    $id=(string)($_GET['site'] ?? ($context[0]!==''?$context[0]:$config['sites'][0]['id']));
    foreach($config['sites'] as $site) if($site['id']===$id) return $site;
    throw new RuntimeException('Unknown website');
}
function linkTo(string $path='index.php',array $extra=[]): string {
    global $language;
    if($path==='adminer.php')return '/adminer.php/'.rawurlencode(site()['id']).'/'.$language.'?'.http_build_query($extra);
    return '/'.$path.'?'.http_build_query(array_merge(['site'=>site()['id'],'lang'=>$language],$extra));
}
function sqlitePath(array $site): string {return $site['directory'].'/storage/database.sqlite';}
function dbPort(array $site): int {global $config;return (int)$config[$site['database']==='mysql57'?'mysql57Port':'mysql80Port'];}
function dbPassword(array $site): string {global $config;return (string)($config[$site['database']==='mysql57'?'mysql57Password':'mysql80Password']??$config['mysqlPassword']);}
function connectDatabase(array $site, bool $select=true): PDO {
    global $config;
    if($site['database']==='sqlite') {
        if(!is_file(sqlitePath($site))) throw new RuntimeException(t('missing'));
        return new PDO('sqlite:'.sqlitePath($site),null,null,[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION]);
    }
    return new PDO('mysql:host=127.0.0.1;port='.dbPort($site).($select?';dbname='.$site['databaseName']:'').';charset=utf8mb4',$config['mysqlUser'],dbPassword($site),[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION]);
}
function backupDirectory(array $site): string {
    $path=YIKAI_ROOT.'/backups/databases/'.$site['id'];
    if(!is_dir($path)&&!mkdir($path,0777,true)&&!is_dir($path)) throw new RuntimeException('Cannot create backup folder');
    return $path;
}
function mysqlCommand(array $site, bool $export): array {
    global $config;
    $version=$site['database']==='mysql57'?'5.7':'8.0';
    $credentials=YIKAI_ROOT.'/temp/db-'.$site['id'].'-client.ini';
    $quote=static fn(string $s):string=>'"'.str_replace(["\\",'"',"\n","\r"],["\\\\",'\\"','\\n','\\r'],$s).'"';
    file_put_contents($credentials,"[client]\nhost=127.0.0.1\nport=".dbPort($site)."\nuser=".$quote($config['mysqlUser'])."\npassword=".$quote(dbPassword($site))."\ndefault-character-set=utf8mb4\n");
    $command=[realpath(YIKAI_ROOT).'/soft/mysql/'.$version.'/bin/'.($export?'mysqldump.exe':'mysql.exe'),'--defaults-file='.realpath($credentials)];
    if($export) array_push($command,'--single-transaction','--routines','--events','--triggers');
    $command[]=$site['databaseName'];
    return $command;
}
function runDatabaseCommand(array $command, ?string $input, string $output): void {
    $error=YIKAI_ROOT.'/temp/database-'.bin2hex(random_bytes(8)).'.err';
    $process=proc_open($command,[0=>['file',$input??'NUL','r'],1=>['file',$output,'w'],2=>['file',$error,'w']],$pipes,null,null,['bypass_shell'=>true]);
    if(!is_resource($process)) throw new RuntimeException('Could not start database tool');
    $code=proc_close($process);
    $message=(string)file_get_contents($error);unlink($error);
    if($code!==0) throw new RuntimeException(trim($message) ?: 'Database tool failed');
}

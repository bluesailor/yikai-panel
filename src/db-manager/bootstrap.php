<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli'){http_response_code(404);exit;}
require __DIR__.'/common.php';
if(($argv[1]??'')==='--check') {
    $target=['database'=>$argv[2],'databaseName'=>''];
    connectDatabase($target,false)->query('SELECT 1');
    echo "Connected\n";exit;
}
foreach($config['sites'] as $site) {
    if($site['database']==='none')continue;
    if(($argv[1]??'')==='--site' && $site['id']!==($argv[2]??''))continue;
    if(isset($site['enabled']) && !$site['enabled'])continue;
    if($site['database']==='sqlite') {
        if(!is_file(sqlitePath($site))){$db=new SQLite3(sqlitePath($site));$db->exec('PRAGMA user_version=0');$db->close();}
    } else {
        if(!preg_match('/^[a-zA-Z0-9_]{1,64}$/D',$site['databaseName'])) throw new RuntimeException('Invalid database name');
        connectDatabase($site,false)->exec('CREATE DATABASE IF NOT EXISTS `'.$site['databaseName'].'` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci');
    }
}
echo "Databases ready\n";

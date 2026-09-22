<?php
declare(strict_types=1);
require __DIR__.'/common.php';
set_time_limit(600);
$chosen=site();$folder=backupDirectory($chosen);
if(isset($_GET['download'])) {
    $name=(string)$_GET['download'];
    if(!preg_match('/^[a-zA-Z0-9_.-]+\.(sql|sqlite)$/D',$name)||!is_file($folder.'/'.$name)){http_response_code(404);exit;}
    header('Content-Type: application/octet-stream');header('Content-Disposition: attachment; filename="'.$name.'"');header('Content-Length: '.filesize($folder.'/'.$name));readfile($folder.'/'.$name);exit;
}
header('Content-Type: application/json; charset=utf-8');
if($_SERVER['REQUEST_METHOD']!=='POST'||!hash_equals($_SESSION['csrf'],(string)($_POST['csrf']??''))){http_response_code(403);echo json_encode(['ok'=>false,'message'=>t('csrf')]);exit;}
session_write_close();
$lock=fopen($folder.'/.operation.lock','c');
try {
    if(!$lock||!flock($lock,LOCK_EX|LOCK_NB)) throw new RuntimeException(t('busy'));
    $isSqlite=$chosen['database']==='sqlite';
    if(($_POST['action']??'')==='backup') {
        connectDatabase($chosen);
        $name=$chosen['id'].'-'.date('Ymd-His').'-'.bin2hex(random_bytes(3)).($isSqlite?'.sqlite':'.sql');
        $target=$folder.'/'.$name;
        if($isSqlite){$source=new SQLite3(sqlitePath($chosen),SQLITE3_OPEN_READONLY);$destination=new SQLite3($target);if(!$source->backup($destination))throw new RuntimeException($source->lastErrorMsg());$source->close();$destination->close();}
        else {try{runDatabaseCommand(mysqlCommand($chosen,true),null,$target);}catch(Throwable $error){if(is_file($target))unlink($target);throw $error;}}
        echo json_encode(['ok'=>true,'message'=>t('saved'),'download'=>linkTo('action.php',['download'=>$name])]);
    } elseif(($_POST['action']??'')==='restore') {
        $file=$_FILES['backup']??null;
        if(!$file||$file['error']!==UPLOAD_ERR_OK) throw new RuntimeException(t('file_required'));
        $extension=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION));
        if($isSqlite) {
            if(!in_array($extension,['db','sqlite','sqlite3'],true))throw new RuntimeException(t('invalid_file'));
            $source=new SQLite3($file['tmp_name'],SQLITE3_OPEN_READONLY);
            if($source->querySingle('PRAGMA quick_check')!=='ok')throw new RuntimeException(t('invalid_file'));
            $destination=new SQLite3(sqlitePath($chosen));$destination->busyTimeout(5000);
            if(!$source->backup($destination))throw new RuntimeException($source->lastErrorMsg());$source->close();$destination->close();
        } else {
            if($extension!=='sql')throw new RuntimeException(t('invalid_file'));
            runDatabaseCommand(mysqlCommand($chosen,false),$file['tmp_name'],'NUL');
        }
        echo json_encode(['ok'=>true,'message'=>t('restored')]);
    } else {throw new RuntimeException('Unknown action');}
} catch(Throwable $error){http_response_code(400);echo json_encode(['ok'=>false,'message'=>$error->getMessage()],JSON_UNESCAPED_UNICODE);}
finally {if(is_resource($lock)){flock($lock,LOCK_UN);fclose($lock);}}

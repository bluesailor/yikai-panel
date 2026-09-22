<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli'){http_response_code(404);exit;}
define('YIKAI_CONFIG_PARSER_ONLY',true);
require __DIR__.'/phpstudy-transfer.php';
// 同步已装站点的数据库连接配置：改 root 密码（password）或改 MySQL 端口（port + oldPort）。
// 只处理能确认目标一致的站点：driver=mysql、用户 root、库名与面板记录一致、host 是本机、且记录的端口等于 oldPort。
// 其余站点放进 manual，由界面提示手动检查。改前按文件哈希备份到 backups 下。
$request=json_decode((string)file_get_contents($argv[1]),true,512,JSON_THROW_ON_ERROR);
$settings=json_decode((string)file_get_contents(__DIR__.'/../../config/panel.json'),true,512,JSON_THROW_ON_ERROR);
$engine=(string)$request['engine'];
$settingsPort=(int)$settings[$engine==='mysql57'?'mysql57Port':'mysql80Port'];
$newPort=isset($request['port'])?(int)$request['port']:$settingsPort;
$oldPort=isset($request['oldPort'])?(int)$request['oldPort']:$settingsPort;
$newPassword=array_key_exists('password',$request)?(string)$request['password']:null;
$updated=[];$manual=[];
foreach($settings['sites'] as $site){
    if(($site['database']??'')!==$engine)continue;
    try{
        $source=inspect($site['directory']);
        if($source['configFile']===''||$source['driver']!=='mysql'||$source['user']!=='root'||$source['name']!==$site['databaseName']||!in_array($source['host'],['127.0.0.1','localhost'],true)||$source['port']!==$oldPort){$manual[]=$site['domain'];continue;}
        $file=$site['directory'].'/'.$source['configFile'];$text=(string)file_get_contents($file);$defs=definitions($text);$changes=[];
        // 端口：WordPress 没有独立端口字段，写在 DB_HOST 的 host:port 里；YikaiCMS 用 DB_PORT。
        if($source['port']!==$newPort){
            if($source['kind']==='wordpress'){
                if(!isset($defs['DB_HOST']))throw new RuntimeException('Missing configuration field: DB_HOST');
                $value=$newPort===3306?$source['host']:$source['host'].':'.$newPort;
                $changes[]=[$defs['DB_HOST']['start'],$defs['DB_HOST']['length'],var_export($value,true)];
            }elseif(isset($defs['DB_PORT'])){
                // 写成带引号的字符串：与 CMS 的 config 示例、面板安装预填保持一致（避免出现 int 型 DB_PORT）
                $changes[]=[$defs['DB_PORT']['start'],$defs['DB_PORT']['length'],var_export((string)$newPort,true)];
            }elseif($defs!==[]){
                // 旧版配置没有 DB_PORT：和迁移脚本一样补一行 define
                $changes[]=[min(array_column($defs,'statementStart')),0,"define('DB_PORT', '".$newPort."');\n"];
            }else throw new RuntimeException('Cannot insert DB_PORT');
        }
        // 密码（改 root 密码时使用；只改端口时不带这个字段）
        if($newPassword!==null){
            $key=$source['kind']==='wordpress'?'DB_PASSWORD':'DB_PASS';
            if(!isset($defs[$key]))throw new RuntimeException('Missing configuration field: '.$key);
            if($defs[$key]['value']!==$newPassword)$changes[]=[$defs[$key]['start'],$defs[$key]['length'],var_export($newPassword,true)];
        }
        if($changes===[])continue;
        $backup=$request['backup'].'/'.substr(hash('sha256',$file),0,16).'.php';
        if(!is_dir(dirname($backup))&&!mkdir(dirname($backup),0777,true))throw new RuntimeException('Could not create configuration backup');
        if(!copy($file,$backup))throw new RuntimeException('Could not back up configuration');
        usort($changes,static fn(array $a,array $b):int=>$b[0]<=>$a[0]);
        foreach($changes as [$start,$length,$value])$text=substr_replace($text,$value,$start,$length);
        if(file_put_contents($file,$text,LOCK_EX)!==strlen($text)){copy($backup,$file);throw new RuntimeException('Could not save configuration');}
        $updated[]=$site['domain'];
    }catch(Throwable $error){$manual[]=$site['domain'];}
}
echo json_encode(['updated'=>$updated,'manual'=>array_values(array_unique($manual))],JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);

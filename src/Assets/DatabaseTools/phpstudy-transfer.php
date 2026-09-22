<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli'){http_response_code(404);exit;}
// Parse source configuration as text. Never execute an imported site's PHP.
function definitions(string $text):array{
    $tokens=token_get_all($text);$parts=[];$offset=0;
    foreach($tokens as $token){$value=is_array($token)?$token[1]:$token;if(!is_array($token)||!in_array($token[0],[T_WHITESPACE,T_COMMENT,T_DOC_COMMENT],true))$parts[]=['text'=>$value,'start'=>$offset,'end'=>$offset+strlen($value)];$offset+=strlen($value);}
    $result=[];
    for($i=0;$i<count($parts)-5;$i++){
        if(strtolower($parts[$i]['text'])!=='define'||$parts[$i+1]['text']!=='('||$parts[$i+3]['text']!==',')continue;
        $key=literal($parts[$i+2]['text']);if($key===null)continue;$j=$i+4;$depth=0;
        for(;$j<count($parts);$j++){if($parts[$j]['text']==='(')$depth++;if($parts[$j]['text']===')'){if($depth===0)break;$depth--;}}
        if($j>=count($parts))continue;$start=$parts[$i+4]['start'];$end=$parts[$j]['start'];$raw=trim(substr($text,$start,$end-$start));
        $result[$key]=['statementStart'=>$parts[$i]['start'],'start'=>$start,'length'=>$end-$start,'raw'=>$raw,'value'=>literal($raw)];
    }return $result;
}
function literal(string $text):?string{
    $text=trim($text);if(ctype_digit($text))return $text;
    if(strlen($text)<2||!in_array($text[0],["'",'"'],true)||substr($text,-1)!==$text[0])return null;
    $body=substr($text,1,-1);
    if($text[0]==="'"){if(preg_match("/(?<!\\\\)(?:\\\\\\\\)*'/",$body))return null;return str_replace(["\\'","\\\\"],["'","\\"],$body);}
    if(str_contains($body,'$')||str_contains($body,'"'))return null;return stripcslashes($body);
}
function inspect(string $folder):array{
    $cms=is_file($folder.'/config/config.php')&&is_file($folder.'/includes/init.php');$wp=is_file($folder.'/wp-config.php');$file=$cms?'config/config.php':($wp?'wp-config.php':'');
    $out=['kind'=>$cms?'yikaicms':($wp?'wordpress':'generic'),'configFile'=>$file,'driver'=>'unknown','host'=>'127.0.0.1','port'=>3306,'name'=>'','user'=>'root','password'=>'','sqlitePath'=>''];
    if($file==='')return $out;
    $defs=definitions((string)file_get_contents($folder.'/'.$file));$get=static fn(string $key):?string=>$defs[$key]['value']??null;
    $driver=$cms?($get('DB_DRIVER')??'mysql'):'mysql';
    if($driver==='sqlite'){
        $path=$get('DB_PATH');if($path===null&&preg_match('/^ROOT_PATH\s*\.\s*(.+)$/s',$defs['DB_PATH']['raw']??'',$m))$path=$folder.(literal($m[1])??'');
        if($path!==null&&is_file($path)){$out['driver']='sqlite';$out['sqlitePath']=$path;}return $out;
    }
    $password=$get($cms?'DB_PASS':'DB_PASSWORD');$name=$get('DB_NAME');$user=$get('DB_USER');$host=$get('DB_HOST');
    if($password===null||$name===null||$user===null||$host===null)return $out;
    $port=(int)($get('DB_PORT')??3306);if(preg_match('/^([^:]+):(\d+)$/',$host,$m)){$host=$m[1];$port=(int)$m[2];}
    return array_merge($out,['driver'=>'mysql','host'=>$host==='localhost'?'127.0.0.1':$host,'port'=>$port,'name'=>$name,'user'=>$user,'password'=>$password]);
}
function command(array $args,?string $input,string $output):void{
    $error=tempnam(sys_get_temp_dir(),'ykerr');$proc=proc_open($args,[0=>['file',$input??'NUL','r'],1=>['file',$output,'w'],2=>['file',$error,'w']],$pipes,null,null,['bypass_shell'=>true]);
    if(!is_resource($proc))throw new RuntimeException('Could not start MySQL utility');$code=proc_close($proc);$message=(string)file_get_contents($error);unlink($error);if($code!==0)throw new RuntimeException(trim($message));
}
function clientFile(array $db,string $path):void{
    $quote=static fn(mixed $s):string=>'"'.str_replace(["\\",'"',"\n","\r"],["\\\\",'\\"','\\n','\\r'],(string)$s).'"';
    file_put_contents($path,"[client]\nhost=".$quote($db['host'])."\nport=".(int)$db['port']."\nuser=".$quote($db['user'])."\npassword=".$quote($db['password'])."\ndefault-character-set=utf8mb4\n");
}
if(defined('YIKAI_CONFIG_PARSER_ONLY'))return;
require __DIR__.'/database-transfer.php';
try{
    $request=json_decode((string)file_get_contents($argv[1]),true,512,JSON_THROW_ON_ERROR);
    if($request['action']==='scan-source'){require __DIR__.'/database-scan.php';echo json_encode(scanDatabaseSources($request['folder']),JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);exit;}
    if($request['action']==='inspect'){echo json_encode(array_map('inspect',$request['folders']),JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);exit;}
    $source=$request['source'];$target=$request['target'];$folder=$request['folder'];$root=$request['root'];$created=false;$pdo=null;$temporary=[];$summary=['tables'=>0,'rows'=>0,'verified'=>false];
    if(!preg_match('/^yk_import_[a-f0-9]{16}$/D',$target['name']))throw new RuntimeException('Invalid import database name');
    if($request['action']==='rollback'){
        if($source['driver']==='mysql')databaseConnection($target)->exec('DROP DATABASE IF EXISTS '.identifier($target['name']));
        echo json_encode(['ok'=>true]);exit;
    }
    if($request['action']!=='transfer')throw new RuntimeException('Unknown transfer action');
    try{
        if($source['driver']==='mysql'){
            $summary=copyMysqlDatabase($source,$target,$root,$temporary,$created,$pdo);
        }elseif($source['driver']==='sqlite'){
            $destination=$folder.'/storage/database.sqlite';if(!is_dir(dirname($destination)))mkdir(dirname($destination),0777,true);
            foreach(['','-wal','-shm'] as $suffix)if(is_file($destination.$suffix))unlink($destination.$suffix);
            transferProgress('import');$src=new SQLite3($source['sqlitePath'],SQLITE3_OPEN_READONLY);$src->enableExceptions(true);$dst=new SQLite3($destination);$dst->enableExceptions(true);
            try{
                $src->exec('BEGIN');$before=sqliteInventory($src);if(!$src->backup($dst))throw new RuntimeException('SQLite backup failed');
                transferProgress('verify');$after=sqliteInventory($dst);if($before!==$after||$dst->querySingle('PRAGMA integrity_check')!=='ok')throw new RuntimeException('SQLite verification failed');
                $summary=['tables'=>count($after['tables']),'rows'=>array_sum($after['tables']),'views'=>count($after['views']),'triggers'=>count($after['triggers']),'verified'=>true];
            }finally{$dst->close();$src->close();}
        }elseif($source['driver']!=='none')throw new RuntimeException('Choose a database type first');
        transferProgress('configure');
        if($source['configFile']!==''&&in_array($source['kind'],['yikaicms','wordpress'],true)){
            $file=$folder.'/'.$source['configFile'];$text=(string)file_get_contents($file);$defs=definitions($text);$changes=[];
            if($source['driver']==='mysql'){$values=['DB_HOST'=>$source['kind']==='wordpress'?'127.0.0.1:'.$target['port']:'127.0.0.1','DB_PORT'=>(string)$target['port'],'DB_NAME'=>$target['name'],'DB_USER'=>$target['user'],($source['kind']==='wordpress'?'DB_PASSWORD':'DB_PASS')=>$target['password']];}
            elseif($source['driver']==='sqlite')$values=['DB_PATH'=>$request['finalDirectory'].'/storage/database.sqlite'];else $values=[];
            if($source['kind']==='yikaicms' && $source['driver']!=='none')$values['DB_DRIVER']=$source['driver'];
            $prefix='';
            foreach($values as $key=>$value){if(isset($defs[$key]))$changes[]=[$defs[$key]['start'],$defs[$key]['length'],var_export($value,true)];elseif($source['kind']==='yikaicms'&&in_array($key,['DB_PORT','DB_DRIVER'],true))$prefix.="define(".var_export($key,true).", ".var_export($value,true).");\n";elseif($key!=='DB_PORT')throw new RuntimeException('Missing configuration field: '.$key);}
            if($prefix!==''){if($defs===[])throw new RuntimeException('Cannot insert database configuration');$changes[]=[min(array_column($defs,'statementStart')),0,$prefix];}
            usort($changes,static fn(array $a,array $b):int=>$b[0]<=>$a[0]);foreach($changes as [$start,$length,$value])$text=substr_replace($text,$value,$start,$length);if(file_put_contents($file,$text)===false)throw new RuntimeException('Cannot save copied database configuration');
        }
        echo json_encode(['ok'=>true,'manualConfig'=>$source['kind']==='generic'&&$source['driver']!=='none','database'=>$summary],JSON_THROW_ON_ERROR);
    }catch(Throwable $error){if($created&&$pdo)$pdo->exec('DROP DATABASE `'.$target['name'].'`');throw $error;}
    finally{foreach($temporary as $file)if(is_file($file))unlink($file);}
}catch(Throwable $error){fwrite(STDERR,$error->getMessage());exit(1);}

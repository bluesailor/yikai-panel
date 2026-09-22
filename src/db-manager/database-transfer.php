<?php
declare(strict_types=1);

function transferProgress(string $phase):void{
    fwrite(STDERR,'YIKAI_PROGRESS '.json_encode(['phase'=>$phase],JSON_THROW_ON_ERROR)."\n");
}
function databaseConnection(array $db):PDO{
    return new PDO('mysql:host='.$db['host'].';port='.(int)$db['port'].';charset=utf8mb4',$db['user'],$db['password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_TIMEOUT=>8]);
}
function identifier(string $name):string{return '`'.str_replace('`','``',$name).'`';}
function databaseInventory(PDO $pdo,string $name):array{
    $out=['tables'=>[],'views'=>[],'triggers'=>[],'routines'=>[],'events'=>[]];
    $query=$pdo->prepare('SELECT TABLE_NAME,TABLE_TYPE FROM information_schema.tables WHERE TABLE_SCHEMA=? ORDER BY TABLE_NAME');$query->execute([$name]);
    foreach($query->fetchAll(PDO::FETCH_ASSOC) as $row){
        if($row['TABLE_TYPE']==='VIEW')$out['views'][]=$row['TABLE_NAME'];
        else $out['tables'][$row['TABLE_NAME']]=(int)$pdo->query('SELECT COUNT(*) FROM '.identifier($name).'.'.identifier($row['TABLE_NAME']))->fetchColumn();
    }
    foreach([
        'triggers'=>['TRIGGER_NAME','TRIGGERS','TRIGGER_SCHEMA'],
        'routines'=>["CONCAT(ROUTINE_TYPE,':',ROUTINE_NAME)",'ROUTINES','ROUTINE_SCHEMA'],
        'events'=>['EVENT_NAME','EVENTS','EVENT_SCHEMA'],
    ] as $kind=>[$column,$table,$schema]){
        $query=$pdo->prepare('SELECT '.$column.' FROM information_schema.'.$table.' WHERE '.$schema.'=? ORDER BY 1');$query->execute([$name]);$out[$kind]=$query->fetchAll(PDO::FETCH_COLUMN);
    }
    return $out;
}
// Change SQL identifiers and object ownership, while preserving string values and ordinary comments.
function relocatedSql(string $sql,string $source,string $target,bool $modernTarget=false):string{
    $pattern=<<<'REGEX'
~'(?:''|\\.|[^'\\])*'|"(?:""|\\.|[^"\\])*"|`(?:``|[^`])*`|--[^\r\n]*|\#[^\r\n]*|/\*(?!\!)[\s\S]*?\*/|[a-zA-Z_\x80-\xff][a-zA-Z0-9_$\x80-\xff]*|\s+|.~s
REGEX;
    if(preg_match_all($pattern,$sql,$matches)===false)throw new RuntimeException('Cannot parse database schema');$tokens=$matches[0];$count=count($tokens);
    $next=static function(int $i)use(&$tokens,$count):int{while($i<$count&&trim($tokens[$i])==='')$i++;return $i;};
    for($i=0;$i<$count;$i++){
        $token=$tokens[$i];$name=str_starts_with($token,'`')?str_replace('``','`',substr($token,1,-1)):$token;
        if($modernTarget&&strcasecmp($name,'sql_mode')===0){
            $previous=$i-1;while($previous>=0&&trim($tokens[$previous])==='')$previous--;
            $equals=$next($i+1);$value=$next($equals+1);
            if($previous>=0&&strcasecmp($tokens[$previous],'SET')===0&&($tokens[$equals]??'')==='='&&isset($tokens[$value])&&str_starts_with($tokens[$value],"'")){
                $modes=explode(',',substr($tokens[$value],1,-1));$modes=array_values(array_filter($modes,static fn(string $mode):bool=>strcasecmp($mode,'NO_AUTO_CREATE_USER')!==0));$tokens[$value]="'".implode(',',$modes)."'";
            }
        }
        if(strcasecmp($name,$source)===0&&($tokens[$next($i+1)]??'')==='.'){
            $tokens[$i]=identifier($target);continue;
        }
        if(strcasecmp($token,'DEFINER')!==0)continue;
        $equals=$next($i+1);$user=$next($equals+1);$at=$next($user+1);$host=$next($at+1);
        if(($tokens[$equals]??'')!=='='||($tokens[$at]??'')!=='@'||$host>=$count)continue;
        $tokens[$i]='DEFINER=CURRENT_USER';for($j=$i+1;$j<=$host;$j++)$tokens[$j]='';$i=$host;
    }
    return implode('',$tokens);
}
function relocateDump(string $input,string $output,string $source,string $target,bool $modernTarget):void{
    $in=fopen($input,'rb');$out=fopen($output,'wb');if(!$in||!$out)throw new RuntimeException('Cannot open database dump');
    $ddl='';$delimiter=';';
    try{
        while(($line=fgets($in))!==false){
            if(str_starts_with($line,'DELIMITER '))$delimiter=trim(substr($line,10));
            // mysqldump writes data as escaped, complete INSERT lines; never parse application data as SQL identifiers.
            if($delimiter===';'&&str_starts_with($line,'INSERT INTO ')){
                if($ddl!==''){fwrite($out,relocatedSql($ddl,$source,$target,$modernTarget));$ddl='';}fwrite($out,$line);
            }else $ddl.=$line;
        }
        if(!feof($in))throw new RuntimeException('Cannot read database dump');
        if($ddl!=='')fwrite($out,relocatedSql($ddl,$source,$target,$modernTarget));
        if(!fflush($out))throw new RuntimeException('Cannot flush database dump');
    }finally{fclose($in);fclose($out);}
}
function copyMysqlDatabase(array $source,array $target,string $root,array &$temporary,bool &$created,?PDO &$pdo):array{
    transferProgress('connect');$src=databaseConnection($source);$pdo=databaseConnection($target);
    $sourceVersion=(string)$src->query('SELECT VERSION()')->fetchColumn();$targetVersion=(string)$pdo->query('SELECT VERSION()')->fetchColumn();
    if((int)$sourceVersion>=8&&(int)$targetVersion<8)throw new RuntimeException('MySQL 8 cannot be downgraded to 5.7. Select MySQL 8.0 as the target.');
    $check=$pdo->prepare('SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name=?');$check->execute([$target['name']]);if($check->fetchColumn())throw new RuntimeException('Target database already exists');
    $check=$src->prepare('SELECT DEFAULT_CHARACTER_SET_NAME,DEFAULT_COLLATION_NAME FROM information_schema.schemata WHERE schema_name=?');$check->execute([$source['name']]);$schema=$check->fetch(PDO::FETCH_ASSOC);if(!$schema)throw new RuntimeException('Source database does not exist');
    $before=databaseInventory($src,$source['name']);
    $srcIni=tempnam(sys_get_temp_dir(),'yksrc');$dstIni=tempnam(sys_get_temp_dir(),'ykdst');$dump=tempnam(sys_get_temp_dir(),'yksql');$relocated=tempnam(sys_get_temp_dir(),'yknew');$temporary=[$srcIni,$dstIni,$dump,$relocated];clientFile($source,$srcIni);clientFile($target,$dstIni);
    transferProgress('export');
    command([$root.'/soft/mysql/8.0/bin/mysqldump.exe','--defaults-file='.$srcIni,'--single-transaction','--quick','--hex-blob','--routines','--events','--triggers','--no-tablespaces','--column-statistics=0','--set-gtid-purged=OFF','--max-allowed-packet=1G','--',$source['name']],null,$dump);
    if($before!==databaseInventory($src,$source['name']))throw new RuntimeException('Source database changed during export. Pause writes to the source project and retry.');
    relocateDump($dump,$relocated,$source['name'],$target['name'],(int)$targetVersion>=8);
    $pdo->exec('CREATE DATABASE '.identifier($target['name']).' CHARACTER SET '.identifier($schema['DEFAULT_CHARACTER_SET_NAME']).' COLLATE '.identifier($schema['DEFAULT_COLLATION_NAME']));$created=true;
    transferProgress('import');
    command([$root.'/soft/mysql/'.($target['engine']==='mysql57'?'5.7':'8.0').'/bin/mysql.exe','--defaults-file='.$dstIni,'--binary-mode','--max-allowed-packet=1G','--',$target['name']],$relocated,'NUL');
    transferProgress('verify');$after=databaseInventory($pdo,$target['name']);
    if($before!==$after)throw new RuntimeException('Database verification failed: table rows or database objects differ.');
    foreach($after['views'] as $view)$pdo->query('SELECT * FROM '.identifier($target['name']).'.'.identifier($view).' LIMIT 0');
    return ['tables'=>count($after['tables']),'rows'=>array_sum($after['tables']),'views'=>count($after['views']),'triggers'=>count($after['triggers']),'routines'=>count($after['routines']),'events'=>count($after['events']),'verified'=>true];
}
function sqliteInventory(SQLite3 $db):array{
    $out=['tables'=>[],'views'=>[],'triggers'=>[]];$objects=$db->query("SELECT name,type FROM sqlite_master WHERE type IN ('table','view','trigger') AND name NOT LIKE 'sqlite_%' ORDER BY name");
    while($row=$objects->fetchArray(SQLITE3_ASSOC)){
        if($row['type']==='table')$out['tables'][$row['name']]=(int)$db->querySingle('SELECT COUNT(*) FROM '.identifier($row['name']));
        else $out[$row['type']==='view'?'views':'triggers'][]=$row['name'];
    }return $out;
}

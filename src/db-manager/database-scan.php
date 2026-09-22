<?php
declare(strict_types=1);

// This file is loaded only by the CLI transfer helper. Source PHP is never included.
function scanKey(string $key): ?string {
    $key = strtolower(str_replace(['_', '.', '-'], '', ltrim($key, '$')));
    return match ($key) {
        'dbhost','databasehost','databasehostname','zcmysqlserver','hostname','host','cfgdbhost','servername','dbserver' => 'host',
        'dbport','databaseport','zcmysqlport','hostport','port','cfgdbport' => 'port',
        'dbname','dbdatabase','databasedatabase','zcmysqlname','databasename','database','cfgdbname' => 'name',
        'dbuser','dbusername','zcmysqlusername','databaseusername','username','user','cfgdbuser' => 'user',
        'dbpass','dbpassword','dbpw','zcmysqlpassword','databasepassword','password','passwd','cfgdbpwd','dbpwd' => 'password',
        'dbconnection','dbdriver','dbtype','databasetype','driver','type' => 'driver',
        'dbpath','sqlitepath' => 'sqlitePath',
        default => null,
    };
}

function scanEnv(string $text): array {
    $out = [];
    foreach (preg_split('/\R/', $text) as $line) {
        if (!preg_match('/^\s*(?:export\s+)?([A-Za-z_][\w.]*)\s*=\s*(.*?)\s*$/', $line, $m)) continue;
        $raw = $m[2];
        if ($raw !== '' && ($raw[0] === '"' || $raw[0] === "'")) $value = literal($raw);
        else $value = preg_replace('/\s+#.*$/', '', $raw);
        if ($value !== null && !str_contains($value, '${')) $out[$m[1]] = $value;
    }
    return $out;
}

function scanExpression(string $raw, array $env): ?string {
    $value = literal($raw);
    if ($value !== null) return $value;
    if (preg_match('/^(?:env|getenv)\s*\(\s*([\'"][^\'"]+[\'"])\s*(?:,\s*(.*))?\)$/s', trim($raw), $m)) {
        $key = literal($m[1]);
        return $env[$key] ?? (isset($m[2]) ? literal($m[2]) : null);
    }
    return null;
}

function scanPhp(string $text, array $env): array {
    $tokens = [];
    foreach (token_get_all($text) as $token) {
        if (is_array($token) && in_array($token[0], [T_WHITESPACE,T_COMMENT,T_DOC_COMMENT,T_INLINE_HTML,T_OPEN_TAG,T_CLOSE_TAG], true)) continue;
        $tokens[] = is_array($token) ? $token[1] : $token;
    }
    $fields = [];
    $add = static function(string $key, ?string $value) use (&$fields): void {
        $canonical = scanKey($key);
        if ($canonical !== null) $fields[$canonical][] = $value;
    };
    foreach (definitions($text) as $key => $definition) $add($key, scanExpression($definition['raw'], $env));
    for ($i=0; $i<count($tokens)-2; $i++) {
        $key = literal($tokens[$i]);
        if (str_starts_with($tokens[$i], '$')) $key = substr($tokens[$i], 1);
        $next = $i+1;
        // Also accept $config['db_host'] = 'localhost'.
        if (($tokens[$next] ?? '') === ']' && ($tokens[$next+1] ?? '') === '=') $next++;
        if ($key === null || !in_array($tokens[$next] ?? '', ['=', '=>'], true) || scanKey($key) === null) continue;
        $raw=''; $depth=0;
        for ($j=$next+1; $j<count($tokens); $j++) {
            $part=$tokens[$j];
            if ($depth===0 && in_array($part, [',',';',']',')'], true)) break;
            if (in_array($part,['(','['],true)) $depth++;
            if (in_array($part,[')',']'],true)) $depth--;
            $raw.=$part;
        }
        $add($key, scanExpression($raw, $env));
    }
    return $fields;
}

function scanDatabaseSources(string $folder): array {
    $root=realpath($folder);
    if ($root===false || !is_dir($root) || is_link($folder)) throw new RuntimeException('Source folder is unavailable.');
    $queue=[[$root,0]]; $files=[]; $visited=0; $skipped=0; $limited=false;
    while ($queue) {
        [$dir,$depth]=array_shift($queue);
        foreach (@scandir($dir) ?: [] as $name) {
            if ($name==='.' || $name==='..') continue;
            if (++$visited>20000) {$limited=true; break 2;}
            $path=$dir.'/'.$name;
            if (is_link($path)) continue;
            $resolved=realpath($path); if ($resolved===false || !str_starts_with(strtolower(str_replace('\\','/',$resolved)),strtolower(str_replace('\\','/',$root)).'/')) continue;
            if (is_dir($path)) {
                if (in_array(strtolower($name),['.git','.svn','vendor','node_modules','uploads','cache','logs','backups','backup'],true)) continue;
                if ($depth<6) $queue[]=[$path,$depth+1]; else $limited=true;
                continue;
            }
            $lower=strtolower($name);
            if ($lower!=='.env' && !preg_match('/(?:conn|config|database|settings|common).*\.php$/i',$name)) continue;
            if (preg_match('/(?:sample|example|backup|\.bak|\.old|\.dist)/i',$name)) continue;
            if (filesize($path)>1048576) {$skipped++;continue;}
            $files[]=$path;
            if (count($files)>=300) {$limited=true;break 2;}
        }
    }
    $matches=[]; $known=inspect($root);
    foreach ($files as $file) {
        $relative=str_replace('\\','/',substr($file,strlen($root)+1));
        $env=[]; $dir=dirname($file);
        while (true) {
            if (is_file($dir.'/.env') && !is_link($dir.'/.env') && filesize($dir.'/.env')<=1048576) $env+=scanEnv((string)file_get_contents($dir.'/.env'));
            if ($dir===$root || dirname($dir)===$dir) break;
            $dir=dirname($dir);
        }
        if (basename($file)==='.env') {
            $fields=[];
            foreach (scanEnv((string)file_get_contents($file)) as $key=>$value) if (($canonical=scanKey($key))!==null) $fields[$canonical][]=$value;
        } else $fields=scanPhp((string)file_get_contents($file),$env);
        $values=[]; $ambiguous=false;
        foreach ($fields as $key=>$items) {
            if (in_array(null,$items,true) || count(array_unique($items))!==1) {$ambiguous=true;break;}
            $values[$key]=$items[0];
        }
        if ($ambiguous) {$skipped++;continue;}
        $out=['kind'=>'generic','configFile'=>$relative,'driver'=>'unknown','host'=>'127.0.0.1','port'=>3306,'name'=>'','user'=>'root','password'=>'','sqlitePath'=>''];
        $driver=strtolower($values['driver']??'mysql');
        if ($driver==='sqlite') {
            $path=$values['sqlitePath']??$values['name']??'';
            if ($path==='' || $path===':memory:') continue;
            if (!preg_match('/^(?:[A-Za-z]:[\\\\\/]|\/)/',$path)) $path=$root.'/'.$path;
            if (!is_file($path)) {$skipped++;continue;}
            $out['driver']='sqlite';$out['sqlitePath']=$path;
        } elseif (in_array($driver,['mysql','mysqli','pdo_mysql'],true)) {
            if (!isset($values['host'],$values['name'],$values['user'],$values['password']) || $values['name']==='') continue;
            foreach (['host','name','user','password'] as $key) $out[$key]=$values[$key];
            if (isset($values['port']) && (!ctype_digit($values['port']) || (int)$values['port']<1 || (int)$values['port']>65535)) {$skipped++;continue;}
            $out['port']=(int)($values['port']??3306);
            if (preg_match('/^([^:]+):(\d+)$/',$out['host'],$m)) {$out['host']=$m[1];$out['port']=(int)$m[2];}
            $out['driver']='mysql';
        } else continue;
        if ($known['driver']!=='unknown' && strcasecmp($known['configFile'],$relative)===0) $out=$known;
        $matches[]=$out;
    }
    if ($known['driver']!=='unknown' && !array_filter($matches,static fn(array $m):bool=>$m['configFile']===$known['configFile'])) $matches[]=$known;
    return ['matches'=>$matches,'files'=>count($files),'skipped'=>$skipped,'limited'=>$limited];
}

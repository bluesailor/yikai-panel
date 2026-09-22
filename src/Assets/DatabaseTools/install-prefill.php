<?php
declare(strict_types=1);
// Only prefill the local install form; all installer validation remains in CMS.
if(PHP_SAPI==='cli')return;
$script=str_replace('\\','/',(string)($_SERVER['SCRIPT_FILENAME']??''));
if(!str_ends_with($script,'/install/index.php'))return;
$panel=json_decode((string)file_get_contents(__DIR__.'/../../config/panel.json'),true);
foreach($panel['sites']??[] as $localSite){
    if(strtolower(str_replace('\\','/',$localSite['directory']).'/install/index.php')!==strtolower($script))continue;
    if(is_file($localSite['directory'].'/installed.lock'))return;
    ob_start(static function(string $html)use($localSite,$panel):string{
        $values=['db_host'=>'127.0.0.1','db_port'=>(string)$panel[$localSite['database']==='mysql57'?'mysql57Port':'mysql80Port'],'db_name'=>$localSite['databaseName'],'db_user'=>(($localSite['databaseUser']??'')!==''?$localSite['databaseUser']:$panel['mysqlUser']),'db_pass'=>(($localSite['databaseUser']??'')!==''?(string)($localSite['databasePassword']??''):($panel[$localSite['database']==='mysql57'?'mysql57Password':'mysql80Password']??$panel['mysqlPassword'])),'admin_user'=>'admin','admin_pass'=>'yikai888'];
        foreach($values as $name=>$value){
            $html=(string)preg_replace_callback('/<input\b[^>]*\bname="'.preg_quote($name,'/').'"[^>]*>/i',static function(array $match)use($value):string{
                $tag=(string)preg_replace('/\svalue="[^"]*"/i','',$match[0]);
                return substr($tag,0,-1).' value="'.htmlspecialchars($value,ENT_QUOTES,'UTF-8').'">';
            },$html);
        }
        // Keep the local quick-install account consistent with the guided installer.
        $html=str_replace("var pass = 'Yk' + Math.random().toString(36).slice(2, 8) + Math.floor(Math.random() * 90 + 10);", "var pass = 'yikai888';", $html);
        $driver=$localSite['database']==='sqlite'?'sqlite':'mysql';
        return (string)preg_replace_callback('/<input\b[^>]*\bname="db_driver"[^>]*>/i',static function(array $match)use($driver):string{
            $tag=(string)preg_replace('/\schecked(?:="[^"]*")?/i','',$match[0]);
            return str_contains($tag,'value="'.$driver.'"')?substr($tag,0,-1).' checked>':$tag;
        },$html);
    });
    break;
}

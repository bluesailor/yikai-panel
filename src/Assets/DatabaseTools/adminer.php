<?php
declare(strict_types=1);
define('YIKAI_ADMINER',true);
require __DIR__.'/common.php';
$chosen=site();
$_COOKIE['adminer_lang']=$language;
$driver=$chosen['database']==='sqlite'?'sqlite':'server';
$server=$driver==='sqlite'?'':'127.0.0.1:'.dbPort($chosen);
$database=$driver==='sqlite'?sqlitePath($chosen):$chosen['databaseName'];
$automatic=!isset($_GET['username'])&&$_SERVER['REQUEST_METHOD']==='GET';
if($automatic) $_POST=['auth'=>['driver'=>$driver,'server'=>$server,'username'=>'root','password'=>dbPassword($chosen),'db'=>$database]];
function adminer_object(): object {
    return new class extends Adminer\Adminer {
        function name(){return '<a href="'.h(linkTo()).'">Yikai · Adminer</a>';}
        function credentials(){global $server,$config,$chosen;return [$server,$config['mysqlUser'],dbPassword($chosen)];}
        function database(){global $database;return $database;}
        function login($login,$password){return true;}
        function verifyLoginToken(){global $automatic;return !$automatic;}
        function verifyVersion(){return false;}
        function serviceWorker(){}
        function pluginsLinks(){echo '<p><a href="'.h(linkTo()).'">'.h(t('back')).'</a></p>';}
    };
}
require __DIR__.'/vendor/adminer.php';

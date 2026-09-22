<?php
declare(strict_types=1);
require __DIR__.'/common.php';
// 还没有项目（最小包首次安装就是这样）或链接里的项目已经不在配置里：给一页说明，不要抛 500。
// 判定方式跟 common.php 的 site() 保持一致：没带 site 参数时用第一个项目。
// 文案直接写在文件里，不依赖 lang.php：老安装的语言表里没有新键，t() 会把键名原样显示出来。
$requestedSite = (string)($_GET['site'] ?? ($context[0] !== '' ? $context[0] : ($config['sites'][0]['id'] ?? '')));
$knownSite = $requestedSite !== '' && (bool)array_filter($config['sites'], static fn(array $item): bool => $item['id'] === $requestedSite);
if (!$knownSite) {
    $sitesEmpty = !$config['sites'];
    $copy = [
        'zh' => $sitesEmpty
            ? ['title'=>'数据库管理', 'head'=>'还没有项目', 'body'=>'先在易开面板里新建项目（或“新建 / 接入项目 → 扫描目录快速导入”），再回到这里管理它的数据库。']
            : ['title'=>'数据库管理', 'head'=>'找不到这个项目', 'body'=>'它可能已经被移除。回到易开面板的项目列表，从那里打开数据库页面。'],
        'en' => $sitesEmpty
            ? ['title'=>'Database manager', 'head'=>'No projects yet', 'body'=>'Create a project in Yikai Panel (or use “Add project → Scan a folder”) and come back to manage its database.']
            : ['title'=>'Database manager', 'head'=>'Project not found', 'body'=>'It may have been removed. Open the database page from the project list in Yikai Panel.'],
        'ja' => $sitesEmpty
            ? ['title'=>'データベース管理', 'head'=>'プロジェクトがありません', 'body'=>'Yikai パネルでプロジェクトを作成（または「スキャンして一括追加」）してから、ここでデータベースを管理してください。']
            : ['title'=>'データベース管理', 'head'=>'プロジェクトが見つかりません', 'body'=>'削除された可能性があります。パネルのプロジェクト一覧から開いてください。'],
    ];
    $text = $copy[$language] ?? $copy['zh'];
    http_response_code(200);
    ?><!doctype html><html lang="<?= h($language) ?>"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title><?= h($text['title']) ?></title><link rel="stylesheet" href="style.css"></head><body>
<header><div class="brand"><span class="mark">Y</span><div><strong><?= h(t('title')) ?></strong><small><?= h(t('subtitle')) ?></small></div></div><nav aria-label="<?= h(t('language')) ?>"><?php foreach(['zh'=>'简体中文','en'=>'English','ja'=>'日本語'] as $code=>$label):?><a class="<?= $language===$code?'active':'' ?>" href="?<?= h(http_build_query(['lang'=>$code])) ?>"><?= h($label) ?></a><?php endforeach?></nav></header>
<main><section class="overview"><div><span class="eyebrow"><?= h($text['head']) ?></span><h1><?= h($text['head']) ?></h1></div></section><div class="notice"><strong><?= h($text['body']) ?></strong></div></main>
<footer>Yikai Panel · <?= h(t('note')) ?></footer></body></html><?php
    exit;
}
$chosen=site();$tables=[];$error=null;
try {
    $pdo=connectDatabase($chosen);
    if($chosen['database']==='sqlite') {
        foreach($pdo->query("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name") as $row){$tables[]=['Name'=>$row['name'],'Rows'=>'—'];}
    }else{$tables=$pdo->query('SHOW TABLE STATUS')->fetchAll(PDO::FETCH_ASSOC);}
}catch(Throwable $exception){$error=$exception->getMessage();}
$backups=glob(backupDirectory($chosen).'/*.{sql,sqlite}',GLOB_BRACE)?:[];rsort($backups);$backups=array_slice($backups,0,10);
?>
<!doctype html><html lang="<?= h($language) ?>"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title><?= h(t('title')) ?></title><link rel="stylesheet" href="style.css"></head><body>
<header><div class="brand"><span class="mark">Y</span><div><strong><?= h(t('title')) ?></strong><small><?= h(t('subtitle')) ?></small></div></div><nav aria-label="<?= h(t('language')) ?>"><?php foreach(['zh'=>'简体中文','en'=>'English','ja'=>'日本語'] as $code=>$label):?><a class="<?= $language===$code?'active':'' ?>" href="?<?= h(http_build_query(['site'=>$chosen['id'],'lang'=>$code])) ?>"><?= h($label) ?></a><?php endforeach?></nav></header>
<main><section class="overview"><div><span class="eyebrow"><?= h(t('site')) ?></span><h1><?= h($chosen['domain']) ?></h1><span class="pill <?= $error?'offline':'' ?>"><?= h(t($error?'offline':'ready')) ?></span> <span class="subtle"><?= h($chosen['database']==='sqlite'?'SQLite':($chosen['database']==='mysql57'?'MySQL 5.7':'MySQL 8.0')) ?></span></div><form><input type="hidden" name="lang" value="<?= h($language) ?>"><label for="site"><?= h(t('site')) ?></label><select id="site" name="site" onchange="this.form.submit()"><?php foreach($config['sites'] as $item):?><option value="<?= h($item['id']) ?>" <?= $item['id']===$chosen['id']?'selected':'' ?>><?= h($item['domain']) ?></option><?php endforeach?></select></form></section>
<?php if($error):?><div class="notice error"><strong><?= h(t('start')) ?></strong><details><summary><?= h(t('connection')) ?></summary><?= h($error) ?></details></div><?php endif?>
<div id="result" role="status" aria-live="polite" hidden></div>
<section class="cards">
<article><span class="icon">↓</span><h2><?= h(t('backup')) ?></h2><p><?= h(t('backup_desc')) ?></p><form class="action-form" method="post" action="<?= h(linkTo('action.php')) ?>"><input type="hidden" name="csrf" value="<?= h($_SESSION['csrf']) ?>"><input type="hidden" name="action" value="backup"><button class="primary" <?= $error?'disabled':'' ?>><?= h(t('create')) ?></button></form></article>
<article><span class="icon">↑</span><h2><?= h(t('restore')) ?></h2><p><?= h(t('restore_desc')) ?></p><button onclick="document.getElementById('restore').showModal()" <?= $error?'disabled':'' ?>><?= h(t('choose')) ?></button></article>
<article><span class="icon">▦</span><h2><?= h(t('browse')) ?></h2><p><?= h(t('browse_desc')) ?></p><a class="button" href="<?= h(linkTo('adminer.php')) ?>"><?= h(t('open')) ?></a></article>
<article><span class="icon code">SQL</span><h2><?= h(t('sql')) ?></h2><p><?= h(t('sql_desc')) ?></p><a class="button" href="<?= h(linkTo('adminer.php',['sql'=>''])) ?>"><?= h(t('open')) ?></a></article>
</section>
<section class="columns"><article class="table-card"><div class="section-head"><h2><?= h(t('tables')) ?> <span class="count"><?= count($tables) ?></span></h2><a href="<?= h(linkTo('adminer.php')) ?>"><?= h(t('advanced')) ?> ↗</a></div><?php if(!$tables):?><p class="empty"><?= h(t('no_tables')) ?></p><?php else:?><table><thead><tr><th><?= h(t('name')) ?></th><th><?= h(t('rows')) ?></th><th></th></tr></thead><tbody><?php foreach($tables as $table):?><tr><td><?= h($table['Name']) ?></td><td><?= h($table['Rows']??'—') ?></td><td><a href="<?= h(linkTo('adminer.php',['select'=>$table['Name']])) ?>"><?= h(t('open')) ?></a></td></tr><?php endforeach?></tbody></table><?php endif?></article>
<aside><article><h2><?= h(t('connection')) ?></h2><dl><dt><?= h(t('database')) ?></dt><dd><?= h($chosen['database']==='sqlite'?basename(sqlitePath($chosen)):$chosen['databaseName']) ?></dd><?php if($chosen['database']!=='sqlite'):?><dt>Host</dt><dd>127.0.0.1:<?= dbPort($chosen) ?></dd><dt><?= h(t('credentials')) ?></dt><dd><?= h(($chosen['databaseUser']??'')!==''?$chosen['databaseUser']:$config['mysqlUser']) ?> / <?= h(($chosen['databaseUser']??'')!==''?(string)($chosen['databasePassword']??''):dbPassword($chosen)) ?></dd><?php endif?></dl></article><article><h2><?= h(t('recent')) ?></h2><?php if(!$backups):?><p class="subtle"><?= h(t('empty')) ?></p><?php endif?><?php foreach($backups as $file):?><div class="backup"><span><?= h(basename($file)) ?><small><?= number_format(filesize($file)/1024,1) ?> KB</small></span><a href="<?= h(linkTo('action.php',['download'=>basename($file)])) ?>"><?= h(t('download')) ?></a></div><?php endforeach?></article></aside></section>
<footer>Yikai Panel · <?= h(t('note')) ?></footer></main>
<dialog id="restore"><form class="action-form" method="post" enctype="multipart/form-data" action="<?= h(linkTo('action.php')) ?>" data-restore="true"><h2><?= h(t('restore')) ?></h2><p><?= h(t('backup_first')) ?></p><input type="hidden" name="csrf" value="<?= h($_SESSION['csrf']) ?>"><input type="hidden" name="action" value="restore"><label for="backup"><?= h(t('restore_hint')) ?></label><input id="backup" type="file" name="backup" accept="<?= $chosen['database']==='sqlite'?'.sqlite,.db,.sqlite3':'.sql' ?>" required><button class="primary"><?= h(t('import')) ?></button><button type="button" onclick="this.closest('dialog').close()">×</button></form></dialog>
<script>
const strings=<?= json_encode(['busy'=>t('busy'),'confirm'=>t('confirm'),'failed'=>t('failed'),'refresh'=>t('refresh'),'large'=>t('large')],JSON_HEX_TAG|JSON_HEX_AMP|JSON_HEX_APOS|JSON_HEX_QUOT) ?>;
document.querySelectorAll('.action-form').forEach(form=>form.addEventListener('submit',async event=>{event.preventDefault();if(form.dataset.restore&&!confirm(strings.confirm))return;const file=form.querySelector('input[type=file]')?.files[0];if(file&&file.size>128*1024*1024){alert(strings.large);return;}const buttons=[...form.querySelectorAll('button')];buttons.forEach(b=>b.disabled=true);const result=document.getElementById('result');result.hidden=false;result.className='notice';result.textContent=strings.busy;try{const response=await fetch(form.getAttribute('action'),{method:'POST',body:new FormData(form)});const data=await response.json();if(!data.ok)throw new Error(data.message);result.textContent=data.message+' ';const link=document.createElement('a');link.href=location.href;link.textContent=strings.refresh;result.append(link);form.closest('dialog')?.close();}catch(error){result.className='notice error';result.textContent=error.message||strings.failed;}finally{buttons.forEach(b=>b.disabled=false);}}));
</script></body></html>

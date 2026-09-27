<?php
declare(strict_types=1);

// 易开面板：把 YikaiCMS 项目的后台管理员恢复成指定账号密码（默认 admin / admin888）。
// 只能由面板在命令行调用；参数从标准输入读 JSON（密码不进命令行）：
//   {"dir":"D:\\yikai\\wwwroot\\demo.localhost","user":"admin","password":"admin888"}
// 做的事：
//   · 读项目 config/config.php 里的 DB_* 常量（只做文本解析，不执行，避免触发 session 等副作用）；
//   · 该用户名存在：重设密码、启用账号、设为超级管理员（role_id=1）、清掉两步验证；
//     不存在：新建一个超级管理员；
//   · 清空 storage/login_throttle，解除输错密码后的登录锁定。其他管理员不动。
// 输出一行 JSON：{"ok":true,"action":"updated|created"} 或 {"ok":false,"error":"..."}。

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}

function done(array $result): never
{
    echo json_encode($result, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), "\n";
    exit($result['ok'] ? 0 : 1);
}

$input = json_decode((string) stream_get_contents(STDIN), true);
$dir = is_array($input) ? (string) ($input['dir'] ?? '') : '';
$user = is_array($input) ? trim((string) ($input['user'] ?? 'admin')) : '';
$password = is_array($input) ? (string) ($input['password'] ?? '') : '';
if ($dir === '' || !is_dir($dir)) done(['ok' => false, 'error' => 'Project folder not found.']);
if (!preg_match('/^[A-Za-z0-9_.@-]{2,50}$/', $user)) done(['ok' => false, 'error' => 'Invalid admin user name.']);
if (strlen($password) < 6 || strlen($password) > 72) done(['ok' => false, 'error' => 'The password must be 6-72 characters.']);

$configFile = $dir . DIRECTORY_SEPARATOR . 'config' . DIRECTORY_SEPARATOR . 'config.php';
if (!is_file($configFile)) done(['ok' => false, 'error' => 'YikaiCMS is not installed yet (config/config.php is missing).']);
$source = (string) file_get_contents($configFile);

function constantValue(string $source, string $name): ?string
{
    if (preg_match("/define\\(\\s*['\"]" . $name . "['\"]\\s*,\\s*'((?:[^'\\\\]|\\\\.)*)'\\s*\\)/", $source, $m)) {
        return stripcslashes($m[1]);
    }
    if (preg_match("/define\\(\\s*['\"]" . $name . "['\"]\\s*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*\\)/", $source, $m)) {
        return stripcslashes($m[1]);
    }
    return null;
}

$driver = constantValue($source, 'DB_DRIVER') ?? 'mysql';
$prefix = constantValue($source, 'DB_PREFIX') ?? 'yikai_';
if (!preg_match('/^[A-Za-z0-9_]{0,32}$/', $prefix)) done(['ok' => false, 'error' => 'Unsupported table prefix.']);
$table = $prefix . 'users';

try {
    if ($driver === 'sqlite') {
        // DB_PATH 一般写成 ROOT_PATH . '/storage/database.sqlite'；取出相对部分拼到项目目录上
        $path = $dir . '/storage/database.sqlite';
        if (preg_match("/define\\(\\s*['\"]DB_PATH['\"]\\s*,\\s*ROOT_PATH\\s*\\.\\s*'([^']+)'\\s*\\)/", $source, $m)) {
            $path = $dir . $m[1];
        } elseif (($absolute = constantValue($source, 'DB_PATH')) !== null) {
            $path = $absolute;
        }
        if (!is_file($path)) done(['ok' => false, 'error' => 'SQLite database not found: ' . $path]);
        $pdo = new PDO('sqlite:' . $path);
    } else {
        $host = constantValue($source, 'DB_HOST') ?? '127.0.0.1';
        $port = constantValue($source, 'DB_PORT') ?? '3306';
        $name = constantValue($source, 'DB_NAME') ?? '';
        $dsn = 'mysql:host=' . ($host === 'localhost' ? '127.0.0.1' : $host) . ';port=' . $port . ';dbname=' . $name . ';charset=utf8mb4';
        $pdo = new PDO($dsn, constantValue($source, 'DB_USER') ?? '', constantValue($source, 'DB_PASS') ?? '');
    }
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

    $columns = [];
    if ($driver === 'sqlite') {
        foreach ($pdo->query("PRAGMA table_info(\"$table\")") as $row) $columns[] = $row['name'];
    } else {
        foreach ($pdo->query("SHOW COLUMNS FROM `$table`") as $row) $columns[] = $row['Field'];
    }
    if (!in_array('username', $columns, true) || !in_array('password', $columns, true)) {
        done(['ok' => false, 'error' => "Admin table $table was not found."]);
    }
    $quote = fn(string $column): string => $driver === 'sqlite' ? "\"$column\"" : "`$column`";

    $hash = password_hash($password, PASSWORD_BCRYPT);
    $now = time();
    $find = $pdo->prepare('SELECT id FROM ' . $quote($table) . ' WHERE username = ?');
    $find->execute([$user]);
    $id = $find->fetchColumn();
    if ($id !== false) {
        $set = ['password' => $hash];
        foreach (['status' => 1, 'role_id' => 1, 'totp_secret' => '', 'updated_at' => $now] as $column => $value) {
            if (in_array($column, $columns, true)) $set[$column] = $value;
        }
        $sql = 'UPDATE ' . $quote($table) . ' SET ' . implode(', ', array_map(fn($c) => $quote($c) . ' = ?', array_keys($set))) . ' WHERE id = ?';
        $pdo->prepare($sql)->execute([...array_values($set), $id]);
        $action = 'updated';
    } else {
        $row = ['username' => $user, 'password' => $hash];
        foreach (['nickname' => $user, 'email' => '', 'role_id' => 1, 'status' => 1, 'created_at' => $now, 'updated_at' => $now] as $column => $value) {
            if (in_array($column, $columns, true)) $row[$column] = $value;
        }
        $sql = 'INSERT INTO ' . $quote($table) . ' (' . implode(', ', array_map($quote, array_keys($row))) . ') VALUES (' . implode(', ', array_fill(0, count($row), '?')) . ')';
        $pdo->prepare($sql)->execute(array_values($row));
        $action = 'created';
    }
} catch (Throwable $e) {
    done(['ok' => false, 'error' => $e->getMessage()]);
}

// 解除登录锁定：输错次数记录在 storage/login_throttle/<ip>.json
$throttle = $dir . DIRECTORY_SEPARATOR . 'storage' . DIRECTORY_SEPARATOR . 'login_throttle';
$cleared = 0;
if (is_dir($throttle)) {
    foreach (glob($throttle . DIRECTORY_SEPARATOR . '*.json') ?: [] as $file) {
        if (@unlink($file)) $cleared++;
    }
}
done(['ok' => true, 'action' => $action, 'throttleCleared' => $cleared]);

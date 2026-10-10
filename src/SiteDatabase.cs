using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YikaiLocal;

// 项目数据库：建库（bootstrap.php，含 SQLite），以及项目专属 MySQL 账号的创建、授权和登录验证。
public sealed partial class Runtime
{
    // 建库与账号同步每次都要起一次 php.exe 和两次 mysql.exe（约 0.25 秒/项目），几十个项目启动时会逐个排队。
    // 成功后记一个指纹（引擎 + MySQL 实例 server-uuid + 库名 + 账号 + 密码）：指纹没变且库目录还在就跳过。
    // 改了账号密码、MySQL 重新初始化（server-uuid 变）、库被手动删掉（目录没了）都会重新建。
    string DatabaseReadyFile=>Path.Combine(Root,"config","database-ready.json");
    Dictionary<string,string>? databaseReady;
    Dictionary<string,string> DatabaseReady()
    {
        if(databaseReady!=null)return databaseReady;
        try{databaseReady=File.Exists(DatabaseReadyFile)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(DatabaseReadyFile))??[]:[];}
        catch(Exception e) when(e is JsonException or IOException){databaseReady=[];}
        return databaseReady;
    }
    string? DatabaseFingerprint(Site site)
    {
        if(site.Database is not ("mysql80" or "mysql57"))return null;
        var data=Path.Combine(Root,"data",site.Database);var identity=Path.Combine(data,"auto.cnf");
        if(!File.Exists(identity)||!Directory.Exists(Path.Combine(data,site.DatabaseName)))return null;
        var raw=string.Join("\n",site.Database,File.ReadAllText(identity).Trim(),site.DatabaseName,site.DatabaseUser??"",site.DatabasePassword??"");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
    async Task PrepareSiteDatabase(Site site)
    {
        if(DatabaseFingerprint(site) is { } known&&DatabaseReady().TryGetValue(site.Id,out var saved)&&saved==known)return;
        await PrepareSiteDatabaseNow(site);
        if(DatabaseFingerprint(site) is { } ready)
        {
            DatabaseReady()[site.Id]=ready;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabaseReadyFile)!);
            File.WriteAllText(DatabaseReadyFile+".tmp",JsonSerializer.Serialize(DatabaseReady()));
            File.Move(DatabaseReadyFile+".tmp",DatabaseReadyFile,true);
        }
    }
    async Task PrepareSiteDatabaseNow(Site site)
    {
        await Run(Php,["-c",Path.Combine(Root,"soft","php",InternalPhpVersion,"php.ini"),"-d","display_errors=stderr",Path.Combine(Root,"soft","db-manager","bootstrap.php"),"--site",site.Id]);
        if(site.Database is "mysql80" or "mysql57"&&!string.IsNullOrEmpty(site.DatabaseUser)&&!string.IsNullOrEmpty(site.DatabasePassword))await EnsureDatabaseUser(site);
    }

    static string SqlText(string value)=>"'"+value.Replace("\\","\\\\").Replace("'","\\'")+"'";

    // 账号同时建在 localhost 和 127.0.0.1 上（PHP 走 TCP 127.0.0.1），只授予本项目数据库的全部权限；已有账号会同步为当前密码。
    async Task EnsureDatabaseUser(Site site)
    {
        var kind=site.Database;var user=site.DatabaseUser!;var password=site.DatabasePassword!;
        if(Settings.DatabaseProblem(kind,site.DatabaseName,user,password) is "name" or "user" or "password")throw new IOException("Invalid database settings for "+site.Domain);
        var sql=new StringBuilder();
        sql.Append($"CREATE DATABASE IF NOT EXISTS `{site.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;\n");
        foreach(var host in new[]{"localhost","127.0.0.1"})
        {
            var account=$"{SqlText(user)}@{SqlText(host)}";
            sql.Append($"CREATE USER IF NOT EXISTS {account} IDENTIFIED BY {SqlText(password)};\nALTER USER {account} IDENTIFIED BY {SqlText(password)};\nGRANT ALL PRIVILEGES ON `{site.DatabaseName}`.* TO {account};\n");
        }
        sql.Append("FLUSH PRIVILEGES;\n");
        var mysql=Path.Combine(Root,"soft","mysql",Settings.DatabaseVersion(kind),"bin","mysql.exe");
        var rootClient=Path.Combine(Root,"temp","client-"+Guid.NewGuid().ToString("N")+".ini");
        var userClient=Path.Combine(Root,"temp","client-"+Guid.NewGuid().ToString("N")+".ini");
        Directory.CreateDirectory(Path.Combine(Root,"temp"));
        try
        {
            // 账号密码写入临时客户端配置文件，不放进命令行参数；SQL 通过标准输入传入。
            File.WriteAllText(rootClient,DatabaseClientConfig(kind,Settings.DatabasePassword(kind),Settings.MysqlUser),new UTF8Encoding(false));
            await RunWithInput(mysql,["--defaults-file="+rootClient,"--protocol=TCP","--connect-timeout=10","--batch"],sql.ToString());
            File.WriteAllText(userClient,DatabaseClientConfig(kind,password,user),new UTF8Encoding(false));
            var check=await RunWithInput(mysql,["--defaults-file="+userClient,"--protocol=TCP","--connect-timeout=10","--batch","--skip-column-names"],$"SELECT DATABASE() FROM (SELECT 1) t; USE `{site.DatabaseName}`; SELECT 'project-user-ok';\n");
            if(!check.Contains("project-user-ok"))throw new IOException("Project database user check failed: "+check.Trim());
            Log($"Database user ready · {user} → {site.DatabaseName}");
        }
        finally{foreach(var file in new[]{rootClient,userClient})if(File.Exists(file))File.Delete(file);}
    }

    async Task<string> RunWithInput(string executable,string[] args,string input,int timeout=60)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new IOException("Process could not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input);process.StandardInput.Close();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeout));}
        catch{if(!process.HasExited)process.Kill();throw;}
        var text=await output+await error;
        if(process.ExitCode!=0)throw new IOException($"{Path.GetFileName(executable)}: "+(string.IsNullOrWhiteSpace(text)?$"exit code {process.ExitCode}":text.Trim()));
        return text;
    }
}

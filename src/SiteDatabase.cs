using System.Diagnostics;
using System.Text;

namespace YikaiLocal;

// 项目数据库：建库（bootstrap.php，含 SQLite），以及项目专属 MySQL 账号的创建、授权和登录验证。
public sealed partial class Runtime
{
    async Task PrepareSiteDatabase(Site site)
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

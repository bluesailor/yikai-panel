using System.Diagnostics;
using System.Text;

namespace YikaiLocal;

// 桌面数据库客户端 HeidiSQL（GPL-2.0，便携版在 soft\heidisql）：为项目写入保存的会话，再用 --description 打开；密码只写进它的便携配置，不放进命令行。
public sealed partial class MainForm
{
    const string HeidiFolder="Yikai";
    string HeidiSqlExe=>Path.Combine(settings.Root,"soft","heidisql","heidisql.exe");
    bool HeidiSqlAvailable=>File.Exists(HeidiSqlExe);

    Task OpenInHeidiSql(Site? site=null)=>Work(async()=>{
        site??=Selected;if(site==null)return;
        if(!HeidiSqlAvailable)throw new IOException(T("未找到 HeidiSQL：","HeidiSQL not found: ","HeidiSQL が見つかりません：")+HeidiSqlExe);
        if(site.Database is "mysql80" or "mysql57")await runtime.EnsureDatabaseAsync(site.Database);
        else if(site.Database!="sqlite")throw new IOException(T("该项目未使用数据库。","This project has no database.","このプロジェクトはデータベースを使用しません。"));
        var session=site.Id;
        WriteHeidiSession(session,site);
        var info=new ProcessStartInfo(HeidiSqlExe){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(HeidiSqlExe)!};
        info.ArgumentList.Add($"--description={HeidiFolder}\\{session}");
        Process.Start(info);
    });

    void WriteHeidiSession(string session,Site site)
    {
        var file=Path.Combine(Path.GetDirectoryName(HeidiSqlExe)!,"portable_settings.txt");
        var prefix=$"Servers\\{HeidiFolder}\\{session}\\";
        var lines=File.Exists(file)?File.ReadAllLines(file,Encoding.UTF8).Where(l=>!l.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&!l.StartsWith($"Servers\\{HeidiFolder}\\Folder<",StringComparison.OrdinalIgnoreCase)&&!l.StartsWith("Updatecheck<")&&!l.StartsWith("DoUsageStatistics<")).ToList():[];
        void Add(string key,int type,string value)=>lines.Add($"{key}<|||>{type}<|||>{value}");
        Add("Updatecheck",3,"0");Add("DoUsageStatistics",3,"0");
        Add($"Servers\\{HeidiFolder}\\Folder",3,"1");
        if(site.Database=="sqlite")
        {
            Add(prefix+"NetType",3,"10");Add(prefix+"Host",1,settings.SqlitePath(site));Add(prefix+"Library",1,"sqlite3.dll");
            Add(prefix+"User",1,"");Add(prefix+"Password",1,"");
        }
        else
        {
            var kind=site.Database;
            var own=!string.IsNullOrEmpty(site.DatabaseUser)&&!string.IsNullOrEmpty(site.DatabasePassword);
            var user=own?site.DatabaseUser!:settings.MysqlUser;var password=own?site.DatabasePassword!:settings.DatabasePassword(kind);
            var ansi=password.All(c=>c<256);
            Add(prefix+"NetType",3,"0");Add(prefix+"Host",1,"127.0.0.1");Add(prefix+"Port",1,settings.DatabasePort(kind).ToString());
            Add(prefix+"User",1,user);Add(prefix+"Password",1,ansi?HeidiEncrypt(password):"");Add(prefix+"LoginPrompt",3,ansi?"0":"1");
            Add(prefix+"WindowsAuth",3,"0");Add(prefix+"Library",1,kind=="mysql57"?"libmariadb.dll":"libmysql-8.4.0.dll");
            if(!string.IsNullOrEmpty(site.DatabaseName))Add(prefix+"Databases",1,site.DatabaseName!);
        }
        File.WriteAllLines(file,lines,new UTF8Encoding(false));
    }

    // HeidiSQL 的 encrypt()：随机 salt 1..9，每个字符加 salt（超过 255 回绕）写成两位十六进制，末尾追加 salt。
    static string HeidiEncrypt(string text)
    {
        if(text.Length==0)return "";
        var salt=Random.Shared.Next(1,10);var sb=new StringBuilder();
        foreach(var c in text){var v=c+salt;if(v>255)v-=255;sb.Append(v.ToString("X2"));}
        return sb.Append(salt).ToString();
    }

    // 新建 YikaiCMS / WordPress 项目前准备程序文件，进度显示在状态栏。
    async Task<string?> DownloadProjectSource(string kind)
    {
        var report=new Progress<string>(text=>{if(!IsDisposed)progress.Text=text;});
        return kind switch{
            "yikaicms"=>await ProjectSources.YikaiCmsAsync(settings,report,CancellationToken.None),
            "wordpress"=>await ProjectSources.WordPressAsync(settings,report,CancellationToken.None),
            _=>null};
    }
}

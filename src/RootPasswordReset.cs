using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace YikaiLocal;

public sealed record RootPasswordResetResult(IReadOnlyList<string> Updated,IReadOnlyList<string> Manual);

public sealed partial class Runtime
{
    static string ClientQuote(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
    string DatabaseClientConfig(string kind,string password,string user)=>$"[client]\nhost=127.0.0.1\nport={Settings.DatabasePort(kind)}\nuser={ClientQuote(user)}\npassword={ClientQuote(password)}\ndefault-character-set=utf8mb4\n";
    string ResetText(string zh,string en,string ja)=>Settings.Language=="en"?en:Settings.Language=="ja"?ja:zh;
    string DatabaseConfigContent(string kind)
    {
        var data=Path.Combine(Root,"data",kind);var baseDir=Path.Combine(Root,"soft","mysql",Settings.DatabaseVersion(kind));
        var content=$"[mysqld]\nbasedir=\"{Slash(baseDir)}\"\ndatadir=\"{Slash(data)}\"\nport={Settings.DatabasePort(kind)}\nbind-address=127.0.0.1\ncharacter-set-server=utf8mb4\ncollation-server=utf8mb4_general_ci\nmax_allowed_packet=128M\nlog-error=\"{Slash(Root)}/logs/panel-{kind}.log\"\n"+(kind=="mysql80"?"mysqlx=0\n":"");
        return content;
    }
    // 自定义配置追加在面板生成内容之后，同名参数以自定义为准。
    static string DatabaseCustomSection(string custom)=>string.IsNullOrWhiteSpace(custom)?"":"\n# ---- custom (config/custom-mysqlXX.ini) ----\n"+custom.Replace("\r\n","\n")+"\n";
    void WriteDatabaseConfig(string kind)
    {
        Directory.CreateDirectory(Path.Combine(Root,"config"));File.WriteAllText(Path.Combine(Root,"config","panel-"+kind+".ini"),DatabaseConfigContent(kind)+DatabaseCustomSection(CustomConfigText(kind)));
    }
    async Task StopOwnedDatabase(string kind)
    {
        if(!Alive(kind))return;var process=processes[kind];var expected=Path.GetFullPath(Path.Combine(Root,"soft","mysql",Settings.DatabaseVersion(kind),"bin","mysqld.exe"));
        if(!string.Equals(process.MainModule?.FileName,expected,StringComparison.OrdinalIgnoreCase))throw new IOException(ResetText("数据库进程不属于当前环境，已停止操作。","This database process belongs to another environment.","別の環境のデータベースです。処理を中止しました。"));
        // MySQL 的 Windows 退出事件会执行正常关闭，不依赖已经忘记的数据库密码。
        bool signaled=false;
        foreach(var name in new[]{"mysqld"+process.Id+"_shutdown","MySQLShutdown"+process.Id}){
            if(!EventWaitHandle.TryOpenExisting(name,out var signal))continue;using(signal){signal.Set();signaled=true;break;}
        }
        if(!signaled)throw new IOException(ResetText("未能请求数据库正常停止，请关闭该 MySQL 后重试。","Could not request a clean shutdown. Stop this MySQL instance and retry.","MySQL を正常に停止できません。停止してから再試行してください。"));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));processes.Remove(kind);executablePaths.Remove(kind);SaveProcesses();process.Dispose();
    }
    public async Task<RootPasswordResetResult> ResetRootPasswordAsync(string kind,string? requestedPassword=null)
    {
        if(kind is not ("mysql80" or "mysql57"))throw new ArgumentException("Invalid database engine.");
        var newPassword=requestedPassword??Settings.DatabasePassword(kind);
        if(newPassword.Length is <1 or >128||newPassword.Any(char.IsControl))throw new ArgumentException(ResetText("密码请填写 1–128 个字符，不含换行。","Use 1–128 password characters without line breaks.","パスワードは改行を含まない 1～128 文字で入力してください。"));
        var version=Settings.DatabaseVersion(kind);var data=Path.Combine(Root,"data",kind);var executable=Path.Combine(Root,"soft","mysql",version,"bin","mysqld.exe");
        if(!Directory.Exists(Path.Combine(data,"mysql")))throw new IOException(ResetText("该 MySQL 尚未初始化，请先启动一次。","Start this MySQL version once before resetting its password.","この MySQL を一度起動してから再設定してください。"));
        if(!File.Exists(executable))throw new FileNotFoundException("MySQL executable not found.",executable);
        var wasRunning=Alive(kind);
        if(!wasRunning&&IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x=>x.Port==Settings.DatabasePort(kind)))throw new IOException(ResetText("数据库端口被其他实例占用，请先停止占用者。","The database port is occupied by another instance.","ポートが別のデータベースに使用されています。"));
        BundledDatabaseTools.Ensure(Root);
        var temp=Path.Combine(Root,"temp");Directory.CreateDirectory(temp);Directory.CreateDirectory(Path.Combine(Root,"logs"));var id=Guid.NewGuid().ToString("N");var sqlFile=Path.Combine(temp,"reset-"+kind+"-"+id+".sql");var clientFile=Path.Combine(temp,"reset-"+kind+"-"+id+".ini");var ini=Path.Combine(Root,"config","panel-"+kind+".ini");bool restore=false,temporary=false;
        var password=newPassword.Replace("\\","\\\\").Replace("'","\\'");
        try{
            File.WriteAllText(sqlFile,"SET NAMES utf8mb4;\nALTER USER 'root'@'localhost' IDENTIFIED BY '"+password+"';\n",new UTF8Encoding(false));
            File.WriteAllText(clientFile,DatabaseClientConfig(kind,newPassword,"root"),new UTF8Encoding(false));
            Log(ResetText($"正在重置 MySQL {version} root 密码…",$"Resetting MySQL {version} root password…",$"MySQL {version} root パスワードを再設定中…"));
            await StopOwnedDatabase(kind);restore=wasRunning;WriteDatabaseConfig(kind);
            Start(kind,executable,"--defaults-file="+ini,"--init-file="+sqlFile);temporary=true;await WaitPort(kind,Settings.DatabasePort(kind));
            await Run(Path.Combine(Root,"soft","mysql",version,"bin","mysql.exe"),["--defaults-file="+clientFile,"--protocol=TCP","--connect-timeout=5","--batch","--skip-column-names","--execute=SELECT 1"],15);
            Settings.SetDatabasePassword(kind,newPassword);
            await StopOwnedDatabase(kind);temporary=false;File.Delete(sqlFile);
            if(wasRunning){Start(kind,executable,"--defaults-file="+ini);await WaitPort(kind,Settings.DatabasePort(kind));restore=false;}
            Log(ResetText($"MySQL {version} root 密码已重置并验证。",$"MySQL {version} root password reset and verified.",$"MySQL {version} root パスワードを再設定し、接続を確認しました。"));
            return await SyncProjectPasswords(kind,newPassword);
        }finally{
            try{if(temporary&&Alive(kind))await StopOwnedDatabase(kind);}
            finally{
                if(File.Exists(sqlFile))File.Delete(sqlFile);if(File.Exists(clientFile))File.Delete(clientFile);
                if(restore&&!Alive(kind)){Start(kind,executable,"--defaults-file="+ini);await WaitPort(kind,Settings.DatabasePort(kind));}
                SaveProcesses();
            }
        }
    }
    async Task<RootPasswordResetResult> SyncProjectPasswords(string kind,string password)
    {
        var sites=Settings.Sites.Where(x=>x.Database==kind).ToArray();if(sites.Length==0)return new([],[]);
        var request=Path.Combine(Root,"temp","root-config-"+Guid.NewGuid().ToString("N")+".json");
        try{
            var backup=Path.Combine(Root,"backups","root-password-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            File.WriteAllText(request,JsonSerializer.Serialize(new{engine=kind,password,backup}));
            var output=await Run(Php,["-n",Path.Combine(Root,"soft","db-manager","root-config-sync.php"),request]);
            return JsonSerializer.Deserialize<RootPasswordResetResult>(output,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new([],sites.Select(x=>x.Domain).ToArray());
        }catch(Exception e) when(e is IOException or JsonException or System.ComponentModel.Win32Exception){Log(ResetText("密码已修改，部分项目连接需要手动检查。","Password changed; review project connections manually.","パスワードは変更済みです。プロジェクトの接続を確認してください。"));return new([],sites.Select(x=>x.Domain).ToArray());}
        finally{if(File.Exists(request))File.Delete(request);}
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace YikaiLocal;

public sealed record DatabasePortResult(int Port,bool Changed,IReadOnlyList<string> Updated,IReadOnlyList<string> Manual);

// 把 MySQL 实例改到常用端口（例如 3306）：与面板自身端口和另一个实例查重、检查被别的程序占用、
// 重启实例并用 SELECT 1 验证连接，然后同步已装站点的数据库配置（YikaiCMS 的 DB_PORT、WordPress 的 DB_HOST）。
// 失败时把端口和实例恢复到原状态。调用方持有 Runtime.Lock。
public sealed partial class Runtime
{
    public async Task<DatabasePortResult> ChangeDatabasePortAsync(string kind,int port)
    {
        if(kind is not ("mysql80" or "mysql57"))throw new ArgumentException("Invalid database engine.");
        if(port<1||port>65535)throw new IOException(ResetText("端口请填 1–65535 的数字。","Enter a port number (1–65535).","ポートは 1～65535 の数字で入力してください。"));
        var other=kind=="mysql80"?"mysql57":"mysql80";
        if(port==Settings.DbManagerPort||port==Settings.DbFastCgiPort)throw new IOException(ResetText("该端口是面板自己使用的（数据库页面或其 PHP）。","That port belongs to the panel itself.","そのポートはパネル自身が使用しています。"));
        if(port==Settings.DatabasePort(other))throw new IOException(ResetText("该端口已被另一个 MySQL 版本使用。","The other MySQL version already uses that port.","そのポートはもう一方の MySQL が使用中です。"));
        if(Alive(other)&&Settings.DatabasePort(other)==port)throw new IOException(ResetText("该端口已被另一个 MySQL 版本使用。","The other MySQL version already uses that port.","そのポートはもう一方の MySQL が使用中です。"));
        var oldPort=Settings.DatabasePort(kind);var oldPinned=Settings.DatabasePortPinned(kind);
        // 未初始化：只保存端口，首次启动时生效（不在这里初始化数据库）
        if(!Directory.Exists(Path.Combine(Root,"data",kind,"mysql")))
        {
            Settings.SetDatabasePort(kind,port);Settings.SetDatabasePortPinned(kind,true);Settings.Save();
            Log(ResetText($"MySQL {Settings.DatabaseVersion(kind)} 端口设为 {port}（尚未初始化，首次启动生效）。",$"MySQL {Settings.DatabaseVersion(kind)} port set to {port} (takes effect on first start).",$"MySQL {Settings.DatabaseVersion(kind)} のポートを {port} に設定しました（初回起動時に有効）。"));
            return new(port,true,[],[]);
        }
        if(port==oldPort&&oldPinned)return new(port,false,[],[]);
        // 目标端口被其它程序占用：直接报错，不动正在运行的实例
        if(PortDiagnostics.ListenerPid(port) is { } holder&&holder!=ServicePid(kind))
            throw new IOException(ResetText($"端口 {port} 已被占用：",$"Port {port} is in use by ",$"ポート {port} は使用中です：")+PortDiagnostics.Describe(holder).Replace("\n"," "));
        BundledDatabaseTools.Ensure(Root);
        var wasRunning=Alive(kind);
        Log(ResetText($"正在把 MySQL {Settings.DatabaseVersion(kind)} 端口从 {oldPort} 改为 {port}…",$"Changing MySQL {Settings.DatabaseVersion(kind)} port {oldPort} → {port}…",$"MySQL {Settings.DatabaseVersion(kind)} のポートを {oldPort} → {port} に変更しています…"));
        try
        {
            if(wasRunning)await StopOwnedDatabase(kind);
            Settings.SetDatabasePort(kind,port);Settings.SetDatabasePortPinned(kind,true);Settings.Save();
            WriteDatabaseConfig(kind);
            await StartDatabase(kind);
            await VerifyDatabaseConnectionAsync(kind);
            var (updated,manual)=await SyncProjectDatabases(kind,oldPort,port);
            if(!wasRunning)await StopOwnedDatabase(kind);
            Log(ResetText($"MySQL {Settings.DatabaseVersion(kind)} 端口已改为 {port}；同步 {updated.Count} 个项目。",$"MySQL {Settings.DatabaseVersion(kind)} port changed to {port}; {updated.Count} project(s) updated.",$"MySQL {Settings.DatabaseVersion(kind)} のポートを {port} に変更し、{updated.Count} 件を更新しました。"));
            return new(port,true,updated,manual);
        }
        catch
        {
            Settings.SetDatabasePort(kind,oldPort);Settings.SetDatabasePortPinned(kind,oldPinned);Settings.Save();
            try
            {
                WriteDatabaseConfig(kind);
                if(Alive(kind))await StopOwnedDatabase(kind);
                if(wasRunning)await StartDatabase(kind);
                Log(ResetText($"端口改动失败，已恢复为 {oldPort}。",$"Port change failed; restored to {oldPort}.",$"ポート変更に失敗し、{oldPort} に戻しました。"));
            }
            catch(Exception e){Log("Port rollback failed · "+e.Message);}
            throw;
        }
    }
    // 用专用账号执行 SELECT 1，确认新端口上的实例真的能连上（不只是端口在监听）
    async Task VerifyDatabaseConnectionAsync(string kind)
    {
        var version=Settings.DatabaseVersion(kind);var temp=Path.Combine(Root,"temp");Directory.CreateDirectory(temp);
        var client=Path.Combine(temp,"port-check-"+kind+"-"+Guid.NewGuid().ToString("N")+".ini");
        try
        {
            File.WriteAllText(client,DatabaseClientConfig(kind,Settings.DatabasePassword(kind),Settings.MysqlUser));
            await Run(Path.Combine(Root,"soft","mysql",version,"bin","mysql.exe"),
                ["--defaults-file="+client,"--protocol=TCP","--connect-timeout=5","--batch","--skip-column-names","--execute=SELECT 1"],15);
        }
        finally{if(File.Exists(client))File.Delete(client);}
    }
    // 端口变了：让已装站点跟着改（YikaiCMS 的 DB_PORT、WordPress 的 DB_HOST 里的 host:port）。
    // 识别不了或端口对不上的站点列入 Manual，由界面提示手动检查。
    async Task<(IReadOnlyList<string> Updated,IReadOnlyList<string> Manual)> SyncProjectDatabases(string kind,int oldPort,int newPort)
    {
        var sites=Settings.Sites.Where(x=>x.Database==kind).ToArray();if(sites.Length==0)return ([],[]);
        var request=Path.Combine(Root,"temp","db-port-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var backup=Path.Combine(Root,"backups","database-port-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            File.WriteAllText(request,JsonSerializer.Serialize(new{engine=kind,port=newPort,oldPort,backup}));
            var output=await Run(Php,["-n",Path.Combine(Root,"soft","db-manager","root-config-sync.php"),request]);
            var result=JsonSerializer.Deserialize<RootPasswordResetResult>(output,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
            return result==null?([],sites.Select(x=>x.Domain).ToArray()):((IReadOnlyList<string>)result.Updated,result.Manual);
        }
        catch(Exception e) when(e is IOException or JsonException or System.ComponentModel.Win32Exception)
        {
            Log(ResetText("端口已修改，但部分项目连接需要手动检查。","Port changed; review project connections manually.","ポートは変更済みですが、一部の接続は手動確認が必要です。"));
            return ([],sites.Select(x=>x.Domain).ToArray());
        }
        finally{if(File.Exists(request))File.Delete(request);}
    }
}

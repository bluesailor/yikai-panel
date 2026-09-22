using System.Text;
using System.Text.RegularExpressions;

namespace YikaiLocal;

// 可在面板里编辑的配置文件。
// php.ini 是源文件，直接编辑；Nginx / Apache / MySQL 的主配置由面板生成，编辑的是合并进去的 custom-* 文件。
public sealed record ConfigFile(string Key,string Title,string FilePath,string Kind,string? Version=null);

public sealed partial class Runtime
{
    static readonly UTF8Encoding Utf8NoBom=new(false);
    string CustomConfigPath(string key)=>Path.Combine(Root,"config","custom-"+key+(key.StartsWith("mysql")?".ini":".conf"));

    public IReadOnlyList<ConfigFile> ConfigFiles()
    {
        var list=new List<ConfigFile>();
        foreach(var version in PhpVersions.Where(PhpInstalled))
            list.Add(new("php"+version,"PHP "+version+" · php.ini",Path.Combine(Root,"soft","php",version,"php.ini"),"php",version));
        list.Add(new("dbpage",ResetText("数据库页面 · php.ini","DB manager · php.ini","DB 管理 · php.ini"),Path.Combine(Root,"config","phpmyadmin-php.ini"),"dbpage",InternalPhpVersion));
        list.Add(new("nginx",ResetText("Nginx · 自定义配置","Nginx · custom config","Nginx · カスタム設定"),CustomConfigPath("nginx"),"nginx"));
        if(WebServerInstalled("apache"))list.Add(new("apache",ResetText("Apache · 自定义配置","Apache · custom config","Apache · カスタム設定"),CustomConfigPath("apache"),"apache"));
        list.Add(new("mysql80",ResetText("MySQL 8.0 · 自定义配置","MySQL 8.0 · custom config","MySQL 8.0 · カスタム設定"),CustomConfigPath("mysql80"),"mysql","mysql80"));
        list.Add(new("mysql57",ResetText("MySQL 5.7 · 自定义配置","MySQL 5.7 · custom config","MySQL 5.7 · カスタム設定"),CustomConfigPath("mysql57"),"mysql","mysql57"));
        return list;
    }
    public ConfigFile ConfigFileByKey(string key)=>ConfigFiles().FirstOrDefault(f=>f.Key==key)??throw new ArgumentException("Unknown config file: "+key);

    public string ReadConfig(ConfigFile file){EnsureCustomConfig(file);return File.ReadAllText(file.FilePath);}

    void EnsureCustomConfig(ConfigFile file)
    {
        if(file.Kind is "php" or "dbpage"||File.Exists(file.FilePath))return;
        var template=file.Kind switch{
            "nginx"=>"# 易开面板 · Nginx 自定义配置\n# 写在 http 块内，对所有项目生效；保存后面板会检查语法并重载 Nginx。\n# 面板默认 client_max_body_size 160m、fastcgi_read_timeout 600s；在这里写同名指令即可替换默认值。\n# 例：\n# client_max_body_size 512m;\n# gzip on;\n",
            "apache"=>"# 易开面板 · Apache 自定义配置\n# 全局生效（在各项目 VirtualHost 之前载入），同名指令覆盖面板默认值；保存后检查语法并重启 Apache。\n# 例：\n# Header always set X-Frame-Options SAMEORIGIN\n# Timeout 900\n",
            _=>"# 易开面板 · MySQL 自定义配置\n# 追加在面板生成的 my.ini 之后，同名参数以这里为准；保存后重启该 MySQL。\n# 不要修改 port、datadir、basedir，它们由面板管理。\n[mysqld]\n# 例：\n# max_allowed_packet=256M\n# sql_mode=STRICT_TRANS_TABLES,NO_ENGINE_SUBSTITUTION\n"};
        Directory.CreateDirectory(Path.GetDirectoryName(file.FilePath)!);File.WriteAllText(file.FilePath,template,Utf8NoBom);
    }
    string CustomConfigText(string key){var path=CustomConfigPath(key);return File.Exists(path)?File.ReadAllText(path):"";}
    static bool HasDirective(string text,string name)=>Regex.IsMatch(text,@"(?im)^\s*"+Regex.Escape(name)+@"\s");

    // 只检查，不写入正式文件。
    public async Task CheckConfigAsync(ConfigFile file,string text)
    {
        if(text.Length>1_048_576)throw new IOException(ResetText("配置文件不能超过 1 MB。","Configuration files are limited to 1 MB.","設定ファイルは 1 MB までです。"));
        switch(file.Kind)
        {
            case "php" or "dbpage":
            {
                var temp=Path.Combine(Root,"temp","config-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
                try
                {
                    File.WriteAllText(Path.Combine(temp,"php.ini"),text,Utf8NoBom);
                    var output=await Run(Path.Combine(Root,"soft","php",file.Version!,"php.exe"),["-c",Path.Combine(temp,"php.ini"),"-d","display_startup_errors=1","-d","display_errors=stderr","-d","log_errors=0","-r","echo 'php-ini-ok';"],120);
                    var problems=output.Replace("php-ini-ok","").Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0).ToList();
                    if(!output.Contains("php-ini-ok")||problems.Count>0)throw new IOException(string.Join("\n",problems.DefaultIfEmpty(output.Trim())));
                }
                finally{try{Directory.Delete(temp,true);}catch(IOException){}}
                break;
            }
            case "nginx" or "apache":
                await WithCustomConfig(file,text,async()=>{
                    if(file.Kind=="nginx"){WriteNginx();await Run(NginxExecutable,["-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig),"-t"]);}
                    else{WriteApache();await Run(ApacheExecutable,["-t","-f",Slash(ApacheConfig)]);}
                },keep:false);
                break;
            case "mysql":
            {
                // MySQL 5.7 / 8.0.12 没有可靠的离线参数校验（--help --verbose 对未知参数也返回成功，--validate-config 需 8.0.16+），
                // 这里只检查格式；参数本身在重启时验证，失败自动回滚。
                var lines=text.Replace("\r\n","\n").Split('\n');bool section=false;
                for(var i=0;i<lines.Length;i++)
                {
                    var line=lines[i].Trim();if(line.Length==0||line[0] is '#' or ';')continue;
                    if(Regex.IsMatch(line,@"^\[[A-Za-z0-9_.-]+\]$")){section=true;continue;}
                    if(line.StartsWith("!include"))continue;
                    if(!Regex.IsMatch(line,@"^[A-Za-z][A-Za-z0-9_-]*(\s*=.*)?$"))throw new IOException(ResetText($"第 {i+1} 行格式不正确：{line}",$"Line {i+1} is not a valid option: {line}",$"{i+1} 行目の形式が正しくありません：{line}"));
                    if(!section)throw new IOException(ResetText($"第 {i+1} 行之前缺少 [mysqld] 段落标题。",$"Add a [mysqld] section header before line {i+1}.",$"{i+1} 行目の前に [mysqld] を追加してください。"));
                }
                await Task.CompletedTask;
                break;
            }
        }
    }

    // 保存并应用：检查 → 备份 → 写入 → 重启 / 重载受影响的运行中服务；应用失败时恢复原文件并尽量恢复服务。调用方持有 Runtime.Lock。
    public async Task<string> SaveConfigAsync(ConfigFile file,string text)
    {
        EnsureCustomConfig(file);
        await CheckConfigAsync(file,text);
        var original=File.ReadAllText(file.FilePath);var wasRunning=ConfigServiceRunning(file);
        var backup=Path.Combine(Root,"backups","config",Path.GetFileNameWithoutExtension(file.FilePath)+(file.Version is {} v&&file.Kind=="php"?"-"+v:"")+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+Path.GetExtension(file.FilePath));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.WriteAllText(backup,original,Utf8NoBom);
        File.WriteAllText(file.FilePath,text,Utf8NoBom);
        Log("Config saved · "+file.Key);
        try{return await ApplyConfig(file,wasRunning);}
        catch(Exception error)
        {
            File.WriteAllText(file.FilePath,original,Utf8NoBom);Log("Config restored · "+file.Key);
            try{await ApplyConfig(file,wasRunning);}
            catch(Exception restoreError) when(restoreError is IOException or TimeoutException or InvalidOperationException)
            {
                Log("Config restore apply failed · "+restoreError.Message);
                throw new IOException(error.Message+"\n\n"+ResetText("原配置已恢复，但服务未能重新启动，请查看运行日志：","The previous configuration was restored, but the service did not restart. See the runtime logs: ","元の設定に戻しましたが、サービスを再起動できませんでした。ログを確認してください：")+restoreError.Message,error);
            }
            throw;
        }
    }
    bool ConfigServiceRunning(ConfigFile file)=>file.Kind switch{"php"=>ServiceState("php"+file.Version).Any,"dbpage"=>Alive("php-db"),"nginx" or "apache"=>WebKey==file.Kind&&WebAlive,_=>Alive(file.Version!)};
    async Task<string> ApplyConfig(ConfigFile file,bool running)
    {
        switch(file.Kind)
        {
            case "php":
            {
                var key="php"+file.Version;
                if(!running)return ResetText("已保存。PHP "+file.Version+" 下次启动时生效。","Saved. PHP "+file.Version+" uses it on next start.","保存しました。PHP "+file.Version+" の次回起動時に反映します。");
                await RestartServiceAsync(key);return ResetText("已保存，PHP "+file.Version+" 已重启。","Saved and PHP "+file.Version+" restarted.","保存し、PHP "+file.Version+" を再起動しました。");
            }
            case "dbpage":
                if(!running)return ResetText("已保存，数据库页面下次启动时生效。","Saved. The DB manager uses it on next start.","保存しました。DB 管理の次回起動時に反映します。");
                await RestartServiceAsync("dbpage");return ResetText("已保存，数据库页面已重启。","Saved and the DB manager restarted.","保存し、DB 管理を再起動しました。");
            case "nginx" or "apache":
                if(!running)
                {
                    if(file.Kind=="nginx"){WriteNginx();await Run(NginxExecutable,["-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig),"-t"]);}
                    else{WriteApache();await Run(ApacheExecutable,["-t","-f",Slash(ApacheConfig)]);}
                    return ResetText($"已保存并通过语法检查，{WebServerName(file.Kind)} 下次启动时生效。",$"Saved and validated. {WebServerName(file.Kind)} uses it on next start.",$"保存し検査に合格しました。{WebServerName(file.Kind)} の次回起動時に反映します。");
                }
                await ReloadWebServer();return ResetText($"已保存，{WebServerName(file.Kind)} 已应用。",$"Saved and applied to {WebServerName(file.Kind)}.",$"保存し、{WebServerName(file.Kind)} に反映しました。");
            default:
            {
                var kind=file.Version!;
                if(!running)return ResetText($"已保存，MySQL {Settings.DatabaseVersion(kind)} 下次启动时生效。",$"Saved. MySQL {Settings.DatabaseVersion(kind)} uses it on next start.",$"保存しました。MySQL {Settings.DatabaseVersion(kind)} の次回起動時に反映します。");
                await StopOwnedDatabase(kind);processes.Remove(kind);
                var logFile=Path.Combine(Root,"logs","panel-"+kind+".log");var logStart=File.Exists(logFile)?new FileInfo(logFile).Length:0;
                try{await EnsureDatabaseAsync(kind);}
                catch(Exception error)
                {
                    // 启动失败的进程可能还在退出中：强制结束，回滚时才能重新启动。
                    if(processes.TryGetValue(kind,out var failed)){try{if(!failed.HasExited){failed.Kill();await failed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}}catch(InvalidOperationException){}processes.Remove(kind);executablePaths.Remove(kind);SaveProcesses();}
                    throw new IOException(ResetText($"MySQL {Settings.DatabaseVersion(kind)} 使用新配置启动失败。",$"MySQL {Settings.DatabaseVersion(kind)} failed to start with the new configuration.",$"MySQL {Settings.DatabaseVersion(kind)} は新しい設定で起動できませんでした。")+"\n"+DatabaseLogErrors(logFile,logStart,error.Message),error);
                }
                return ResetText($"已保存，MySQL {Settings.DatabaseVersion(kind)} 已重启。",$"Saved and MySQL {Settings.DatabaseVersion(kind)} restarted.",$"保存し、MySQL {Settings.DatabaseVersion(kind)} を再起動しました。");
            }
        }
    }
    static string DatabaseLogErrors(string logFile,long from,string fallback)
    {
        try
        {
            using var stream=new FileStream(logFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);stream.Seek(Math.Min(from,stream.Length),SeekOrigin.Begin);
            var errors=new StreamReader(stream).ReadToEnd().Split('\n').Where(l=>l.Contains("[ERROR]")&&!l.Contains("Aborting")).Select(l=>{var i=l.IndexOf("[Server]",StringComparison.Ordinal);return (i>=0?l[(i+8)..]:l).Trim();}).Distinct().Take(5).ToList();
            return errors.Count>0?string.Join("\n",errors):fallback;
        }
        catch(IOException){return fallback;}
    }
    // 用候选内容临时替换自定义文件执行检查，结束后恢复（keep=false）。
    async Task WithCustomConfig(ConfigFile file,string text,Func<Task> action,bool keep)
    {
        EnsureCustomConfig(file);
        var original=File.ReadAllText(file.FilePath);var generated=file.Kind=="nginx"?NginxConfig:ApacheConfig;var generatedOriginal=File.Exists(generated)?File.ReadAllText(generated):null;
        File.WriteAllText(file.FilePath,text,Utf8NoBom);
        try{await action();}
        finally
        {
            if(!keep){File.WriteAllText(file.FilePath,original,Utf8NoBom);if(generatedOriginal!=null)File.WriteAllText(generated,generatedOriginal);}
        }
    }
}

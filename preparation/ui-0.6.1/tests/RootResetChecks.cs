using YikaiLocal;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

partial class Program
{
    static int UnusedPort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    static async Task<string> Execute(string exe,IEnumerable<string> args,int timeout=30)
    {
        var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);using var p=Process.Start(info)!;
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeout));}catch{if(!p.HasExited)p.Kill();throw;}
        var text=await output+await error;if(p.ExitCode!=0)throw new IOException(text);return text;
    }
    static async Task RootResetChecks()
    {
        var root=Path.Combine(Root,"isolated mysql reset");var settings=new Settings{Root=root,Mysql80Port=UnusedPort(),Mysql57Port=UnusedPort()};settings.Save();var runtime=new Runtime(settings);var records=new List<OwnedProcess>();
        const string password80="New'Root\\80#\"中";const string password57="Other'Root\\57#\"文";
        string Exe(string kind,string file)=>Path.Combine(root,"soft","mysql",settings.DatabaseVersion(kind),"bin",file+".exe");
        void Ledger(){Directory.CreateDirectory(Path.Combine(root,"temp"));File.WriteAllText(Path.Combine(root,"temp","panel-processes.json"),JsonSerializer.Serialize(records));runtime.Adopt();}
        async Task<string> Sql(string kind,string password,string sql)
        {
            var client=Path.Combine(root,"temp","test-"+kind+".ini");File.WriteAllText(client,$"[client]\nhost=127.0.0.1\nport={settings.DatabasePort(kind)}\nuser=root\npassword=\"{password.Replace("\\","\\\\").Replace("\"","\\\"")}\"\ndefault-character-set=utf8mb4\n");try{return await Execute(Exe(kind,"mysql"),["--defaults-file="+client,"--protocol=TCP","--connect-timeout=2","--batch","--skip-column-names","--execute="+sql]);}finally{File.Delete(client);}
        }
        async Task StartFixture(string kind,string password)
        {
            var start=new ProcessStartInfo(Exe(kind,"mysqld")){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--defaults-file="+Path.Combine(root,"config","panel-"+kind+".ini"));var p=Process.Start(start)!;
            records.RemoveAll(x=>x.Key==kind);records.Add(new(kind,p.Id,p.StartTime.ToUniversalTime().Ticks,p.MainModule!.FileName));Ledger();
            for(var i=0;i<150;i++){if(p.HasExited)throw new IOException("Fixture exited: "+kind);try{await Sql(kind,password,"SELECT 1");return;}catch(IOException){await Task.Delay(200);}}
            throw new TimeoutException("Fixture startup: "+kind);
        }
        Task Stop(string kind)=>(Task)typeof(Runtime).GetMethod("StopOwnedDatabase",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(runtime,[kind])!;
        try{
            var phpFolder=Path.Combine(root,"soft","php","8.2");Directory.CreateDirectory(phpFolder);foreach(var file in Directory.EnumerateFiles(@"D:\yikai\soft\php\8.2")){if(Path.GetExtension(file)==".dll"||Path.GetFileName(file)=="php.exe")File.Copy(file,Path.Combine(phpFolder,Path.GetFileName(file)));}
            foreach(var kind in new[]{"mysql80","mysql57"}){
                var source=Path.Combine(@"D:\yikai\soft\mysql",settings.DatabaseVersion(kind));var destination=Path.Combine(root,"soft","mysql",settings.DatabaseVersion(kind));Directory.CreateDirectory(Path.Combine(destination,"bin"));
                foreach(var file in Directory.EnumerateFiles(Path.Combine(source,"bin"))){var name=Path.GetFileName(file);if(name is "mysql.exe" or "mysqld.exe"||Path.GetExtension(file)==".dll")File.Copy(file,Path.Combine(destination,"bin",name));}
                foreach(var file in Directory.EnumerateFiles(Path.Combine(source,"share"),"*",SearchOption.AllDirectories)){var relative=Path.GetRelativePath(source,file);Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(destination,relative))!);File.Copy(file,Path.Combine(destination,relative));}
                Directory.CreateDirectory(Path.Combine(root,"data",kind));Directory.CreateDirectory(Path.Combine(root,"logs"));Call(runtime,"WriteDatabaseConfig",kind);
                await Execute(Exe(kind,"mysqld"),["--defaults-file="+Path.Combine(root,"config","panel-"+kind+".ini"),"--initialize-insecure"],120);
                await StartFixture(kind,"");await Sql(kind,"","CREATE DATABASE reset_check; CREATE TABLE reset_check.keep_data (id INT PRIMARY KEY, value VARCHAR(50)); INSERT INTO reset_check.keep_data VALUES (1,'preserved'); ALTER USER 'root'@'localhost' IDENTIFIED BY 'forgotten-value';");
                Check(true,"Initialized isolated MySQL "+settings.DatabaseVersion(kind)+" with unknown-to-panel root password");
                var folder=Path.Combine(root,"wwwroot",kind);Directory.CreateDirectory(folder);var site=new Site{Id=kind,Domain=kind+".yikai",Database=kind,DatabaseName="reset_check",Directory=folder};settings.Sites.Add(site);
                var text="<?php\nthrow new RuntimeException('Project PHP must not execute during inspection');\ndefine('DB_NAME','reset_check');define('DB_USER','root');";
                if(kind=="mysql80"){Directory.CreateDirectory(Path.Combine(folder,"config"));Directory.CreateDirectory(Path.Combine(folder,"includes"));File.WriteAllText(Path.Combine(folder,"includes","init.php"),"");File.WriteAllText(Path.Combine(folder,"config","config.php"),text+"define('DB_HOST','127.0.0.1');define('DB_PORT',"+settings.Mysql80Port+");define('DB_PASS','forgotten-value');");}
                else File.WriteAllText(Path.Combine(folder,"wp-config.php"),text+"define('DB_HOST','127.0.0.1:"+settings.Mysql57Port+"');define('DB_PASSWORD','forgotten-value');");
            }
            settings.Sites.Add(new Site{Id="unknown",Domain="unknown.yikai",Database="mysql80",Directory=Path.Combine(root,"unknown")});settings.Save();var sitesBefore=JsonSerializer.Serialize(settings.Sites,Settings.Json);var otherPid=runtime.ServicePid("mysql57");RootPasswordResetResult result80;using(Runtime.Lock(settings))result80=await runtime.ResetRootPasswordAsync("mysql80",password80);
            Check(result80.Updated.SequenceEqual(new[]{"mysql80.yikai"})&&result80.Manual.SequenceEqual(new[]{"unknown.yikai"}),"YikaiCMS configuration updated; unknown project reported for manual review");
            Check(settings.DatabasePassword("mysql80")==password80&&settings.DatabasePassword("mysql57")=="123456","MySQL credentials saved independently");
            Check(runtime.ServiceRunning("mysql80"),"Running MySQL 8.0 returns to running state");Check(runtime.ServicePid("mysql57")==otherPid,"MySQL 8.0 reset leaves MySQL 5.7 process unchanged");
            Check((await Sql("mysql80",password80,"SELECT value FROM reset_check.keep_data WHERE id=1")).Trim()=="preserved","MySQL 8.0 custom root login verified and project data preserved");
            try{await Sql("mysql80","forgotten-value","SELECT 1");throw new Exception("Old password still accepted");}catch(IOException){Check(true,"MySQL 8.0 old password rejected");}
            await Stop("mysql57");var running80=runtime.ServicePid("mysql80");RootPasswordResetResult result57;using(Runtime.Lock(settings))result57=await runtime.ResetRootPasswordAsync("mysql57",password57);Check(result57.Updated.SequenceEqual(new[]{"mysql57.yikai"})&&result57.Manual.Count==0,"WordPress database password updated");
            Check(!runtime.ServiceRunning("mysql57"),"Stopped MySQL 5.7 remains stopped after reset");Check(runtime.ServicePid("mysql80")==running80,"MySQL 5.7 reset leaves MySQL 8.0 process unchanged");
            records.RemoveAll(x=>x.Key=="mysql80");var ledger=JsonSerializer.Deserialize<List<OwnedProcess>>(File.ReadAllText(Path.Combine(root,"temp","panel-processes.json")))!;records.AddRange(ledger.Where(x=>x.Key=="mysql80"));
            await StartFixture("mysql57",password57);Check((await Sql("mysql57",password57,"SELECT value FROM reset_check.keep_data WHERE id=1")).Trim()=="preserved","MySQL 5.7 custom root login verified and project data preserved");
            try{await Sql("mysql57","forgotten-value","SELECT 1");throw new Exception("Old password still accepted");}catch(IOException){Check(true,"MySQL 5.7 old password rejected");}
            Check(sitesBefore==JsonSerializer.Serialize(settings.Sites,Settings.Json),"Project entries and ports preserved");Check(Settings.Load(root).DatabasePassword("mysql80")==password80&&Settings.Load(root).DatabasePassword("mysql57")==password57,"Independent passwords survive reload");Check(!Directory.EnumerateFiles(Path.Combine(root,"temp"),"reset-*").Any()&&!Directory.EnumerateFiles(Path.Combine(root,"temp"),"root-config-*").Any(),"Reset SQL, request and client files are removed");
            Directory.CreateDirectory(Path.Combine(root,"soft","db-manager"));File.Copy(@"D:\yikai\soft\db-manager\lang.php",Path.Combine(root,"soft","db-manager","lang.php"));
            foreach(var kind in new[]{"mysql80","mysql57"}){var code="require '"+Path.Combine(root,"soft","db-manager","common.php").Replace('\\','/')+"'; foreach($config['sites'] as $s) { if($s['id']==='"+kind+"') echo connectDatabase($s)->query('SELECT value FROM keep_data WHERE id=1')->fetchColumn(); }";Check((await Execute(Path.Combine(phpFolder,"php.exe"),["-c",@"D:\yikai\soft\php\8.2\php.ini","-r",code])).Trim()=="preserved","PHP database manager uses independent password: "+kind);}
            await Stop("mysql57");var blocker=new TcpListener(IPAddress.Loopback,settings.Mysql57Port);blocker.Start();try{await runtime.ResetRootPasswordAsync("mysql57");throw new Exception("Occupied port accepted");}catch(IOException){Check(true,"An unrelated port listener is rejected before reset");}finally{blocker.Stop();}
        }finally{foreach(var kind in new[]{"mysql80","mysql57"})if(runtime.ServiceRunning(kind))await Stop(kind);Console.WriteLine("MySQL reset fixture: "+root);}
    }
}

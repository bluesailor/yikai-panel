using YikaiLocal;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;

class Program
{
    static readonly string Root=Path.Combine(Path.GetTempPath(),"yikai-migration-"+Guid.NewGuid().ToString("N"));
    static readonly List<Process> Processes=[];
    static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static async Task<string> Exec(string exe,IEnumerable<string> args,int timeout=30){
        var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)info.ArgumentList.Add(arg);using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeout));}catch{if(!p.HasExited)p.Kill();throw;}
        var error=await errors;var text=await output;if(p.ExitCode!=0)throw new IOException(error);return text;
    }
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    [STAThread] static int Main(string[] args){
        try{if(args.Contains("--path")){PathSafety().GetAwaiter().GetResult();return 0;}if(args.Contains("--ui")){Ui();return 0;}Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.WriteLine(e);return 1;}
        finally{foreach(var p in Processes){try{if(!p.HasExited){using var shutdown=EventWaitHandle.OpenExisting("MySQLShutdown"+p.Id);shutdown.Set();if(!p.WaitForExit(10000))p.Kill();}}catch{if(!p.HasExited)p.Kill();}p.Dispose();}Console.WriteLine("Fixture: "+Root);}
    }
    static async Task PathSafety(){
        var settings=new Settings{Root=Root};settings.Save();var php=Path.Combine(Root,"soft","php","8.2");Directory.CreateDirectory(php);File.WriteAllText(Path.Combine(php,"php-cgi.exe"),"");
        var temp=Path.Combine(Root,"temp");Directory.CreateDirectory(temp);
        try{await new PhpStudyImport(settings,new Runtime(settings)).ImportAsync(new(){Folder=temp,TargetDomain="cycle.yikai",Database=new(){Driver="none"}});throw new Exception("Recursive staging accepted");}
        catch(IOException e){Check(e.Message.Contains("staging")&&!Directory.EnumerateFileSystemEntries(temp).Any(),"Recursive staging source rejected before copying");}
    }
    static async Task Run(){
        var settings=new Settings{Root=Root,Mysql80Port=Port(),Mysql57Port=Port(),MysqlPassword=""};settings.Save();var runtime=new Runtime(settings);
        var phpDir=Path.Combine(Root,"soft","php","8.2");Directory.CreateDirectory(phpDir);
        foreach(var file in Directory.EnumerateFiles(@"D:\yikai\soft\php\8.2")){if(Path.GetExtension(file)==".dll"||Path.GetFileName(file) is "php.exe" or "php-cgi.exe")File.Copy(file,Path.Combine(phpDir,Path.GetFileName(file)));}
        File.WriteAllText(Path.Combine(phpDir,"php.ini"),"extension_dir=\"D:/yikai/soft/php/8.2/ext\"\nextension=pdo_mysql\nextension=sqlite3\n");
        var owned=new List<OwnedProcess>();
        string Mysql(string kind,string file)=>Path.Combine(Root,"soft","mysql",settings.DatabaseVersion(kind),"bin",file+".exe");
        async Task<string> Sql(string kind,string sql)=>await Exec(Mysql(kind,"mysql"),["--no-defaults","--protocol=TCP","-h127.0.0.1","-P"+settings.DatabasePort(kind),"-uroot","--default-character-set=utf8mb4","--batch","--skip-column-names","--execute="+sql]);
        foreach(var kind in new[]{"mysql80","mysql57"}){
            var source=Path.Combine(@"D:\yikai\soft\mysql",settings.DatabaseVersion(kind));var dest=Path.Combine(Root,"soft","mysql",settings.DatabaseVersion(kind));
            Directory.CreateDirectory(Path.Combine(dest,"bin"));
            foreach(var file in Directory.EnumerateFiles(Path.Combine(source,"bin"))){if(Path.GetExtension(file)==".dll"||Path.GetFileName(file) is "mysqld.exe" or "mysql.exe" or "mysqldump.exe")File.Copy(file,Path.Combine(dest,"bin",Path.GetFileName(file)));}
            foreach(var file in Directory.EnumerateFiles(Path.Combine(source,"share"),"*",SearchOption.AllDirectories)){var target=Path.Combine(dest,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}
            Directory.CreateDirectory(Path.Combine(Root,"data",kind));Directory.CreateDirectory(Path.Combine(Root,"logs"));
            typeof(Runtime).GetMethod("WriteDatabaseConfig",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(runtime,[kind]);
            var ini=Path.Combine(Root,"config","panel-"+kind+".ini");await Exec(Mysql(kind,"mysqld"),["--defaults-file="+ini,"--initialize-insecure"],150);
            var start=new ProcessStartInfo(Mysql(kind,"mysqld")){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--defaults-file="+ini);
            var process=Process.Start(start)!;Processes.Add(process);bool ready=false;
            for(int i=0;i<120;i++){try{await Sql(kind,"SELECT 1");ready=true;break;}catch(IOException){await Task.Delay(200);}}
            Check(ready,"Isolated "+kind+" started");owned.Add(new(kind,process.Id,process.StartTime.ToUniversalTime().Ticks,process.MainModule!.FileName));
            await Sql(kind,"CREATE DATABASE source_db CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci; CREATE TABLE source_db.items(id INT PRIMARY KEY AUTO_INCREMENT, title VARCHAR(100), payload LONGBLOB, optional VARCHAR(20) NULL) ENGINE=InnoDB; INSERT INTO source_db.items(title,payload,optional) VALUES ('中文日本語',0x0001FF27225C,NULL),('source_db.items stays text',0x01,''); CREATE TABLE source_db.empty_table(id INT) ENGINE=MyISAM; CREATE TABLE source_db.audit(id INT); CREATE DEFINER='root'@'localhost' VIEW source_db.item_view AS SELECT id,title FROM source_db.items; CREATE DEFINER='root'@'localhost' TRIGGER source_db.item_added AFTER INSERT ON source_db.items FOR EACH ROW INSERT INTO source_db.audit VALUES(NEW.id); CREATE DEFINER='root'@'localhost' PROCEDURE source_db.item_count() SELECT COUNT(*) FROM source_db.items; CREATE DEFINER='root'@'localhost' EVENT source_db.future_event ON SCHEDULE AT CURRENT_TIMESTAMP + INTERVAL 10 YEAR DISABLE DO INSERT INTO source_db.audit VALUES(999);");
        }
        Directory.CreateDirectory(Path.Combine(Root,"temp"));File.WriteAllText(Path.Combine(Root,"temp","panel-processes.json"),JsonSerializer.Serialize(owned));runtime.Adopt();
        var original=Path.Combine(Root,"original");Directory.CreateDirectory(Path.Combine(original,"config"));Directory.CreateDirectory(Path.Combine(original,"includes"));
        File.WriteAllText(Path.Combine(original,"includes","init.php"),"<?php throw new Exception('Do not execute copied PHP');");
        File.WriteAllText(Path.Combine(original,"index.php"),"<?php echo 'fixture';");File.WriteAllBytes(Path.Combine(original,"asset.bin"),[0,1,255,25]);
        var config=Path.Combine(original,"config","config.php");
        void Configure(string kind){File.WriteAllText(config,"<?php\ndeclare(strict_types=1);\n// define configuration without an explicit driver\ndefine('DB_HOST','127.0.0.1');\ndefine('DB_PORT',"+settings.DatabasePort(kind)+");\ndefine('DB_NAME','source_db');define('DB_USER','root');define('DB_PASS','');");}
        var transfer=new PhpStudyImport(settings,runtime);
        async Task<ImportResult> Import(string from,string to,string domain,bool brokenConfig=false){
            Configure(from);var hash=Hash(config);var siteCount=settings.Sites.Count;
            var candidate=new ImportCandidate{Domain="source.yikai",TargetDomain=domain,Folder=original,Engine=to,Database=new(){Kind="yikaicms",ConfigFile=brokenConfig?"missing.php":"config/config.php",Driver="mysql",Host="127.0.0.1",Port=settings.DatabasePort(from),Name="source_db",User="root",Password=""}};
            var phases=new List<string>();var result=await transfer.ImportAsync(candidate,new Reporter(p=>phases.Add(p.Phase)));
            Check(Hash(config)==hash,"Source configuration unchanged: "+domain);
            Check(settings.Sites.Count==siteCount+1&&!result.Site.Enabled,"Only successful project registered, stopped: "+domain);
            Check(result.Files.Count==4&&result.Database.Verified&&result.Database.Tables==3&&result.Database.Rows==2&&result.Database.Views==1&&result.Database.Triggers==1&&result.Database.Routines==1&&result.Database.Events==1,"Whole database object inventory: "+from+" to "+to);
            Check(new[]{"files","connect","export","import","verify","configure"}.All(phases.Contains),"Migration progress phases");
            var db="`"+result.Site.DatabaseName+"`";Check((await Sql(to,"SELECT HEX(payload),optional IS NULL FROM "+db+".items WHERE id=1")).Trim()=="0001FF27225C\t1","Binary data and NULL preserved");
            Check((await Sql(to,"SELECT title FROM "+db+".items WHERE id=2")).Trim()=="source_db.items stays text","Data strings not rewritten as SQL");
            Check((await Sql(to,"SHOW CREATE VIEW "+db+".item_view")).Contains(result.Site.DatabaseName),"View references destination schema");
            await Sql(to,"INSERT INTO "+db+".items(title) VALUES ('copied');");Check((await Sql(to,"SELECT COUNT(*) FROM "+db+".audit")).Trim()=="1","Copied trigger executes");
            Check((await Sql(to,"CALL "+db+".item_count()")).Trim()=="3","Copied procedure uses target data");
            Check((await Sql(from,"SELECT COUNT(*) FROM source_db.items")).Trim()=="2","Source rows unchanged");
            var copiedConfig=File.ReadAllText(Path.Combine(result.Site.Directory,"config","config.php"));Check(copiedConfig.Contains(result.Site.DatabaseName)&&copiedConfig.Contains("DB_DRIVER")&&copiedConfig.Contains(settings.DatabasePort(to).ToString()),"Copied configuration points to destination");
            Check(Hash(Path.Combine(result.Site.Directory,"asset.bin"))==Hash(Path.Combine(original,"asset.bin")),"Project file content preserved");
            return result;
        }
        await Import("mysql80","mysql80","copy80.yikai");await Import("mysql57","mysql57","copy57.yikai");await Import("mysql57","mysql80","upgrade57.yikai");
        var beforeDbs=await Sql("mysql80","SHOW DATABASES");int beforeSites=settings.Sites.Count;
        try{await Import("mysql80","mysql80","fail-config.yikai",true);throw new Exception("Invalid config accepted");}catch(IOException){Check(settings.Sites.Count==beforeSites&&beforeDbs==await Sql("mysql80","SHOW DATABASES"),"Config failure rolls back only the new database");}
        try{await Import("mysql80","mysql57","fail-downgrade.yikai");throw new Exception("Downgrade accepted");}catch(IOException e){Check(e.Message.Contains("downgraded")&&settings.Sites.Count==beforeSites,"Unsupported downgrade clearly rejected before registration");}
        Configure("mysql80");var bad=new ImportCandidate{Domain="source.yikai",TargetDomain="wrong-password.yikai",Folder=original,Database=new(){Driver="mysql",Name="source_db",Port=settings.Mysql80Port,Password="incorrect"}};
        try{await transfer.ImportAsync(bad);throw new Exception("Wrong password accepted");}catch(IOException){Check(settings.Sites.Count==beforeSites&&beforeDbs==await Sql("mysql80","SHOW DATABASES"),"Wrong credentials leave target database unchanged");}
        Configure("mysql80");var blocker=new ImportCandidate{Domain="source.yikai",TargetDomain="fail-save.yikai",Folder=original,Database=new(){Kind="yikaicms",ConfigFile="config/config.php",Driver="mysql",Name="source_db",Port=settings.Mysql80Port}};
        using(var locked=new FileStream(settings.FileName,FileMode.Open,FileAccess.Read,FileShare.Read)){try{await transfer.ImportAsync(blocker);throw new Exception("Locked settings accepted");}catch(Exception e) when(e is IOException or UnauthorizedAccessException){Check(settings.Sites.Count==beforeSites&&beforeDbs==await Sql("mysql80","SHOW DATABASES")&&!Directory.Exists(Path.Combine(Root,"wwwroot","fail-save.yikai")),"Registration failure rolls back the new database and folder");}}
        var sqlite=Path.Combine(original,"source.sqlite");await Exec(Path.Combine(phpDir,"php.exe"),["-c",Path.Combine(phpDir,"php.ini"),"-r","$db=new SQLite3($argv[1]);$db->exec('CREATE TABLE items(id INTEGER PRIMARY KEY, value BLOB); INSERT INTO items VALUES(1, zeroblob(3)); CREATE VIEW item_view AS SELECT * FROM items;');",sqlite]);
        var sqliteResult=await transfer.ImportAsync(new(){Domain="sqlite.yikai",TargetDomain="sqlite.yikai",Folder=original,Database=new(){Driver="sqlite",SqlitePath=sqlite}});
        Check(sqliteResult.Database.Verified&&sqliteResult.Database.Tables==1&&sqliteResult.Database.Rows==1&&sqliteResult.Database.Views==1,"SQLite complete copy and integrity verification");
        var blank=await transfer.ImportAsync(new(){Domain="files.yikai",TargetDomain="files.yikai",Folder=original,Database=new(){Driver="none"}});
        Check(blank.Site.Database=="none"&&Hash(Path.Combine(blank.Site.Directory,"config","config.php"))==Hash(config),"Files-only preserves explicit choice");
        Check(!Directory.EnumerateFiles(Path.Combine(Root,"temp"),"transfer-*.json").Any(),"Transient credential requests removed");
        Console.WriteLine("ALL MIGRATION CHECKS PASSED");
    }
    sealed class Reporter(Action<ImportProgress> report):IProgress<ImportProgress>{public void Report(ImportProgress value)=>report(value);}
    static void Ui(){
        ApplicationConfiguration.Initialize();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Directory.CreateDirectory("evidence");
        var settings=new Settings{Root=Root};settings.Save();using var form=new MainForm(settings,new Runtime(settings),true);form.Show();Application.DoEvents();
        foreach(var language in new[]{0,1,2}){
            Desc(form).OfType<Choice>().Single(c=>c.Name=="language").SelectedIndex=language;Application.DoEvents();
            using var timer=new System.Windows.Forms.Timer{Interval=120};bool done=false;
            timer.Tick+=(_,_)=>{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form);if(dialog==null)return;timer.Stop();using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,dialog.Size));bitmap.Save(Path.Combine("evidence","import-"+language+".png"));Check(Desc(dialog).OfType<DataGridView>().Single().Columns.Cast<DataGridViewColumn>().Any(c=>c.DataPropertyName=="Status"),"Migration status column "+language);dialog.Close();done=true;};
            timer.Start();typeof(MainForm).GetMethod("ShowPhpStudyImport",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null);
            var deadline=DateTime.UtcNow.AddSeconds(8);while(!done&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(10);}Check(done,"Import dialog opens and closes "+language);
        }
    }
}

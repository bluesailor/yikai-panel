using YikaiLocal;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net;
using System.Text.Json;

partial class Program
{
    static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    static string CompileDummy(string name,string version,int exitCode)
    {
        var folder=Path.Combine(Root,"dummy builds");Directory.CreateDirectory(folder);var source=Path.Combine(folder,name+".cs");var exe=Path.Combine(folder,name+".exe");
        File.WriteAllText(source,$$"""
using System;using System.IO;using System.Threading;using System.Reflection;
[assembly: AssemblyFileVersion("{{version}}")] class Program {
static int Main(string[] args) {
if(Array.IndexOf(args,"--wait")>=0){Thread.Sleep(1200);return 0;}
var i=Array.IndexOf(args,"--root");if(i>=0)File.AppendAllText(Path.Combine(args[i+1],"launched.txt"),"{{version}}\n");
return {{exitCode}};} }
""");
        var start=new ProcessStartInfo(@"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};foreach(var arg in new[]{"/nologo","/target:winexe","/out:"+exe,source})start.ArgumentList.Add(arg);
        using var compiler=Process.Start(start)!;var output=compiler.StandardOutput.ReadToEnd();compiler.WaitForExit();Check(compiler.ExitCode==0,"Compile update fixture "+name+" "+output);return exe;
    }
    static void UpdateChecks()
    {
        var old=CompileDummy("old panel","0.5.2.0",0);var next=CompileDummy("new panel","0.7.0.0",0);var failed=CompileDummy("failed panel","0.7.0.0",1);
        var settings=new Settings{Root=Path.Combine(Root,"download test")};settings.Save();var release=new PanelRelease("0.7.0","https://example.invalid/panel.exe",Hash(next),"Fixture");
        HttpClient Client()=>new(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(next))}));
        using(var client=Client()){var download=PanelUpdate.DownloadAsync(settings,release,client,null,CancellationToken.None).GetAwaiter().GetResult();Check(Hash(download)==Hash(next),"Download accepts matching bytes and executable version");}
        using(var client=Client()){try{PanelUpdate.DownloadAsync(settings,release with{Sha256=new string('0',64)},client,null,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Bad hash accepted");}catch(InvalidDataException){Console.WriteLine("PASS Download rejects bad hash");}}
        using(var client=Client()){try{PanelUpdate.DownloadAsync(settings,release with{Version="0.8.0"},client,null,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Bad version accepted");}catch(InvalidDataException){Console.WriteLine("PASS Download rejects wrong executable version");}}
        Check(!Directory.EnumerateFiles(Path.Combine(settings.Root,"temp","updates"),"*.part").Any(),"Failed downloads remove partial files");
        foreach(var scenario in new[]{"success","rollback","tamper"})
        {
            var test=new Settings{Root=Path.Combine(Root,"helper "+scenario+" with spaces")};test.Save();Directory.CreateDirectory(Path.Combine(test.Root,"soft","panel"));var target=Path.Combine(test.Root,"soft","panel","YikaiLocal.exe");File.Copy(old,target);var originalConfig=File.ReadAllBytes(Path.Combine(test.Root,"config","panel.json"));
            var source=scenario=="rollback"?failed:next;var parentStart=new ProcessStartInfo(target){UseShellExecute=false,CreateNoWindow=true};parentStart.ArgumentList.Add("--wait");using var parent=Process.Start(parentStart)!;
            var helperStart=PanelUpdate.PrepareApply(test,source,scenario=="tamper"?new string('0',64):Hash(source),parent.Id,parent.StartTime.ToUniversalTime().Ticks,"Fixture",false);helperStart.RedirectStandardOutput=true;helperStart.RedirectStandardError=true;
            using var helper=Process.Start(helperStart)!;Check(helper.WaitForExit(20000),"Update helper finishes: "+scenario);var error=helper.StandardError.ReadToEnd();if(error!="")Console.WriteLine(error);
            var resultPath=Path.Combine(test.Root,"temp","updates","result.json");Check(File.Exists(resultPath),"Helper records outcome: "+scenario);using var json=JsonDocument.Parse(File.ReadAllText(resultPath));Check(json.RootElement.GetProperty("success").GetBoolean()==(scenario=="success"),"Expected helper outcome: "+scenario);
            Check(Hash(target)==Hash(scenario=="success"?next:old),"Installed executable is correct: "+scenario);Check(originalConfig.SequenceEqual(File.ReadAllBytes(Path.Combine(test.Root,"config","panel.json"))),"Project configuration preserved: "+scenario);
            if(scenario!="tamper")Check(Hash(Path.Combine(json.RootElement.GetProperty("backup").GetString()!,"YikaiLocal.exe"))==Hash(old),"Old executable backup preserved: "+scenario);
            Check(parent.HasExited,"Helper waits for original panel: "+scenario);
        }
    }
}

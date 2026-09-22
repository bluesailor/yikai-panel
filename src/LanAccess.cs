using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace YikaiLocal;

// 局域网访问：把项目的 Web 端口从 127.0.0.1 放开到 0.0.0.0，并可添加 Windows 防火墙入站规则。
// 数据库管理页面、MySQL 和 PHP FastCGI 始终只监听 127.0.0.1，不随这个开关对外。
public sealed partial class Runtime
{
    public string WebBind=>Settings.LanAccess?"0.0.0.0":"127.0.0.1";

    // 改变监听地址只需要重新生成配置并重载 Web 服务器，端口和项目都不变。
    public async Task SetLanAccessAsync(bool enable)
    {
        if(Settings.LanAccess==enable)return;
        Settings.LanAccess=enable;Settings.Save();
        Log("LAN access "+(enable?"on":"off"));
        if(WebAlive)await ReloadWebServer();
    }

    // 本机在局域网里的 IPv4 地址，供同事访问。
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&n.NetworkInterfaceType!=NetworkInterfaceType.Tunnel)
                .SelectMany(n=>n.GetIPProperties().UnicastAddresses)
                // 169.254.x.x 是没拿到 DHCP 时的自动地址，对同事没用。
                .Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address)&&!a.Address.ToString().StartsWith("169.254."))
                .Select(a=>a.Address.ToString()).Distinct().ToList();
        }
        catch(NetworkInformationException){return [];}
    }

    public IReadOnlyList<string> FirewallPrograms()=>new[]{NginxExecutable,ApacheExecutable}.Where(File.Exists).ToList();
    static string FirewallRule(string program)=>"Yikai Panel · "+Path.GetFileNameWithoutExtension(program);
    // 规则齐全才算“已允许”；查询不需要管理员权限。
    public bool FirewallAllowed()
    {
        var programs=FirewallPrograms();
        return programs.Count>0&&programs.All(p=>Netsh(["advfirewall","firewall","show","rule","name="+FirewallRule(p)])==0);
    }
    // 添加 / 移除入站规则：只放行面板自带的 Web 服务器程序，范围限定专用网络和域网络。需要管理员权限。
    public void ConfigureFirewall(bool allow)
    {
        foreach(var program in FirewallPrograms())
        {
            var name=FirewallRule(program);
            Netsh(["advfirewall","firewall","delete","rule","name="+name]);
            if(!allow)continue;
            var code=Netsh(["advfirewall","firewall","add","rule","name="+name,"dir=in","action=allow","program="+program,"enable=yes","profile=private,domain","protocol=TCP"]);
            if(code!=0)throw new IOException(ResetText($"添加防火墙规则失败（netsh 返回 {code}）。",$"Could not add the firewall rule (netsh exit code {code}).",$"ファイアウォール規則を追加できませんでした（netsh: {code}）。"));
        }
        Log("Firewall rules "+(allow?"added":"removed"));
    }
    static int Netsh(string[] arguments)
    {
        var info=new ProcessStartInfo("netsh"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        try
        {
            using var process=Process.Start(info)??throw new IOException("netsh could not start.");
            process.StandardOutput.ReadToEnd();process.StandardError.ReadToEnd();
            return process.WaitForExit(20000)?process.ExitCode:-1;
        }
        catch(System.ComponentModel.Win32Exception){return -1;}
    }
}

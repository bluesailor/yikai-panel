namespace YikaiLocal;

// 共用 HTTP 端口（Settings.SharedHttpPort，默认 80）：所有项目除了各自的端口，还在这个端口上按域名（server_name）监听，
// 打开网站直接是 http://域名/，和小皮面板一样。各自端口照常保留：局域网访问、127.0.0.1:端口、面板自动装 CMS 都用它。
// 这个端口被别的程序占着（IIS、小皮面板等）时自动跳过，Web 服务器照常启动，网址退回带端口的形式。
public sealed partial class Runtime
{
    // 现在能不能用共用端口：没人监听，或者正由本面板的 Web 服务器监听
    public bool SharedPortUsable()
    {
        var port=Settings.SharedHttpPort;
        if(port<=0)return false;
        if(PortDiagnostics.ListenerPid(port) is not { } pid)return true;
        return Alive(WebKey)&&PortOwnedBy(WebKey,port);
    }
    // 写 Web 服务器配置时用：0 表示这次不监听共用端口（并记一次日志说明原因）
    int SharedPortForConfig()
    {
        if(Settings.SharedHttpPort<=0)return 0;
        if(SharedPortUsable())return Settings.SharedHttpPort;
        if(PortDiagnostics.ListenerPid(Settings.SharedHttpPort) is { } holder&&holder!=sharedPortWarnedPid)
        {
            sharedPortWarnedPid=holder;
            Log(ResetText($"{Settings.SharedHttpPort} 端口被占用（{PortDiagnostics.Describe(holder).Replace("\n"," ")}），项目改用各自端口访问。",
                $"Port {Settings.SharedHttpPort} is in use ({PortDiagnostics.Describe(holder).Replace("\n"," ")}); projects use their own ports.",
                $"ポート {Settings.SharedHttpPort} は使用中です（{PortDiagnostics.Describe(holder).Replace("\n"," ")}）。各プロジェクトのポートを使います。"));
        }
        return 0;
    }
    int? sharedPortWarnedPid;
    // 能用共用端口且域名能解析到本机（.localhost 或 hosts 已同步）时，网址不带端口号
    bool UsesSharedPort(Site site)=>SharedPortUsable()&&HasHosts(site);
}

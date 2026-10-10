using System.Security.Cryptography;
using System.Text;

namespace YikaiLocal;

// PHP 进程池：同一 PHP 版本、扩展设置也相同的项目共用一组 php-cgi（以前每个项目一个进程，几十个项目就是几十个进程）。
// Windows 上一个 php-cgi 同一时间只处理一个请求，所以每组起几个进程（Settings.PhpPoolSize，默认 4，不超过组内启用的项目数），
// Nginx 用 upstream least_conn、Apache 用 balancer bybusyness 把请求交给空闲的那个；某个进程意外退出时请求自动转给其它进程。
// 改过扩展的项目按“版本 + 扩展增删”单独成组，配置互不影响。进程键：php-pool-<组>#<序号>；端口记在 panel.json 的 phpPools。
public sealed partial class Runtime
{
    public sealed record PhpPool(string Id,string Version,IReadOnlyList<(string Name,bool Zend,bool Enable)> Changes);
    const string PoolPrefix="php-pool-";

    public PhpPool PoolOf(Site site)
    {
        var changes=SiteExtensionChanges(site).OrderBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ThenBy(c=>c.Enable).ToList();
        if(changes.Count==0)return new(site.Php,site.Php,changes);
        var signature=string.Join(",",changes.Select(c=>(c.Enable?"+":"-")+c.Name.ToLowerInvariant()));
        return new(site.Php+"-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..8].ToLowerInvariant(),site.Php,changes);
    }
    static string PoolKey(string poolId,int index)=>$"{PoolPrefix}{poolId}#{index}";
    static string PoolIdOfKey(string key)=>key[PoolPrefix.Length..key.LastIndexOf('#')];
    static string PoolVersion(string poolId)=>poolId.Split('-')[0];
    public static string UpstreamName(string poolId)=>"yikai_php_"+new string(poolId.Select(c=>char.IsAsciiLetterOrDigit(c)?c:'_').ToArray());
    IEnumerable<string> PoolKeys(string poolId)=>processes.Keys.Where(k=>k.StartsWith(PoolPrefix+poolId+"#",StringComparison.Ordinal)).ToArray();
    IEnumerable<string> RunningPoolIds=>processes.Keys.Where(k=>k.StartsWith(PoolPrefix,StringComparison.Ordinal)&&Alive(k)).Select(PoolIdOfKey).Distinct().ToArray();
    bool PoolAlive(string poolId)=>PoolKeys(poolId).Any(Alive);
    // 这个项目的 PHP 是否在运行（它所在的进程池至少有一个进程活着）
    public bool PhpRunning(Site site)=>PoolAlive(PoolOf(site).Id);
    public IReadOnlyList<int> PoolPorts(string poolId)=>Settings.PhpPools.TryGetValue(poolId,out var ports)?ports:[];
    // 启用项目需要的进程池（version 为 null 表示全部版本）
    List<PhpPool> NeededPools(string? version=null)=>Settings.Sites.Where(s=>s.Enabled&&(version==null||s.Php==version)).Select(PoolOf).DistinctBy(p=>p.Id).ToList();
    int PoolSize(string poolId)=>Math.Clamp(Math.Min(Settings.PhpPoolSize,Settings.Sites.Count(s=>s.Enabled&&PoolOf(s).Id==poolId)),1,16);

    // 进程池的 php.ini：该版本的 php.ini + 本组的扩展增删 + 安装向导预填脚本（与以前每个项目的 php.ini 内容相同）
    string PoolPhpIni(PhpPool pool)
    {
        var text=File.ReadAllText(PhpIniPath(pool.Version));
        if(pool.Changes.Count>0)text=ApplyPhpExtensions(text,pool.Changes);
        var path=Path.Combine(Root,"config","panel-php-pool-"+pool.Id+".ini");
        File.WriteAllText(path,text+"\nauto_prepend_file=\""+Slash(Path.Combine(Root,"soft","db-manager","install-prefill.php"))+"\"\n",Utf8NoBom);
        return path;
    }

    // PreparePorts 调用：运行中的进程池保留端口；需要启动的进程池分配端口（尽量沿用上次的）；不再需要的记录删掉
    void PreparePoolPorts(HashSet<int> used)
    {
        var running=RunningPoolIds.ToHashSet();
        foreach(var id in running)foreach(var port in PoolPorts(id))used.Add(port);
        var needed=NeededPools();
        foreach(var pool in needed.Where(p=>!running.Contains(p.Id)))
        {
            var previous=PoolPorts(pool.Id);var ports=new List<int>();
            for(var i=0;i<PoolSize(pool.Id);i++)ports.Add(FreePort(i<previous.Count?previous[i]:9100,used));
            Settings.PhpPools[pool.Id]=ports;
        }
        foreach(var id in Settings.PhpPools.Keys.ToList())if(!running.Contains(id)&&!needed.Any(p=>p.Id==id))Settings.PhpPools.Remove(id);
    }

    // 拉起进程池里没在运行的进程，返回要等待就绪的（键, 端口）
    List<(string Key,int Port)> LaunchPool(PhpPool pool)
    {
        var started=new List<(string,int)>();var ports=PoolPorts(pool.Id);
        if(ports.Count==0||ports.Select((_,i)=>PoolKey(pool.Id,i)).All(Alive))return started;
        var ini=PoolPhpIni(pool);var executable=Path.Combine(Root,"soft","php",pool.Version,"php-cgi.exe");
        for(var i=0;i<ports.Count;i++)
        {
            var key=PoolKey(pool.Id,i);if(Alive(key))continue;
            Start(key,executable,"-c",ini,"-d",NoOpcache,"-b",$"127.0.0.1:{ports[i]}");
            started.Add((key,ports[i]));
        }
        var count=Settings.Sites.Count(s=>s.Enabled&&PoolOf(s).Id==pool.Id);
        Log($"PHP {pool.Version}{(pool.Changes.Count>0?" · "+ResetText("自定义扩展","custom extensions","カスタム拡張"):"")} · {ports.Count} {ResetText("个进程","processes","プロセス")} · {count} {ResetText("个项目","projects","プロジェクト")}");
        return started;
    }
    // 停掉进程池；不再有启用项目用它时连同端口记录一起删掉（还有人用的只是重启，端口留着下次沿用）
    async Task StopPool(string poolId)
    {
        foreach(var key in PoolKeys(poolId))await StopProcess(key);
        if(!NeededPools().Any(p=>p.Id==poolId)&&Settings.PhpPools.Remove(poolId))Settings.Save();
    }
    // 停掉不再有启用项目的进程池，以及旧版本（每项目一个进程）留下的进程
    async Task StopUnusedPhp()
    {
        var needed=NeededPools().Select(p=>p.Id).ToHashSet();
        var unusedPools=processes.Keys.Where(k=>k.StartsWith(PoolPrefix,StringComparison.Ordinal)).Select(PoolIdOfKey).Distinct().Where(id=>!needed.Contains(id)).ToList();
        var legacy=processes.Keys.Where(k=>k.StartsWith("php-")&&k!="php-db"&&!k.StartsWith(PoolPrefix,StringComparison.Ordinal)&&Alive(k)).ToList();
        if(unusedPools.Count==0&&legacy.Count==0)return;
        // 调用方刚重载过 Web 服务器（ReloadNginxServer 会等旧工作进程退出），这时停旧 PHP 不会再有请求被转给它们
        foreach(var id in unusedPools)await StopPool(id);
        foreach(var key in legacy)await StopProcess(key);
    }
    // 启动一组进程池并等待就绪；失败的进程汇总成一条错误（其余照常可用）
    async Task<List<string>> StartPools(IEnumerable<PhpPool> pools)
    {
        var waits=new List<(string Key,int Port)>();
        foreach(var pool in pools)waits.AddRange(LaunchPool(pool));
        return await WaitPorts(waits);
    }
    // 状态栏提示用：每个进程池的版本、是否自定义扩展、项目数、在运行的进程数与端口
    public sealed record PoolSummary(string Id,string Version,bool Custom,int Projects,int Alive,IReadOnlyList<int> Ports);
    public List<PoolSummary> PoolSummaries()=>NeededPools().Select(p=>new PoolSummary(p.Id,p.Version,p.Changes.Count>0,
        Settings.Sites.Count(s=>s.Enabled&&PoolOf(s).Id==p.Id),PoolKeys(p.Id).Count(Alive),PoolPorts(p.Id))).ToList();
    // Nginx upstream：每个启用项目用到的进程池一段；端口还没分配时用占位端口，保证配置检查能通过
    string NginxUpstreams()
    {
        var text=new StringBuilder();
        foreach(var id in Settings.Sites.Where(s=>s.Enabled||s.RewriteRules!=null).Select(s=>PoolOf(s).Id).Distinct())
        {
            var ports=PoolPorts(id);
            text.Append($"upstream {UpstreamName(id)} {{ least_conn; ");
            foreach(var port in ports.Count>0?ports:new[]{9})text.Append($"server 127.0.0.1:{port}; ");
            text.AppendLine("}");
        }
        return text.ToString();
    }
    // Apache balancer：与 Nginx 相同的进程池，bybusyness 选最空闲的进程
    string ApacheBalancers()
    {
        var text=new StringBuilder();
        foreach(var id in Settings.Sites.Where(s=>s.Enabled).Select(s=>PoolOf(s).Id).Distinct())
        {
            var ports=PoolPorts(id);
            text.Append($"<Proxy \"balancer://{UpstreamName(id)}\">\n");
            foreach(var port in ports.Count>0?ports:new[]{9})text.Append($"    BalancerMember \"fcgi://127.0.0.1:{port}\"\n");
            text.Append("    ProxySet lbmethod=bybusyness\n</Proxy>\n");
        }
        return text.ToString();
    }
}

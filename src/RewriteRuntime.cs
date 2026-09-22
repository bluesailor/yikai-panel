namespace YikaiLocal;

public sealed partial class Runtime
{
    public string RewriteTemplate(Site site,string name)
    {
        if(name=="default")name=site.Template=="yikaicms"?"yikaicms":"front";
        if(name=="yikaicms")return File.ReadAllText(Path.Combine(Root,"config","yikaicms-rewrite.conf")).Replace("127.0.0.1:9082","127.0.0.1:{{PHP_PORT}}").Replace("include fastcgi_params;","include \"{{FASTCGI_PARAMS}}\";");
        var route=name switch{
            "plain"=>"location / { try_files $uri $uri/ =404; }",
            "thinkphp"=>"location / { try_files $uri $uri/ /index.php?s=$uri&$query_string; }",
            "front"=>"location / { try_files $uri $uri/ /index.php?$query_string; }",
            _=>throw new ArgumentException("Unknown rewrite template.")};
        return route+"\n\nlocation ~ /\\. { deny all; }\nlocation ^~ /storage/ { deny all; }\nlocation ~ \\.php$ {\n    try_files $uri =404;\n    include \"{{FASTCGI_PARAMS}}\";\n    fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;\n    fastcgi_pass 127.0.0.1:{{PHP_PORT}};\n}\n";
    }
    string ExpandRewrite(Site site,string rules)=>rules.Replace("{{PHP_PORT}}",site.FastCgiPort.ToString()).Replace("{{FASTCGI_PARAMS}}",Slash(Path.Combine(Root,"config","fastcgi_params")));

    // Caller holds Runtime.Lock; validate stopped sites as well without starting them.
    public async Task SaveRewriteAsync(Site site,string? rules,bool checkOnly=false)
    {
        if(!Settings.Sites.Contains(site))throw new InvalidOperationException("Project no longer exists.");
        if(rules is not null && (string.IsNullOrWhiteSpace(rules)||rules.Length>131072))throw new IOException("Enter rules (maximum 128 KB), or restore the default.");
        var paths=Settings.Sites.Select(s=>Path.Combine(Root,"config","panel-rewrite-"+s.Id+".conf")).Append(NginxConfig).Append(Settings.FileName).ToArray();
        var originals=paths.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var oldRules=site.RewriteRules;var enabled=site.Enabled;bool committed=false;
        void Restore(){foreach(var pair in originals){if(pair.Value is null){if(File.Exists(pair.Key))File.Delete(pair.Key);}else File.WriteAllBytes(pair.Key,pair.Value);}}
        try
        {
            site.RewriteRules=rules;site.Enabled=true;WriteNginx();
            var nginx=Path.Combine(Root,"soft","nginx","nginx.exe");var args=new[]{"-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig)};
            await Run(nginx,[..args,"-t"]);
            site.Enabled=enabled;
            if(checkOnly)return;
            WriteNginx();Settings.Save();
            if(Alive("nginx"))await Run(nginx,[..args,"-s","reload"]);
            committed=true;
        }
        finally
        {
            site.Enabled=enabled;
            if(!committed){site.RewriteRules=oldRules;Restore();}
        }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace YikaiLocal;

public sealed record CertificateInfo(string Subject,string Issuer,DateTime NotBefore,DateTime NotAfter,bool MatchesDomain,string Thumbprint);

// 本地 HTTPS 证书：面板自建本机根证书并为项目签发证书（类似 mkcert），或使用用户导入的证书。
// 所有证书和私钥保存在 config/ssl 下；信任根证书只写入当前用户的“受信任的根证书颁发机构”，由用户确认。
public sealed partial class Runtime
{
    string SslDirectory=>Path.Combine(Root,"config","ssl");
    public string LocalCaCertificatePath=>Path.Combine(SslDirectory,"ca","yikai-local-root.crt");
    string LocalCaKeyPath=>Path.Combine(SslDirectory,"ca","yikai-local-root.key");
    public (string Certificate,string Key) CertificatePaths(Site site)
    {
        var folder=site.CertificateSource=="custom"?"custom":"sites";
        return (Path.Combine(SslDirectory,folder,site.Id+".crt"),Path.Combine(SslDirectory,folder,site.Id+".key"));
    }

    public X509Certificate2? LocalCa()
    {
        if(!File.Exists(LocalCaCertificatePath)||!File.Exists(LocalCaKeyPath))return null;
        try{return X509Certificate2.CreateFromPemFile(LocalCaCertificatePath,LocalCaKeyPath);}catch(CryptographicException){return null;}
    }

    // 只在不存在或已过期时创建；重建根证书会让之前的信任失效，所以不主动轮换。
    public X509Certificate2 EnsureLocalCa()
    {
        var existing=LocalCa();
        if(existing!=null&&existing.NotAfter>DateTime.Now.AddDays(30))return existing;
        existing?.Dispose();
        using var key=RSA.Create(2048);
        var request=new CertificateRequest($"CN=Yikai Panel Local Root ({Environment.MachineName}), O=Yikai Panel",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,true,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.CrlSign,true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey,false));
        using var ca=request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1),DateTimeOffset.Now.AddYears(10));
        Directory.CreateDirectory(Path.GetDirectoryName(LocalCaCertificatePath)!);
        File.WriteAllText(LocalCaCertificatePath,ca.ExportCertificatePem());
        File.WriteAllText(LocalCaKeyPath,key.ExportPkcs8PrivateKeyPem());
        Log("Local root certificate created · "+ca.Thumbprint);
        return LocalCa()!;
    }

    public bool LocalCaTrusted()
    {
        using var ca=LocalCa();if(ca==null)return false;
        using var store=new X509Store(StoreName.Root,StoreLocation.CurrentUser);store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint,ca.Thumbprint,false).Count>0;
    }
    // Windows 会弹出安全警告，用户确认后才加入；用户拒绝时抛出 CryptographicException。
    public void TrustLocalCa()
    {
        using var ca=EnsureLocalCa();using var publicOnly=X509CertificateLoader.LoadCertificate(ca.RawData);
        using var store=new X509Store(StoreName.Root,StoreLocation.CurrentUser);store.Open(OpenFlags.ReadWrite);store.Add(publicOnly);
        Log("Local root certificate trusted · "+ca.Thumbprint);
    }
    public void UntrustLocalCa()
    {
        using var ca=LocalCa();if(ca==null)return;
        using var store=new X509Store(StoreName.Root,StoreLocation.CurrentUser);store.Open(OpenFlags.ReadWrite);
        foreach(var item in store.Certificates.Find(X509FindType.FindByThumbprint,ca.Thumbprint,false))store.Remove(item);
        Log("Local root certificate untrusted · "+ca.Thumbprint);
    }

    // 生成 Web 服务器配置时调用：证书不可用（例如自定义证书被删除）只跳过该项目的 HTTPS 并记录日志，不影响其他项目。
    bool SslReady(Site site)
    {
        if(!site.Https||site.HttpsPort<=0)return false;
        try{EnsureSiteCertificate(site);return true;}
        catch(Exception e) when(e is IOException or CryptographicException or UnauthorizedAccessException){Log("HTTPS skipped · "+site.Domain+" · "+e.Message);return false;}
    }
    // 项目列表每次重画都要读证书，按文件修改时间缓存解析结果。
    readonly Dictionary<string,(DateTime Stamp,CertificateInfo Info)> certificateCache=new(StringComparer.OrdinalIgnoreCase);
    public CertificateInfo? SiteCertificateInfo(Site site)
    {
        var (certificate,key)=CertificatePaths(site);
        if(!File.Exists(certificate)||!File.Exists(key))return null;
        var stamp=File.GetLastWriteTimeUtc(certificate);var cacheKey=certificate+"|"+site.Domain;
        if(certificateCache.TryGetValue(cacheKey,out var cached)&&cached.Stamp==stamp)return cached.Info;
        try
        {
            using var cert=X509Certificate2.CreateFromPemFile(certificate,key);
            var info=new CertificateInfo(cert.Subject,cert.Issuer,cert.NotBefore,cert.NotAfter,cert.MatchesHostname(site.Domain),cert.Thumbprint);
            certificateCache[cacheKey]=(stamp,info);return info;
        }
        catch(CryptographicException){certificateCache.Remove(cacheKey);return null;}
    }

    // 保证项目证书可用：自动签发的证书在缺失、域名不符、非本机根证书签发或 30 天内过期时重签；自定义证书只校验存在。
    public void EnsureSiteCertificate(Site site,bool forceRenew=false)
    {
        var (certificatePath,keyPath)=CertificatePaths(site);
        if(site.CertificateSource=="custom")
        {
            if(!File.Exists(certificatePath)||!File.Exists(keyPath))throw new IOException(ResetText($"{site.Domain} 尚未导入证书文件。",$"No certificate has been imported for {site.Domain}.",$"{site.Domain} の証明書がまだありません。"));
            return;
        }
        using var ca=EnsureLocalCa();
        if(!forceRenew&&File.Exists(certificatePath)&&File.Exists(keyPath))
        {
            try
            {
                using var current=X509Certificate2.CreateFromPemFile(certificatePath,keyPath);
                if(current.MatchesHostname(site.Domain)&&current.Issuer==ca.Subject&&current.NotAfter>DateTime.Now.AddDays(30))return;
            }
            catch(CryptographicException){}
        }
        using var key=RSA.Create(2048);
        var request=new CertificateRequest($"CN={site.Domain}, O=Yikai Panel",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var names=new SubjectAlternativeNameBuilder();names.AddDnsName(site.Domain);names.AddDnsName("localhost");names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")],false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey,false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca,true,false));
        var serial=RandomNumberGenerator.GetBytes(16);serial[0]&=0x7F;
        // 浏览器要求服务器证书有效期不超过 825 天。
        using var issued=request.Create(ca,DateTimeOffset.Now.AddDays(-1),DateTimeOffset.Now.AddDays(800),serial);
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        File.WriteAllText(certificatePath,issued.ExportCertificatePem()+"\n"+ca.ExportCertificatePem());
        File.WriteAllText(keyPath,key.ExportPkcs8PrivateKeyPem());
        Log($"Certificate issued · {site.Domain} · until {issued.NotAfter:yyyy-MM-dd}");
    }

    // 导入用户证书（PEM，可含证书链）：校验私钥与证书配对且未过期后复制到面板目录；域名不匹配时返回提示但仍允许使用。
    public string? ImportCustomCertificate(Site site,string certificateFile,string keyFile)
    {
        X509Certificate2 cert;
        try{cert=X509Certificate2.CreateFromPemFile(certificateFile,keyFile);}
        catch(Exception e) when(e is CryptographicException or IOException or ArgumentException){throw new IOException(ResetText("证书或私钥无法读取，或两者不匹配（需要 PEM 格式、未加密的私钥）：","Cannot read the certificate or key, or they do not match (PEM with an unencrypted key is required): ","証明書または秘密鍵を読み込めないか、一致しません（PEM・暗号化なしの鍵が必要）：")+e.Message,e);}
        using(cert)
        {
            if(cert.NotAfter<DateTime.Now)throw new IOException(ResetText($"证书已于 {cert.NotAfter:yyyy-MM-dd} 过期。",$"The certificate expired on {cert.NotAfter:yyyy-MM-dd}.",$"証明書は {cert.NotAfter:yyyy-MM-dd} に期限切れです。"));
            var target=Path.Combine(SslDirectory,"custom");Directory.CreateDirectory(target);
            File.Copy(certificateFile,Path.Combine(target,site.Id+".crt"),true);File.Copy(keyFile,Path.Combine(target,site.Id+".key"),true);
            Log($"Custom certificate imported · {site.Domain} · until {cert.NotAfter:yyyy-MM-dd}");
            return cert.MatchesHostname(site.Domain)?null:ResetText($"注意：证书不包含域名 {site.Domain}，浏览器会提示不安全。",$"Note: the certificate does not cover {site.Domain}; browsers will warn.",$"注意：証明書に {site.Domain} が含まれていません。");
        }
    }

    // 开关项目 HTTPS 并应用：签发 / 校验证书、分配端口、保存配置，Web 服务器运行中时重新加载。调用方持有 Runtime.Lock。
    // requestedPort：用户在“HTTPS 端口”里填的端口（常用端口 443）。填了就固定使用该端口（site.PortPinned），
    // 留空则沿用原来的自动分配（8443 起，冲突时避让）。
    public async Task<string?> ConfigureHttpsAsync(Site site,bool enable,string source,string? certificateFile=null,string? keyFile=null,bool renew=false,int? requestedPort=null)
    {
        if(source is not ("auto" or "custom"))throw new ArgumentException("Unknown certificate source.");
        var previous=(site.Https,site.CertificateSource,site.HttpsPort,site.PortPinned);string? warning=null;
        try
        {
            site.CertificateSource=source;
            if(enable)
            {
                if(source=="custom"&&certificateFile!=null&&keyFile!=null)warning=ImportCustomCertificate(site,certificateFile,keyFile);
                EnsureSiteCertificate(site,renew&&source=="auto");
                if(requestedPort is {} wanted)
                {
                    var problem=Settings.PortProblem(wanted,site);
                    if(problem!=null)throw new IOException(ResetText("端口不可用，请换一个。","That port is not available.","そのポートは使用できません。"));
                    var holder=PortDiagnostics.ListenerPid(wanted);
                    if(holder!=null&&holder!=Process.GetCurrentProcess().Id&&Alive(WebKey)&&holder!=ServicePid(WebKey))
                        throw new IOException(ResetText($"端口 {wanted} 已被占用：",$"Port {wanted} is in use by ","ポート {wanted} は使用中です：")+PortDiagnostics.Describe(holder.Value).Replace("\n"," "));
                    site.HttpsPort=wanted;site.PortPinned=true;
                }
                else if(site.HttpsPort<=0||Settings.Sites.Any(s=>s!=site&&s.Https&&s.HttpsPort==site.HttpsPort))site.HttpsPort=Math.Max(8442,Settings.Sites.Where(s=>s!=site).Select(s=>s.HttpsPort).DefaultIfEmpty(8442).Max())+1;
            }
            site.Https=enable;
            if(WebAlive){PreparePorts();await ReloadWebServer();}else Settings.Save();
            Log($"HTTPS {(enable?"on":"off")} · {site.Domain}"+(enable?$" · {site.HttpsPort} · {source}":""));
            return warning;
        }
        catch
        {
            (site.Https,site.CertificateSource,site.HttpsPort,site.PortPinned)=previous;Settings.Save();
            if(WebAlive){try{await ReloadWebServer();}catch(Exception e) when(e is IOException or TimeoutException){Log("HTTPS rollback reload failed · "+e.Message);}}
            throw;
        }
    }
}

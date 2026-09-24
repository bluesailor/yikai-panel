using System.Text.Json;
using System.Text.RegularExpressions;

namespace YikaiLocal;

public enum SiteUpdateState { Available, Current, Restricted }
public sealed record SiteInstallation(string Product,string Version);
public sealed record SiteUpdateResult(SiteUpdateState State,string CurrentVersion,string? LatestVersion);

// 只读检测核心程序版本；不会执行站点代码、下载更新包或修改站点文件。
public static partial class SiteUpdates
{
    public static SiteInstallation? Detect(string directory)
    {
        if(CmsVersion.Detect(directory) is {} cms && Version.TryParse(cms,out _))
            return new SiteInstallation("YikaiCMS",cms);
        try
        {
            var path=Path.Combine(directory,"wp-includes","version.php");
            if(!File.Exists(Path.Combine(directory,"wp-admin","index.php")))return null;
            var match=WordPressVersion().Match(File.ReadAllText(path));
            if(match.Success && Version.TryParse(match.Groups[1].Value,out _))
                return new SiteInstallation("WordPress",match.Groups[1].Value);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){}
        return null;
    }

    public static async Task<SiteUpdateResult> CheckAsync(SiteInstallation installed,string domain,string language,string php,HttpClient client,CancellationToken token)
    {
        var url=installed.Product switch
        {
            // 完整的手动检查参数用于更新服务的定向版本路由；不上传站点名称或账号。
            "YikaiCMS"=>"https://update.yikaicms.com/api/update/check.php?version="+Uri.EscapeDataString(installed.Version)+"&channel=stable&domain="+Uri.EscapeDataString(domain)+"&site_name=&php="+Uri.EscapeDataString(php)+"&t="+DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "WordPress"=>"https://api.wordpress.org/core/version-check/1.7/?version="+Uri.EscapeDataString(installed.Version)+"&locale="+Uri.EscapeDataString(language=="ja"?"ja":language=="zh"?"zh_CN":"en_US"),
            _=>throw new ArgumentException("Unsupported site product.",nameof(installed))
        };
        using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>65536)throw new InvalidDataException("Update response is too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(token);
        using var bounded=new MemoryStream();var buffer=new byte[4096];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(bounded.Length+read>65536)throw new InvalidDataException("Update response is too large.");bounded.Write(buffer,0,read);}
        bounded.Position=0;using var json=await JsonDocument.ParseAsync(bounded,cancellationToken:token);
        return installed.Product=="YikaiCMS"?ParseYikaiCms(installed.Version,json.RootElement):ParseWordPress(installed.Version,json.RootElement);
    }

    public static SiteUpdateResult ParseYikaiCms(string current,JsonElement root)
    {
        if(!root.TryGetProperty("code",out var code)||code.ValueKind!=JsonValueKind.Number||!code.TryGetInt32(out var codeNumber)||codeNumber!=0||!root.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Invalid YikaiCMS update response.");
        if(data.TryGetProperty("upgrade_access",out var access)&&access.ValueKind==JsonValueKind.Object&&
           access.TryGetProperty("allowed",out var allowed)&&allowed.ValueKind==JsonValueKind.False)
            return new SiteUpdateResult(SiteUpdateState.Restricted,current,null);
        if(!data.TryGetProperty("has_update",out var hasUpdate)||hasUpdate.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Invalid YikaiCMS update response.");
        var latest=data.TryGetProperty("latest_version",out var value)?value.GetString():null;
        if(!Version.TryParse(current,out var installed)||!Version.TryParse(latest,out var available))
            throw new InvalidDataException("Invalid YikaiCMS version.");
        if(hasUpdate.GetBoolean()&&Normalize(available)<=Normalize(installed))throw new InvalidDataException("Inconsistent YikaiCMS update response.");
        return new SiteUpdateResult(hasUpdate.GetBoolean()?SiteUpdateState.Available:SiteUpdateState.Current,current,latest);
    }

    public static SiteUpdateResult ParseWordPress(string current,JsonElement root)
    {
        if(!Version.TryParse(current,out var installed)||!root.TryGetProperty("offers",out var offers)||offers.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Invalid WordPress update response.");
        if(offers.GetArrayLength()==0)throw new InvalidDataException("Empty WordPress update response.");
        Version? latest=null;
        foreach(var offer in offers.EnumerateArray())
        {
            if(offer.ValueKind!=JsonValueKind.Object||!offer.TryGetProperty("response",out var response)||response.GetString()!="upgrade")continue;
            var text=offer.TryGetProperty("current",out var version)?version.GetString():null;
            if(!Version.TryParse(text,out var candidate))continue;
            if(Normalize(candidate)>Normalize(installed)&&(latest==null||Normalize(candidate)>Normalize(latest)))latest=candidate;
        }
        return new SiteUpdateResult(latest==null?SiteUpdateState.Current:SiteUpdateState.Available,current,latest?.ToString());
    }

    static Version Normalize(Version version)=>new(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));

    [GeneratedRegex(@"\$wp_version\s*=\s*['""]([0-9]+(?:\.[0-9]+){1,3})['""]\s*;")]
    private static partial Regex WordPressVersion();
}

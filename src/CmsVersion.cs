using System.Text.RegularExpressions;

namespace YikaiLocal;

// 读取项目目录里的 YikaiCMS 版本号：config/version.php 是 CMS 的单一版本来源，
// 旧版本可能仍写在 config/config.php。按文件修改时间缓存，在线升级后自动刷新。
public static partial class CmsVersion
{
    static readonly Dictionary<string,(DateTime Stamp,string? Version)> cache=new(StringComparer.OrdinalIgnoreCase);

    public static string? Detect(string directory)
    {
        foreach(var name in new[]{"version.php","config.php"})
        {
            var path=Path.Combine(directory,"config",name);
            try
            {
                var stamp=File.GetLastWriteTimeUtc(path);
                if(stamp.Year<1700)continue;
                if(!cache.TryGetValue(path,out var entry)||entry.Stamp!=stamp)
                {
                    var match=Pattern().Match(File.ReadAllText(path));
                    entry=(stamp,match.Success?match.Groups[1].Value:null);cache[path]=entry;
                }
                if(entry.Version!=null)return entry.Version;
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){}
        }
        return null;
    }

    [GeneratedRegex(@"define\(\s*['""]CMS_VERSION['""]\s*,\s*['""]([0-9A-Za-z.\-+]{1,32})['""]")]
    private static partial Regex Pattern();
}

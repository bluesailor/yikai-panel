using System.IO;

namespace YikaiLocal;

// PHPStudy 导入入口的显示条件：本机确实有 PHPStudy 才显示（默认装在磁盘根目录下的
// phpstudy_pro / phpstudy / phpStudy 等目录）。功能本身保留，装上 PHPStudy 后下次打开菜单就会出现。
public static class PhpStudyDetection
{
    static readonly string[] Names = ["phpstudy_pro", "phpstudy", "phpStudy", "PHPStudy"];

    // 判定一个目录是不是 PHPStudy：有 Extensions 目录，或有 phpstudy 可执行文件。
    public static bool LooksLikePhpStudy(string path)
        => Directory.Exists(Path.Combine(path, "Extensions")) || Directory.Exists(Path.Combine(path, "PHPTutorial"))
           || File.Exists(Path.Combine(path, "phpstudy.exe")) || File.Exists(Path.Combine(path, "phpStudy.exe"));

    // 从候选路径里找出 PHPStudy 安装目录（便于测试：传入任意路径集合即可）
    public static List<string> Find(IEnumerable<string> candidates)
    {
        var found = new List<string>();
        foreach (var path in candidates)
        {
            try { if (Directory.Exists(path) && LooksLikePhpStudy(path)) found.Add(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found;
    }

    // 扫描各固定磁盘根目录下的常见名字
    public static List<string> Candidates()
    {
        var list = new List<string>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (IOException) { return list; }
        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                foreach (var name in Names) list.Add(Path.Combine(drive.RootDirectory.FullName, name));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    public static bool Detected() => Find(Candidates()).Count > 0;
    public static string? FirstPath()
    {
        var found = Find(Candidates());
        return found.Count > 0 ? found[0] : null;
    }
}

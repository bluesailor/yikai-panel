using System.Security.Cryptography;
using System.Text.Json;

namespace YikaiLocal;

public static class BundledDatabaseTools
{
    public static void Ensure(string root)
    {
        // 数据库页面的 PHP session 目录：随包的 phpmyadmin-php.ini 把 session.save_path 写死成
        // <root>/temp/sessions-phpmyadmin，PHP 不会自建它。缺了目录 session 起不来（session_start 返回 false），
        // CSRF 每次请求都会重新生成，备份 / 恢复 / 执行 SQL 全部被挡成 403。这里每次启动都补建一次。
        Directory.CreateDirectory(Path.Combine(root,"temp","sessions-phpmyadmin"));
        EnsureFiles(root,["common.php","index.php","install-prefill.php","adminer.php","phpstudy-transfer.php","database-transfer.php","database-scan.php","root-config-sync.php"]);
    }
    public static void EnsureImportTools(string root)=>EnsureFiles(root,["phpstudy-transfer.php","database-transfer.php","database-scan.php"]);
    static void EnsureFiles(string root,string[] names)
    {
        var assembly=typeof(BundledDatabaseTools).Assembly;
        using var knownStream=assembly.GetManifestResourceStream("YikaiLocal.Assets.DatabaseTools.previous-hashes.json")!;var known=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(knownStream)!;
        var pending=new List<(string File,byte[] Bytes)>();var directory=Path.Combine(root,"soft","db-manager");
        foreach(var name in names){
            using var stream=assembly.GetManifestResourceStream("YikaiLocal.Assets.DatabaseTools."+name)!;using var output=new MemoryStream();stream.CopyTo(output);var bytes=output.ToArray();var target=Path.Combine(directory,name);
            if(File.Exists(target)){var current=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));if(current==Convert.ToHexString(SHA256.HashData(bytes)))continue;if(!known.TryGetValue(name,out var previous)||(previous.ValueKind==JsonValueKind.Array?!previous.EnumerateArray().Any(hash=>hash.GetString()==current):previous.GetString()!=current))throw new IOException("Database tool was modified: "+name+". Restore the bundled version before continuing.");}
            pending.Add((target,bytes));
        }
        if(pending.Count==0)return;Directory.CreateDirectory(directory);var backup=Path.Combine(root,"backups","database-tools-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));Directory.CreateDirectory(backup);
        foreach(var item in pending){if(File.Exists(item.File))File.Copy(item.File,Path.Combine(backup,Path.GetFileName(item.File)));File.WriteAllBytes(item.File,item.Bytes);}
    }
}

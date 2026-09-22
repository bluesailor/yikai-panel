using System.Diagnostics;

namespace YikaiLocal;

public sealed record DirectorySizeResult(long Bytes,long Files,int Skipped,bool Limited,DateTime ScannedAt);

public static class DirectorySize
{
    // Counts file lengths, not allocated disk space or external MySQL data. Never follows child links.
    public static DirectorySizeResult Measure(string root,CancellationToken cancellation=default,int maxEntries=1000000,TimeSpan? timeLimit=null)
    {
        cancellation.ThrowIfCancellationRequested();if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        long bytes=0,files=0;int skipped=0,entries=0;var clock=Stopwatch.StartNew();var limit=timeLimit??TimeSpan.FromSeconds(30);var pending=new Stack<string>();pending.Push(root);
        while(pending.Count>0){
            cancellation.ThrowIfCancellationRequested();var dir=pending.Pop();
            try{
                foreach(var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos()){
                    cancellation.ThrowIfCancellationRequested();if(++entries>maxEntries||clock.Elapsed>limit)return new(bytes,files,skipped,true,DateTime.UtcNow);
                    try{
                        if((entry.Attributes&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                        if(entry is DirectoryInfo child)pending.Push(child.FullName);
                        else if(entry is FileInfo file){bytes=checked(bytes+file.Length);files++;}
                    }catch(Exception e) when(e is IOException or UnauthorizedAccessException){skipped++;}
                }
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException){skipped++;}
        }
        return new(bytes,files,skipped,false,DateTime.UtcNow);
    }
    public static string Format(long bytes)
    {
        string[] units=["B","KB","MB","GB","TB"];double value=bytes;var index=0;while(value>=1024&&index<units.Length-1){value/=1024;index++;}
        return value.ToString(index==0?"0":"0.##",System.Globalization.CultureInfo.InvariantCulture)+" "+units[index];
    }
}

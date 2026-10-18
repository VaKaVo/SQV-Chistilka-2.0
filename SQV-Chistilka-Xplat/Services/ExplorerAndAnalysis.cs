using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SqvChistilka.Services;

public sealed record FileEntry(string Name, string Path, bool IsDir, long Size, DateTime MTime);

public static class ExplorerService
{
    public static List<FileEntry> ListFolder(string path)
    {
        var res = new List<FileEntry>();
        foreach (var e in Directory.GetFileSystemEntries(path))
        {
            try
            {
                if (File.Exists(e))
                {
                    var fi = new FileInfo(e);
                    res.Add(new FileEntry(fi.Name, fi.FullName, false, fi.Length, fi.LastWriteTime));
                }
                else
                {
                    var di = new DirectoryInfo(e);
                    res.Add(new FileEntry(di.Name, di.FullName, true, 0, di.LastWriteTime));
                }
            }
            catch { }
        }
        res.Sort((a, b) =>
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return res;
    }
}

public static class AnalysisService
{
    public static List<(long Size, string Path)> FindLargestFiles(string root, int topN, CancellationToken ct, Action<int>? progress = null)
    {
        var heap = new PriorityQueue<(long, string), long>();
        int count = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) break;
            var cur = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(cur))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        long sz = new FileInfo(f).Length;
                        heap.Enqueue((sz, f), sz);
                        if (heap.Count > topN) heap.Dequeue();
                    }
                    catch { }
                    if (++count % 500 == 0) progress?.Invoke(count);
                }
                foreach (var d in Directory.EnumerateDirectories(cur))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        if (new DirectoryInfo(d).LinkTarget != null) continue;
                        stack.Push(d);
                    }
                    catch { }
                }
                if (++count % 500 == 0) progress?.Invoke(count);
            }
            catch { }
        }
        var out_ = new List<(long, string)>();
        while (heap.Count > 0) out_.Add(heap.Dequeue());
        out_.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        return out_;
    }

    public static List<(long Size, string Path)> FindLargestFolders(string root, CancellationToken ct, Action<int>? progress = null)
    {
        var sizes = new List<(long, string)>();
        int count = 0;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (new DirectoryInfo(d).LinkTarget != null) continue;
                    sizes.Add((CleanerService.GetDirSize(d, ct), d));
                }
                catch { }
                progress?.Invoke(++count);
            }
        }
        catch { }
        sizes.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        return sizes.Take(300).ToList();
    }
}

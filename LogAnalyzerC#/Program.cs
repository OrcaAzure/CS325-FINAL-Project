using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

class AnalysisResult
{
    public int TotalLines { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public Dictionary<string, int> ErrorMessages { get; set; } = new();
    public Dictionary<string, int> WarningMessages { get; set; } = new();

    public void Merge(AnalysisResult other)
    {
        TotalLines += other.TotalLines;
        ErrorCount += other.ErrorCount;
        WarningCount += other.WarningCount;

        foreach (var kv in other.ErrorMessages)
        {
            if (ErrorMessages.ContainsKey(kv.Key)) ErrorMessages[kv.Key] += kv.Value;
            else ErrorMessages[kv.Key] = kv.Value;
        }

        foreach (var kv in other.WarningMessages)
        {
            if (WarningMessages.ContainsKey(kv.Key)) WarningMessages[kv.Key] += kv.Value;
            else WarningMessages[kv.Key] = kv.Value;
        }
    }
}

class Program
{
    static readonly Regex ErrorRegex = new(@"\berror\b[:\-\s]*(.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex WarningRegex = new(@"\bwarning\b[:\-\s]*(.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly object MergeLock = new();

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run -- <logfile> [sequential|parallel|both]");
            return;
        }

        string file = args[0];
        string mode = args.Length >= 2 ? args[1].ToLower() : "both";

        if (!File.Exists(file))
        {
            Console.WriteLine($"File not found: {file}");
            return;
        }

        var lines = File.ReadAllLines(file);

        if (mode == "sequential")
        {
            var sw = Stopwatch.StartNew();
            var result = AnalyzeSequential(lines, true);
            sw.Stop();
            PrintReport("SEQUENTIAL ANALYSIS REPORT", result, sw.Elapsed.TotalSeconds);
        }
        else if (mode == "parallel")
        {
            var sw = Stopwatch.StartNew();
            var result = AnalyzeParallel(lines, true);
            sw.Stop();
            PrintReport("PARALLEL ANALYSIS REPORT", result, sw.Elapsed.TotalSeconds);
        }
        else
        {
            var sw1 = Stopwatch.StartNew();
            var seq = AnalyzeSequential(lines, true);
            sw1.Stop();
            PrintReport("SEQUENTIAL ANALYSIS REPORT", seq, sw1.Elapsed.TotalSeconds);

            var sw2 = Stopwatch.StartNew();
            var par = AnalyzeParallel(lines, true);
            sw2.Stop();
            PrintReport("PARALLEL ANALYSIS REPORT", par, sw2.Elapsed.TotalSeconds);

            Console.WriteLine("\n================ COMPARISON SUMMARY ================");
            Console.WriteLine($"Sequential time : {sw1.Elapsed.TotalSeconds:F6} seconds");
            Console.WriteLine($"Parallel time   : {sw2.Elapsed.TotalSeconds:F6} seconds");
            Console.WriteLine($"Speedup         : {(sw1.Elapsed.TotalSeconds / Math.Max(sw2.Elapsed.TotalSeconds, 0.000001)):F2}x");
            Console.WriteLine($"Same result     : {(ResultsMatch(seq, par) ? "YES" : "NO")}");
        }
    }

    static AnalysisResult AnalyzeSequential(string[] lines, bool showProgress)
    {
        var result = new AnalysisResult { TotalLines = lines.Length };
        int total = lines.Length;
        int step = Math.Max(1, total / 10);

        for (int i = 0; i < lines.Length; i++)
        {
            AnalyzeLine(lines[i], result);

            if (showProgress && ((i + 1) % step == 0 || i + 1 == total))
            {
                Console.WriteLine($"Sequential progress: {i + 1}/{total} ({((i + 1) * 100.0 / total):F0}%)");
            }
        }

        return result;
    }

    static AnalysisResult AnalyzeParallel(string[] lines, bool showProgress)
    {
        var overall = new AnalysisResult { TotalLines = lines.Length };
        int total = lines.Length;
        int completed = 0;

        var ranges = Partitioner.Create(0, lines.Length, Math.Max(1, lines.Length / (Environment.ProcessorCount * 4)));

        Parallel.ForEach(
            ranges,
            () => new AnalysisResult(),
            (range, state, local) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    AnalyzeLine(lines[i], local);
                }
                return local;
            },
            local =>
            {
                lock (MergeLock)
                {
                    overall.Merge(local);
                    completed += local.TotalLines;
                    if (showProgress)
                    {
                        Console.WriteLine($"Parallel progress: {completed}/{total} lines processed");
                    }
                }
            }
        );

        return overall;
    }

    static void AnalyzeLine(string line, AnalysisResult result)
    {
        string lower = line.ToLowerInvariant();

        var em = ErrorRegex.Match(line);
        if (em.Success || lower.Contains("error"))
        {
            result.ErrorCount++;
            string msg = em.Success && !string.IsNullOrWhiteSpace(em.Groups[1].Value)
                ? em.Groups[1].Value.Trim()
                : line.Trim();
            msg = NormalizeMessage(msg);
            if (result.ErrorMessages.ContainsKey(msg)) result.ErrorMessages[msg]++;
            else result.ErrorMessages[msg] = 1;
        }

        var wm = WarningRegex.Match(line);
        if (wm.Success || lower.Contains("warning"))
        {
            result.WarningCount++;
            string msg = wm.Success && !string.IsNullOrWhiteSpace(wm.Groups[1].Value)
                ? wm.Groups[1].Value.Trim()
                : line.Trim();
            msg = NormalizeMessage(msg);
            if (result.WarningMessages.ContainsKey(msg)) result.WarningMessages[msg]++;
            else result.WarningMessages[msg] = 1;
        }
    }

    static string NormalizeMessage(string text)
    {
        text = Regex.Replace(text.Trim(), @"^\[.*?\]\s*", "");
        text = Regex.Replace(text, @"^\d{4}-\d{2}-\d{2}.*?\s+", "");
        return string.IsNullOrWhiteSpace(text) ? "unknown message" : text.ToLowerInvariant();
    }

    static void PrintReport(string title, AnalysisResult result, double elapsed)
    {
        Console.WriteLine("\n============================================================");
        Console.WriteLine(title);
        Console.WriteLine("============================================================");
        Console.WriteLine($"Total lines scanned : {result.TotalLines}");
        Console.WriteLine($"Error count         : {result.ErrorCount}");
        Console.WriteLine($"Warning count       : {result.WarningCount}");
        Console.WriteLine($"Execution time      : {elapsed:F6} seconds");

        Console.WriteLine("\nTop error messages:");
        foreach (var kv in result.ErrorMessages.OrderByDescending(x => x.Value).Take(5))
        {
            Console.WriteLine($"  {kv.Value} x {kv.Key}");
        }

        Console.WriteLine("\nTop warning messages:");
        foreach (var kv in result.WarningMessages.OrderByDescending(x => x.Value).Take(5))
        {
            Console.WriteLine($"  {kv.Value} x {kv.Key}");
        }
    }

    static bool ResultsMatch(AnalysisResult a, AnalysisResult b)
    {
        return a.TotalLines == b.TotalLines
            && a.ErrorCount == b.ErrorCount
            && a.WarningCount == b.WarningCount
            && a.ErrorMessages.OrderBy(x => x.Key).SequenceEqual(b.ErrorMessages.OrderBy(x => x.Key))
            && a.WarningMessages.OrderBy(x => x.Key).SequenceEqual(b.WarningMessages.OrderBy(x => x.Key));
    }
}

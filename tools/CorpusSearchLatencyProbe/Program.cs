using System.Diagnostics;
using QuranReconciliation.Infrastructure;
if (args.Length != 2) return 64;
var startup = Stopwatch.StartNew();
var engine = new CorpusSearchRepository(args[0], args[1]);
Console.WriteLine($"Cold index verification: {startup.ElapsedMilliseconds} ms");
var cases = new (string Label, CorpusSearchQuery Query)[]
{
    ("arabic", new CorpusSearchQuery("الله", CorpusSearchLanguage.Arabic)),
    ("english", new CorpusSearchQuery("mercy", CorpusSearchLanguage.English)),
    ("bangla", new CorpusSearchQuery("আল্লাহ", CorpusSearchLanguage.Bangla)),
    ("all", new CorpusSearchQuery("Allah"))
};
foreach (var item in cases)
{
    var timings = new List<double>();
    for (int i = 0; i < 6; i++)
    {
        var clock = Stopwatch.StartNew();
        var result = engine.Search(item.Query);
        clock.Stop();
        timings.Add(clock.Elapsed.TotalMilliseconds);
        if (i == 0) Console.WriteLine($"{item.Label}: {result.Hits.Count} first-page hits");
    }
    timings.Sort();
    Console.WriteLine($"{item.Label}: median {((timings[2]+timings[3])/2):F1}ms, worst {timings[5]:F1}ms");
}
return 0;

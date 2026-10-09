using System.Diagnostics;
using System.Text;
using QuickerPlaces.Services;
using QuickerPlaces.Services.RecentFiles;

// Load-time and memory benchmark for recent-files.json (ai/261006_Activity History Plan.md §6).
//
//   dotnet run -c Release -- before   loads the version 1 file as written (a year of opens). On a build before
//                                      step 3 that is the steady state; on this build it is the one-off load
//                                      that converts the file, until the app next saves it.
//   dotnet run -c Release -- after    converts the file and saves it (its old opens go to a history folder),
//                                      then measures loading the version 2 file: this build's steady state.
//
// Workload: a year of Recent Files at 40 opens per working day (10,440 opens) over about 1,500 PDF, Word and
// Excel files, seeded so every run is the same. Today = 2026-09-25 local (UTC+10). Each mode loads the store
// 9 times and reports the last 7: load time, memory still held after a full GC, and bytes allocated while loading.
// Everything is written under the temp folder and deleted afterwards; no real store is touched.
var dir = Path.Combine(Path.GetTempPath(), "qp-bench-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var zone = TimeZoneInfo.CreateCustomTimeZone("B", TimeSpan.FromHours(10), "B", "B");
var now = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
var rng = new Random(42);
var exts = new[] { ".pdf", ".docx", ".xlsx" };
var opens = new Dictionary<string, List<DateTimeOffset>>();
var start = now.AddDays(-364);
for (var day = start; day <= now; day = day.AddDays(1))
{
    var local = TimeZoneInfo.ConvertTime(day, zone);
    if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
    for (var i = 0; i < 40; i++)
    {
        var f = rng.Next(1500);
        var path = $@"C:\Jobs\Job {f % 60:D3}\Drawings\Sheet-{f:D4}{exts[f % 3]}";
        var at = day.AddMinutes(rng.Next(8 * 60));
        (opens.TryGetValue(path, out var l) ? l : opens[path] = new()).Add(at);
    }
}
var sb = new StringBuilder();
sb.Append("{\"schemaVersion\":1,\"settings\":{\"enabled\":true,\"kinds\":[\"pdf\",\"word\",\"excel\"],\"scope\":\"everywhere\",\"trackingStartedAt\":\"2025-01-01T00:00:00+00:00\",\"resumedAt\":\"2025-01-01T00:00:00+00:00\"},\"files\":[");
sb.Append(string.Join(",", opens.Select(kv => $"{{\"path\":{System.Text.Json.JsonSerializer.Serialize(kv.Key)},\"opens\":[{string.Join(",", kv.Value.OrderBy(o => o).Select(o => $"\"{o:O}\""))}]}}")));
sb.Append("]}");
var mode = args.Length > 0 ? args[0] : "before";
var file = Path.Combine(dir, "recent-files.json");
File.WriteAllText(file, sb.ToString());
var time = new FixedTime(now, zone);

if (mode == "after")
{
    // Steady state after the change: load once with history (migrates and prunes), flush, then measure loading what was written.
    var migrating = new RecentFilesStore(new FilePlacesStorage(dir, "recent-files.json"), time,
        new QuickerPlaces.Services.History.ActivityHistory(new QuickerPlaces.Services.History.HistoryFolder(Path.Combine(dir, "History")), time, "BENCH"));
    migrating.SetScope(QuickerPlaces.Models.RecentFiles.RecentFilesScope.Everywhere); // any change saves; loading alone never writes
}

Console.WriteLine($"mode={mode} opens={opens.Values.Sum(l => l.Count)} files={opens.Count} recent-files.json={new FileInfo(file).Length / 1024.0:F0} KiB");
var times = new List<double>();
var retained = new List<long>();
var allocated = new List<long>();
for (var run = 0; run < 9; run++)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = GC.GetTotalMemory(true);
    var alloc0 = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    var store = new RecentFilesStore(new FilePlacesStorage(dir, "recent-files.json"), time);
    sw.Stop();
    var alloc1 = GC.GetAllocatedBytesForCurrentThread();
    var after = GC.GetTotalMemory(true);
    GC.KeepAlive(store);
    if (run >= 2) { times.Add(sw.Elapsed.TotalMilliseconds); retained.Add(after - before); allocated.Add(alloc1 - alloc0); }
}
static double Median(IEnumerable<double> v) { var a = v.OrderBy(x => x).ToArray(); return a[a.Length / 2]; }
Console.WriteLine($"load ms median={Median(times):F1} min={times.Min():F1} max={times.Max():F1}");
Console.WriteLine($"retained KiB median={Median(retained.Select(x => (double)x)) / 1024:F0} min={retained.Min() / 1024} max={retained.Max() / 1024}");
Console.WriteLine($"allocated KiB median={Median(allocated.Select(x => (double)x)) / 1024:F0}");
Directory.Delete(dir, true);

sealed class FixedTime(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public override TimeZoneInfo LocalTimeZone => zone;
}

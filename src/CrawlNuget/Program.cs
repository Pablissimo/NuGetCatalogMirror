using System;
using System.Net.Http;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace CrawlNuget
{
    class Program
    {
        const string ROOT_URL = "https://api.nuget.org/v3/catalog0/index.json";

        static ConcurrentQueue<string> _workQueue = new ConcurrentQueue<string>();
        static ConcurrentDictionary<string, byte> _inProgress = new ConcurrentDictionary<string, byte>();

        // Why no ConcurrentSet, why?
        static ConcurrentDictionary<string, byte> _fetchedUrls = new ConcurrentDictionary<string, byte>();

        static HttpClientHandler _httpClientHandler;
        static HttpClient _httpClient;

        static string _outFolder;

        static Regex _urlRegex;
        static string _rootAuthority;

        const int THREAD_COUNT = 64;
        const int SNAPSHOT_INTERVAL_MS = 60 * 1000;

        static int _processedCount;
        static DateTime _start = DateTime.Now;

        static async Task Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: CrawlNuget <outputPath>");
                return;
            }

            _outFolder = args[0];

            _httpClientHandler = new HttpClientHandler() 
            {
                MaxConnectionsPerServer = THREAD_COUNT
            };

            _httpClient = new HttpClient(_httpClientHandler);

            _rootAuthority = new Uri(ROOT_URL, UriKind.Absolute).GetLeftPart(UriPartial.Authority);
            _urlRegex = new Regex("\"(" + Regex.Escape(_rootAuthority) + @"[A-Za-z0-9\%\-_\.\+\/]+)" + "\"", RegexOptions.Compiled);

            var processorTasks = Enumerable.Range(1, THREAD_COUNT).Select(x => Task.Run(ProcessQueue));

            var snapshotTask = Task.Run(() => SnapshotQueueTask(SNAPSHOT_INTERVAL_MS));

            if (HasSnapshot())
            {
                InitialiseFromSnapshot();
            }
            else
            {
                // Seed the pool with the first tasks
                await ProcessUrl(ROOT_URL);
            }

            // Await the result
            await Task.WhenAll(processorTasks);
            snapshotTask.Dispose();
        }

        static void FlagAsProcessed()
        {
            Interlocked.Increment(ref _processedCount);
        }

        static string GetStatsString(int remainingTasks)
        {
            if (_processedCount > 0)
            {
                var duration = DateTime.Now - _start;
                var processedPerSecond = Math.Round(_processedCount / duration.TotalSeconds, 2);
                var estimatedRemaining = TimeSpan.FromSeconds(remainingTasks / processedPerSecond).ToString(@"dd\d\ hh\h\ mm\m\ ss\s");

                return $"{processedPerSecond}/sec, {estimatedRemaining} rem";
            }
            else
            {
                return string.Empty;
            }
        }

        static string GetQueueSnapshotFilename()
        {
            return Path.Join(_outFolder, "queue.json");
        }
        static string GetFetchedSnapshotFilename()
        {
            return Path.Join(_outFolder, "fetched.json");
        }

        static async Task SnapshotQueueTask(int snapshotIntervalMs)
        {
            while (true)
            {
                await Task.Delay(snapshotIntervalMs);
                SnapshotQueue();
            }
        }

        static void SnapshotQueue()
        {
            Console.WriteLine("Snapshotting queue and fetched list...");
            var queueItems = _workQueue.ToArray();
            var fetched = _fetchedUrls.ToArray().Select(x => x.Key).ToArray();
            var inProgressItems = _inProgress.ToArray().Select(x => x.Key).ToArray();
            var queueToSerialise = queueItems.Concat(inProgressItems);

            var tempQueuePath = Path.ChangeExtension(GetQueueSnapshotFilename(), ".tmp");
            var tempFetchedPath = Path.ChangeExtension(GetFetchedSnapshotFilename(), ".tmp");

            using (var w = new StreamWriter(tempQueuePath, false, Encoding.UTF8))
            {
                foreach (var line in queueToSerialise) {
                    w.WriteLine(line);
                }
            }

            using (var w = new StreamWriter(tempFetchedPath, false, Encoding.UTF8))
            {
                foreach (var line in fetched) {
                    w.WriteLine(line);
                }
            }

            File.Delete(GetQueueSnapshotFilename());
            File.Move(tempQueuePath, GetQueueSnapshotFilename());
            
            File.Delete(GetFetchedSnapshotFilename());
            File.Move(tempFetchedPath, GetFetchedSnapshotFilename());

            Console.WriteLine($"Queue snapshot complete, {queueItems.Length} items serialised");
            Console.WriteLine($"Fetched snapshot complete, {fetched.Length} items serialised");
        }

        static bool HasSnapshot()
        {
            return File.Exists(Path.Join(_outFolder, "queue.json"));
        }

        static void InitialiseFromSnapshot()
        {
            using (var r = new StreamReader(GetQueueSnapshotFilename(), Encoding.UTF8))
            {
                while (!r.EndOfStream)
                {
                    _workQueue.Enqueue(r.ReadLine());
                }
            }

            if (File.Exists(GetFetchedSnapshotFilename()))
            {
                using (var r = new StreamReader(GetFetchedSnapshotFilename(), Encoding.UTF8))
                {
                    while (!r.EndOfStream)
                    {
                        _fetchedUrls.TryAdd(r.ReadLine(), 0);
                    }
                }
            }
        }

        static async Task ProcessQueue()
        {
            Console.WriteLine("Worker coming up...");

            while (_workQueue.Count > 0)
            {
                if (_workQueue.TryDequeue(out var nextUrl))
                {
                    _inProgress.TryAdd(nextUrl, 0);
                    var remainingCount =_workQueue.Count;

                    Console.WriteLine($"[{remainingCount.ToString().PadLeft(8)} remaining {GetStatsString(remainingCount)}] Processing {nextUrl}");
                    await ProcessUrl(nextUrl);

                    _inProgress.TryRemove(nextUrl, out byte _);
                    FlagAsProcessed();
                    await Task.Delay(100);
                }
                else
                {
                    Console.WriteLine("Worker waiting for work...");
                    await Task.Delay(500);
                }
            }

            Console.WriteLine("Worker done");
        }

        static async Task ProcessUrl(string url) {
            string parsedUrl = new Uri(url, UriKind.Absolute).GetLeftPart(UriPartial.Path);

            if (!_fetchedUrls.TryAdd(parsedUrl, 0) || !url.StartsWith(_rootAuthority)) {
                // Already processed this, nothing to do here
                return;
            }

            try {
                var content = await _httpClient.GetStringAsync(parsedUrl);

                await SaveFile(content, parsedUrl);

                // Parse the file, find links and add them to the work stack
                // if they're not already processed
                var links = GetLinks(content).Distinct();

                foreach (var link in links)
                {
                    if (!_fetchedUrls.ContainsKey(link) && link.StartsWith(_rootAuthority))
                    {
                        _workQueue.Enqueue(link);
                    }
                }
            }
            catch (Exception e) {
                Console.WriteLine($"Failed to process {url}: {e.ToString()}");
            }
        }

        static IEnumerable<string> GetLinks(string content)
        {
            var matches = _urlRegex.Matches(content);

            return matches.Select(m => m.Groups[1].Value);
        }

        static async Task SaveFile(string content, string originalUrl) {
            var targetFolder = GetFolderForUrl(originalUrl);
            var targetFilename = GetFilenameForUrl(originalUrl);

            EnsureFolders(targetFolder);
            using (var s = new StreamWriter(Path.Join(targetFolder, targetFilename), false, Encoding.UTF8)) {
                await s.WriteAsync(content);
            }
        }

        static string GetFolderForUrl(string originalUrl) {
            var uri = new Uri(originalUrl, UriKind.Absolute);

            var authority = uri.Authority;
            var path = uri.AbsolutePath;

            var components = (authority + "/" + path).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var pathComponents = components.Take(components.Length - 1);
            
            var targetFolder = _outFolder;
            foreach (var component in pathComponents) {
                targetFolder = Path.Join(targetFolder, component);
            }

            return targetFolder;
        }

        static string GetFilenameForUrl(string originalUrl) {
             return originalUrl.Split('/').Last();
        }

        static void EnsureFolders(string targetFolder) {
            if (!Directory.Exists(targetFolder)) {
                Directory.CreateDirectory(targetFolder);
            }
        }
    }
}

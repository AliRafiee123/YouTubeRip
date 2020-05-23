using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YouTubeRip
{
    class Program
    {
        static void Main(string[] args)
        {
            string videoId = string.Empty;
            string file = string.Empty;
            string outputPath = string.Empty;
            bool testMode = false;

            for (int i = 0; i < args.Length; ++i)
            {
                var arg = args[i].ToLower();
                if (arg == "-h")
                {
                    PrintHelp();
                    return;
                }
                if (arg == "-t")
                {
                    testMode = true;
                }
                else if (arg == "-i")
                {
                    videoId = GetArg(args, ++i);
                }
                else if (arg == "-f")
                {
                    file = GetArg(args, ++i);
                }
                else if (arg == "-o")
                {
                    outputPath = GetArg(args,++i);
                }
            }
            
            if(string.IsNullOrWhiteSpace(outputPath) || 
              (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(videoId)))
            {
                Console.WriteLine("Not enough options!\n");
                PrintHelp();
                return;
            }

            if (!string.IsNullOrWhiteSpace(file) && !string.IsNullOrWhiteSpace(videoId))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Can't specify both -i and -f.\n");
                Console.ResetColor();
                PrintHelp();
                return;
            }

            Console.CursorVisible = false;
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                DownloadVideo(videoId, outputPath, testMode).GetAwaiter().GetResult();
            }
            else
            {
                DownloadVideos(file, outputPath, testMode).GetAwaiter().GetResult();
            }
            Console.CursorVisible = true;
        }

        private static string GetArg(string[] args, int index)
        {
            if (index < args.Length)
            {
                return args[index];
            }
            return string.Empty;
        }

        static void PrintHelp()
        {
            Console.WriteLine("YouTubeRip - Downloads videos from Youtube.com");
            Console.WriteLine("Options:");
            Console.WriteLine("-i <VideoId>|<VideoUrl>");
            Console.WriteLine("-f <inputfile>");
            Console.WriteLine("-o <outputpath>");
            Console.WriteLine("-t test mode, in this mode the temp files are not deleted");
            Console.WriteLine("-h prints this page");
        }

        static async Task DownloadVideo(string url, string outputPath, bool testMode)
        {
            Point? point = null;
            try
            {
                Directory.CreateDirectory(outputPath);
                var youtubeClient = new YoutubeClient();

                var video = await youtubeClient.Videos.GetAsync(url);
                point = PrintInfo(video);

                var streamManifest = await youtubeClient.Videos.Streams.GetManifestAsync(video.Id);

                // Get highest quality video-only stream
                var videoStreamInfo = streamManifest.GetVideoOnly().WithHighestVideoQuality();

                // ...or highest bitrate audio-only stream
                var audioStreamInfo = streamManifest.GetAudioOnly().WithHighestBitrate();

                var videoProgress = new Progress<double>(x => PrintProgress(point.Value, x));
                var videoFilename = Path.Combine(outputPath, SafeFilename(video.Title + "-Video." + videoStreamInfo.Container));
                var audioFilename = Path.Combine(outputPath, SafeFilename(video.Title + "-Audio." + audioStreamInfo.Container));

                // Start the download
                var videoTask = youtubeClient.Videos.Streams.DownloadAsync(videoStreamInfo, videoFilename, videoProgress);
                var audioTask = youtubeClient.Videos.Streams.DownloadAsync(audioStreamInfo, audioFilename);

                await Task.WhenAll(videoTask, audioTask);

                // Combine the Video and Audio files
                var outputFilename = Path.Combine(outputPath, SafeFilename(video.Title + "." + videoStreamInfo.Container));
                var ffmpegCmd = $"-i \"{videoFilename}\" -i \"{audioFilename}\" -preset medium -c copy -f {videoStreamInfo.Container} -threads {Environment.ProcessorCount/2} -nostdin -shortest -y \"{outputFilename}\"";

                var process = new Process();
                process.StartInfo.FileName = "ffmpeg.exe";
                process.StartInfo.Arguments = ffmpegCmd;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.Start();
                process.WaitForExit();

                // If user didn't want the files deleted
                if (!testMode)
                {
                    File.Delete(videoFilename);
                    File.Delete(audioFilename);
                }

                PrintVideoMessage(point, $"Done with {video.Title}", ConsoleColor.Green);
            }
            catch (Exception e)
            {
                PrintVideoMessage(point, e.Message, ConsoleColor.Red);
            }
        }

        static async Task DownloadVideos(string filename, string outputPath, bool testMode)
        {
            var tasks = new List<Task>();
            using (var fileStream = File.OpenText(filename))
            {
                if (fileStream == null)
                {
                    Console.WriteLine("Unable to open input file");
                }

                while (!fileStream.EndOfStream)
                {
                    var url = fileStream.ReadLine();
                    tasks.Add(DownloadVideo(url, outputPath, testMode));

                    if (tasks.Count >= Environment.ProcessorCount / 2)
                    {
                        await Task.WhenAny(tasks);
                    }
                }
            }

            await Task.WhenAll(tasks);
        }

        private static Point PrintInfo(Video videoInfo)
        {
            Point point;
            lock (Console.Out)
            {
                Console.WriteLine("Downloading:");
                Console.WriteLine($"ID: {videoInfo.Id}");
                Console.WriteLine($"Title: {videoInfo.Title}");
                Console.WriteLine($"Duration: {videoInfo.Duration}");
                point = new Point(Console.CursorLeft, Console.CursorTop);
                Console.WriteLine($"Progress: 0%\n\n");
            }

            return point;
        }

        private static void PrintProgress(Point point, double progress)
        {
            lock (Console.Out)
            {
                var left = Console.CursorLeft;
                var top = Console.CursorTop;
                Console.SetCursorPosition(point.X, point.Y);
                Console.WriteLine($"Progress: {string.Format("{0:0.00}%", progress*100)}%");
                Console.SetCursorPosition(left, top);
            }
        }

        private static void PrintVideoMessage(Point? point, string message, ConsoleColor color)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = color;
                if (point != null)
                {
                    var left = Console.CursorLeft;
                    var top = Console.CursorTop;
                    Console.SetCursorPosition(point.Value.X, point.Value.Y);
                    Console.WriteLine($"\n{message}");
                    Console.SetCursorPosition(left, top);
                }
                else
                {
                    Console.WriteLine($"{message}");
                }

                Console.ResetColor();
            }
        }

        private static string SafeFilename(string filename)
        {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

            // Builds a string out of valid chars
            return new string(filename.Where(ch => !invalidFileNameChars.Contains(ch)).ToArray());
        }
    }
}

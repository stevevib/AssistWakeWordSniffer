using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace AssistWakeWordSniffer
{
    public class Program
    {
        public static async Task Main( string[] args )
        {
            // Force the console to support emojis and special symbols.  Note that this may not work in all environments,
            // but it's worth trying for better UX.
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            LoadEnvFromDisk();

            var builder = Host.CreateApplicationBuilder( args );

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole( options =>
            {
                options.FormatterName = "clean";
            } )
            .AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>();


            var haToken = builder.Configuration["HA_TOKEN"] ?? builder.Configuration["HomeAssistant:Token"];
            var haUrl = builder.Configuration["HA_URL"] ?? builder.Configuration["HomeAssistant:Url"];

            if (string.IsNullOrEmpty( haToken ) || haToken == "YOUR_LONG_LIVED_ACCESS_TOKEN")
            {
                Console.WriteLine( "❌ ERROR: Home Assistant Token is missing." );
                return;
            }

            if (string.IsNullOrEmpty( haUrl ))
            {
                Console.WriteLine( "❌ ERROR: Home Assistant URL is missing." );
                return;
            }

            if (!IsFFmpegAvailable())
            {
                Console.WriteLine( "❌ ERROR: FFmpeg was not found in the system PATH." );
                return;
            }

            Console.WriteLine( $"✅ Sanity checks passed. Connecting to: {haUrl}" );

            int bufferSeconds = builder.Configuration.GetValue<int>( "Audio:SecondsToBuffer", 10 );

            // Calculate bytes: (16000 samples * 2 bytes) * seconds
            int bufferBytes = 16000 * 2 * bufferSeconds;
            builder.Services.AddSingleton( new RollingBuffer( bufferBytes ) );
            //builder.Services.AddSingleton( new RollingBuffer( 320000 ) );

            // Register the Background Services
            builder.Services.AddHostedService<UdpAudioListenerService>();
            builder.Services.AddHostedService<AssistListenerService>();

            using IHost host = builder.Build();
            await host.RunAsync();
        }

        private static void LoadEnvFromDisk( )
        {
            string localEnv = Path.Combine( AppContext.BaseDirectory, ".env" );

            // Check current dir or 4 levels up for dev environment
            string rootEnv = Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "..", "..", "..", "..", ".env" ) );

            string targetEnv = File.Exists( localEnv ) ? localEnv : (File.Exists( rootEnv ) ? rootEnv : null);

            if (targetEnv != null)
            {
                foreach (var line in File.ReadAllLines( targetEnv ))
                {
                    if (string.IsNullOrWhiteSpace( line ) || line.StartsWith( "#" ))
                        continue;
                    var parts = line.Split( '=', 2 );
                    if (parts.Length == 2)
                    {
                        Environment.SetEnvironmentVariable( parts[0].Trim(), parts[1].Trim() );
                    }
                }

                Console.WriteLine( $"✅ Loaded variables from: {targetEnv}" );
            }

            else
            {
                Console.WriteLine( "⚠️ WARNING: No .env file found." );
            }
        }

        private static bool IsFFmpegAvailable( )
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "ffmpeg";
                process.StartInfo.Arguments = "-version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.Start();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

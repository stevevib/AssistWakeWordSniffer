//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.Logging;
//using System.Diagnostics;
//using AssistWakeWordSniffer;
//using Microsoft.Extensions.Logging.Console;

//namespace AssistWakeWordSniffer
//{
//    public class Program
//    {
//        public static async Task Main( string[] args )
//        {
//            // Force the console to support emojis and special symbols.
//            Console.OutputEncoding = System.Text.Encoding.UTF8;

//            var builder = Host.CreateApplicationBuilder( args );

//            var settings = new AppSettings();
//            builder.Configuration.Bind( settings );
//            builder.Services.AddSingleton( settings );

//            builder.Logging.ClearProviders();
//            builder.Logging.AddConsole( options => { options.FormatterName = "clean"; } )
//                           .AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>();

//            int bufferBytes = 16000 * 2 * settings.AudioSecondsToBuffer;
//            builder.Services.AddSingleton( new RollingBuffer( bufferBytes ) );
//            builder.Services.AddSingleton<UdpAudioListenerService>();
//            builder.Services.AddHostedService( sp => sp.GetRequiredService<UdpAudioListenerService>() );
//            builder.Services.AddHostedService<AssistListenerService>();

//            using IHost host = builder.Build();
//            var logger = host.Services.GetRequiredService<ILogger<Program>>();

//            LoadEnvFromDisk( logger, settings );

//            // Re-bind configuration to pick up the newly set environment variables
//            host.Services.GetRequiredService<IConfiguration>().Bind( settings );

//            // Early Validation & Sanity Checks
//            if (string.IsNullOrEmpty( settings.HaToken ) || settings.HaToken == "YOUR_LONG_LIVED_ACCESS_TOKEN")
//            {
//                logger.LogError( $"{settings.MapIcon( "❌" )} ERROR: Home Assistant Token is missing or invalid." );
//                return;
//            }

//            if (string.IsNullOrEmpty( settings.HaUrl ))
//            {
//                logger.LogError( $"{settings.MapIcon( "❌" )} ERROR: Home Assistant URL is missing." );
//                return;
//            }

//            if (!IsFFmpegAvailable())
//            {
//                logger.LogError( $"{settings.MapIcon( "❌" )} ERROR: FFmpeg was not found in the system PATH." );
//                return;
//            }

//            logger.LogInformation( $"{settings.MapIcon( "✅" )} Sanity checks passed. Connecting to: {settings.HaUrl}" );
//            logger.LogInformation( $"{settings.MapIcon( "🚀" )} Assist Sniffer Starting..." );
//            logger.LogInformation( "" );

//            await host.RunAsync();
//        }

//        private static void LoadEnvFromDisk( ILogger logger, AppSettings settings )
//        {
//            string localEnv = Path.Combine( AppContext.BaseDirectory, ".env" );

//            // Check current dir or 4 levels up for dev environment
//            string rootEnv = Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "..", "..", "..", "..", ".env" ) );
//            string? targetEnv = File.Exists( localEnv ) ? localEnv : (File.Exists( rootEnv ) ? rootEnv : null);
//            bool loadedAny = false;

//            if (File.Exists( localEnv ))
//            {
//                try
//                {
//                    var lines = File.ReadAllLines( localEnv );

//                    foreach (var line in lines)
//                    {
//                        if (string.IsNullOrWhiteSpace( line ) || line.StartsWith( "#" ))
//                            continue;

//                        var parts = line.Split( '=', 2 );

//                        if (parts.Length == 2)
//                        {
//                            Environment.SetEnvironmentVariable( parts[0].Trim(), parts[1].Trim() );
//                        }
//                    }

//                    loadedAny = true;
//                }

//                catch (Exception ex)
//                {
//                    logger.LogError( $"{settings.MapIcon( "❌" )} Failed to read .env file: {ex.Message}" );
//                }
//            }

//            else
//            {
//                logger.LogError( $"{settings.MapIcon( "⚠️")} No .env file found. Ensure environment variables are set manually." );
//            }

//            // 1. Home Assistant Mapping
//            settings.HomeAssistant.Token = Environment.GetEnvironmentVariable( "HA_TOKEN" ) ?? settings.HomeAssistant.Token;
//            settings.HomeAssistant.Url = Environment.GetEnvironmentVariable( "HA_URL" ) ?? settings.HomeAssistant.Url;
//            settings.HomeAssistant.SatelliteId = Environment.GetEnvironmentVariable( "HA_SATELLITE_ID" ) ?? settings.HomeAssistant.SatelliteId;


//            // 2. Audio Mapping
//            settings.Audio.DeviceName = Environment.GetEnvironmentVariable( "AUDIO_DEVICE_NAME" ) ?? settings.Audio.DeviceName;

//            if (int.TryParse( Environment.GetEnvironmentVariable( "AUDIO_SECONDS_TO_BUFFER" ), out int secs ))
//                settings.Audio.SecondsToBuffer = secs;

//            // 3. Flat Settings Mapping
//            if (bool.TryParse( Environment.GetEnvironmentVariable( "DEBUG_UDP_AUDIO" ), out bool debug ))
//                settings.DebugUdpAudio = debug;

//            if (bool.TryParse( Environment.GetEnvironmentVariable( "USE_ASCII_LOGS" ), out bool ascii ))
//                settings.UseAsciiLogs = ascii;

//            if (loadedAny || !string.IsNullOrEmpty( settings.HomeAssistant.Token ))
//            {
//                logger.LogInformation( $"{settings.MapIcon( "✅" )} Environment configuration processed." );
//            }
//        }

//        private static bool IsFFmpegAvailable( )
//        {
//            try
//            {
//                using var process = new Process
//                {
//                    StartInfo = new ProcessStartInfo
//                    {
//                        FileName = "ffmpeg",
//                        Arguments = "-version",
//                        RedirectStandardOutput = true,
//                        RedirectStandardError = true,
//                        UseShellExecute = false,
//                        CreateNoWindow = true
//                    }
//                };
//                process.Start();
//                return true;
//            }
//            catch
//            {
//                return false;
//            }
//        }
//    }
//}
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using AssistWakeWordSniffer;
using Microsoft.Extensions.Logging.Console;

namespace AssistWakeWordSniffer
{
    public class Program
    {
        public static async Task Main( string[] args )
        {
            // Force the console to support emojis and special symbols.
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 1. Pre-boot Setup: We need a temporary logger and settings object to run the loader
            var loggerFactory = LoggerFactory.Create( builder => builder.AddConsole( opt => opt.FormatterName = "clean" )
                .AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>() );
            var tempLogger = loggerFactory.CreateLogger( "Startup" );
            var settings = new AppSettings();

            // 2. Load .env into Environment Variables and map to our settings object
            LoadEnvFromDisk( tempLogger, settings );

            var builder = Host.CreateApplicationBuilder( args );

            // 3. Configuration & Settings Singleton
            // We bind again to ensure any standard appsettings.json or command line args are merged
            builder.Configuration.Bind( settings );
            builder.Services.AddSingleton( settings );

            // 4. Professional Logging Setup
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole( options => { options.FormatterName = "clean"; } )
                   .AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>();

            // 5. Service Registration
            int bufferBytes = 16000 * 2 * settings.AudioSecondsToBuffer;
            builder.Services.AddSingleton( new RollingBuffer( bufferBytes ) );
            builder.Services.AddSingleton<UdpAudioListenerService>();
            builder.Services.AddHostedService( sp => sp.GetRequiredService<UdpAudioListenerService>() );
            builder.Services.AddHostedService<AssistListenerService>();

            using IHost host = builder.Build();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            // 6. Early Validation & Sanity Checks
            if (string.IsNullOrEmpty( settings.HaToken ) || settings.HaToken == "YOUR_LONG_LIVED_ACCESS_TOKEN")
            {
                logger.LogError( $"{settings.MapIcon( "❌" )} ERROR: Home Assistant Token is missing or invalid." );
                return;
            }

            if (string.IsNullOrEmpty( settings.HaUrl ))
            {
                logger.LogError( $"{settings.MapIcon( "❌" )} ERROR: Home Assistant URL is missing." );
                return;
            }

            if (!IsFFmpegAvailable())
            {
                logger.LogError( $"{settings.MapIcon( "❌" )} ERROR: FFmpeg was not found in the system PATH. Recordings will fail." );
                return;
            }

            logger.LogInformation( $"{settings.MapIcon( "✅" )} Sanity checks passed. Connecting to: {settings.HaUrl}" );
            logger.LogInformation( $"{settings.MapIcon( "🚀" )} Assist Sniffer Starting..." );
            logger.LogInformation( "" );

            await host.RunAsync();
        }

        private static void LoadEnvFromDisk( ILogger logger, AppSettings settings )
        {
            string localEnv = Path.Combine( AppContext.BaseDirectory, ".env" );
            string rootEnv = Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "..", "..", "..", "..", ".env" ) );

            bool fileExists = File.Exists( localEnv ) || File.Exists( rootEnv );
            string targetPath = File.Exists( localEnv ) ? localEnv : rootEnv;

            if (fileExists)
            {
                try
                {
                    foreach (var line in File.ReadAllLines( targetPath ))
                    {
                        if (string.IsNullOrWhiteSpace( line ) || line.StartsWith( "#" ))
                            continue;
                        var parts = line.Split( '=', 2 );
                        if (parts.Length == 2)
                            Environment.SetEnvironmentVariable( parts[0].Trim(), parts[1].Trim() );
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError( $"{settings.MapIcon( "❌" )} Failed to read .env file: {ex.Message}" );
                }
            }

            // --- Mapping Logic ---
            string? haToken = Environment.GetEnvironmentVariable( "HA_TOKEN" );

            // Only warn if missing both the file AND the critical environment variable
            if (!fileExists && string.IsNullOrEmpty( haToken ))
            {
                logger.LogWarning( $"{settings.MapIcon( "⚠️" )} No .env file found and HA_TOKEN is empty. Ensure environment variables are set manually." );
            }
            else if (fileExists)
            {
                logger.LogInformation( $"{settings.MapIcon( "✅" )} Loaded configuration from .env file." );
            }

            // Map Environment -> Settings Object
            settings.HomeAssistant.Token = haToken ?? settings.HaToken;
            settings.HomeAssistant.Url = Environment.GetEnvironmentVariable( "HA_URL" ) ?? settings.HaUrl;
            settings.HomeAssistant.SatelliteId = Environment.GetEnvironmentVariable( "HA_SATELLITE_ID" ) ?? settings.HaSatelliteId;
            settings.Audio.DeviceName = Environment.GetEnvironmentVariable( "AUDIO_DEVICE_NAME" ) ?? settings.AudioDeviceName;

            if (int.TryParse( Environment.GetEnvironmentVariable( "AUDIO_SECONDS_TO_BUFFER" ), out int secs ))
                settings.Audio.SecondsToBuffer = secs;

            if (bool.TryParse( Environment.GetEnvironmentVariable( "DEBUG_UDP_AUDIO" ), out bool debug ))
                settings.DebugUdpAudio = debug;

            if (bool.TryParse( Environment.GetEnvironmentVariable( "USE_ASCII_LOGS" ), out bool ascii ))
                settings.UseAsciiLogs = ascii;

            if (!string.IsNullOrEmpty( settings.HaToken ))
            {
                logger.LogInformation( $"{settings.MapIcon( "✅" )} Environment configuration processed." );
            }
        }

        private static bool IsFFmpegAvailable( )
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
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
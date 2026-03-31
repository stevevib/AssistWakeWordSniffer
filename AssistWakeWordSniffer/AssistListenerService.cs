using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace AssistWakeWordSniffer
{
    public class AssistListenerService : BackgroundService
    {
        private readonly ILogger<AssistListenerService> _logger;
        private readonly IConfiguration _config;
        private readonly RollingBuffer _audioBuffer;
        private readonly string _wsUrl;
        private readonly string _accessToken;
        private int _messageId = 1;

        public AssistListenerService( ILogger<AssistListenerService> logger, RollingBuffer audioBuffer, IConfiguration config )
        {
            _logger = logger;
            _config = config;
            _audioBuffer = audioBuffer;
            _wsUrl = config["HA_URL"] ?? "ws://homeassistant.local:8123/api/websocket";
            _accessToken = config["HA_TOKEN"] ?? "";
        }

        private async Task RunCleanupTask( CancellationToken ct )
        {
            _logger.LogInformation( "🧹 Auto-Cleanup Service initialized (72h retention)." );

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    string folderPath = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "captures" );
                    if (Directory.Exists( folderPath ))
                    {
                        var files = Directory.GetFiles( folderPath, "*.wav" );
                        int deletedCount = 0;
                        DateTime threshold = DateTime.Now.AddHours( -72 );

                        foreach (var file in files)
                        {
                            var info = new FileInfo( file );
                            if (info.CreationTime < threshold)
                            {
                                // Check if file is locked/still being written
                                try
                                {
                                    File.Delete( file );
                                    deletedCount++;
                                }
                                catch (IOException) { /* File in use, skip for now */ }
                            }
                        }

                        if (deletedCount > 0)
                        {
                            _logger.LogInformation( "🧹 Cleanup: Removed {count} old captures.", deletedCount );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError( "❌ Cleanup Error: {message}", ex.Message );
                }

                // Run the check every hour
                await Task.Delay( TimeSpan.FromHours( 1 ), ct );
            }
        }

        protected override async Task ExecuteAsync( CancellationToken stoppingToken )
        {
            _logger.LogInformation( "🚀 Assist Sniffer Service Started. Waiting for triggers..." );
            _ = RunCleanupTask( stoppingToken );

            while (!stoppingToken.IsCancellationRequested)
            {
                using var ws = new ClientWebSocket();
                try
                {
                    await ws.ConnectAsync( new Uri( _wsUrl ), stoppingToken );

                    if (await AuthenticateAsync( ws, stoppingToken ))
                    {
                        _logger.LogInformation( "✅ Connected and Authenticated to HA." );
                        await SubscribeToPipelineEvents( ws, stoppingToken );
                        await ListenLoop( ws, stoppingToken );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError( "❌ Connection lost: {Message}. Retrying in 5s...", ex.Message );
                    await Task.Delay( 5000, stoppingToken );
                }
            }
        }

        private async Task SubscribeToPipelineEvents( ClientWebSocket ws, CancellationToken ct )
        {
            // Pull from HA_SATELLITE_ID environment variable
            string? entityId = _config["HA_SATELLITE_ID"];

            if (string.IsNullOrEmpty( entityId ))
            {
                _logger.LogError( "❌ CONFIG ERROR: 'HA_SATELLITE_ID' is not set in .env or environment variables." );
                return;
            }

            var subscribeMsg = new
            {
                id = _messageId++,
                type = "subscribe_trigger",
                trigger = new
                {
                    platform = "state",
                    entity_id = entityId,
                    to = "listening" // Tis is usually 'listening' or 'on'
                }
            };
            await SendJsonAsync( ws, subscribeMsg, ct );
            _logger.LogInformation( "📡 Subscribed to {Entity} events", subscribeMsg.trigger.entity_id );
        }

        private async Task ListenLoop( ClientWebSocket ws, CancellationToken ct )
        {
            var buffer = new byte[1024 * 64];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync( new ArraySegment<byte>( buffer ), ct );
                var message = Encoding.UTF8.GetString( buffer, 0, result.Count );

                using var doc = JsonDocument.Parse( message );
                var root = doc.RootElement;

                // Only log if it's an error from HA
                if (root.TryGetProperty( "type", out var type ) && type.GetString() == "result")
                {
                    if (!root.GetProperty( "success" ).GetBoolean())
                    {
                        _logger.LogError( "❌ HA Subscription Failed: {Msg}", message );
                    }
                    continue; // Skip the "success: true" dross
                }

                // Handle the trigger
                if (type.GetString() == "event")
                {
                    _logger.LogInformation( "⚡ Wake Word Detected!" );
                    ProcessTrigger( root );
                }
            }
        }

        private void ProcessTrigger( JsonElement data )
        {
            _ = Task.Run( async ( ) =>
            {
                _logger.LogInformation( "⏳ Processing 10s centered clip..." );
                await Task.Delay( 3000 );

                try
                {
                    string timestamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss" );
                    string fileName = $"centered_{timestamp}.wav";
                    string folderPath = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "captures" );

                    if (!Directory.Exists( folderPath ))
                        Directory.CreateDirectory( folderPath );

                    string filePath = Path.Combine( folderPath, fileName );

                    byte[] audioData = _audioBuffer.GetLastSeconds( 10 );
                    SaveWavFile( filePath, audioData );

                    // LOG THE SAVE CLEARLY
                    _logger.LogInformation( "💾 SAVE COMPLETE: {FileName}", Path.GetFileName( filePath ) );
                }
                catch (Exception ex)
                {
                    _logger.LogError( "❌ Save Failed: {Message}", ex.Message );
                }
            } );
        }

        private void SaveWavFile( string filePath, byte[] pcmData )
        {
            using var fs = new FileStream( filePath, FileMode.Create );
            using var bw = new BinaryWriter( fs );

            // RIFF Header
            bw.Write( "RIFF".ToCharArray() );
            bw.Write( 36 + pcmData.Length );
            bw.Write( "WAVE".ToCharArray() );

            // Format Chunk
            bw.Write( "fmt ".ToCharArray() );
            bw.Write( 16 ); // Chunk size
            bw.Write( (short)1 ); // PCM format
            bw.Write( (short)1 ); // Channels (Mono)
            bw.Write( 16000 ); // Sample Rate
            bw.Write( 16000 * 2 ); // Byte Rate
            bw.Write( (short)2 ); // Block Align
            bw.Write( (short)16 ); // Bits per sample

            // Data Chunk
            bw.Write( "data".ToCharArray() );
            bw.Write( pcmData.Length );
            bw.Write( pcmData );
        }

        private void SaveCleanClip( int detectMs )
        {
            var rawData = _audioBuffer.Dump();
            int bytesToTrim = 350 * 32; // Trim the end "ding"
            int lengthToSave = Math.Max( 0, rawData.Length - bytesToTrim );

            string fileName = $"trigger_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            string path = Path.Combine( AppContext.BaseDirectory, "captures", fileName );
            Directory.CreateDirectory( Path.GetDirectoryName( path )! );

            using var fs = new FileStream( path, FileMode.Create );
            WriteWavHeader( fs, lengthToSave );
            fs.Write( rawData, 0, lengthToSave );
            _logger.LogInformation( "✅ Clip saved: {FileName}", fileName );
        }

        private void WriteWavHeader( Stream stream, int length )
        {
            using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
            writer.Write( "RIFF".ToCharArray() );
            writer.Write( 36 + length );
            writer.Write( "WAVE".ToCharArray() );
            writer.Write( "fmt ".ToCharArray() );
            writer.Write( 16 );
            writer.Write( (short)1 );
            writer.Write( (short)1 );
            writer.Write( 16000 );
            writer.Write( 16000 * 2 );
            writer.Write( (short)2 );
            writer.Write( (short)16 );
            writer.Write( "data".ToCharArray() );
            writer.Write( length );
        }

        private async Task<bool> AuthenticateAsync( ClientWebSocket ws, CancellationToken ct )
        {
            var buf = new byte[1024];
            await ws.ReceiveAsync( new ArraySegment<byte>( buf ), ct );
            await SendJsonAsync( ws, new { type = "auth", access_token = _accessToken }, ct );
            var res = await ws.ReceiveAsync( new ArraySegment<byte>( buf ), ct );
            return Encoding.UTF8.GetString( buf, 0, res.Count ).Contains( "auth_ok" );
        }

        private async Task SendJsonAsync( ClientWebSocket ws, object obj, CancellationToken ct )
        {
            await ws.SendAsync( Encoding.UTF8.GetBytes( JsonSerializer.Serialize( obj ) ), WebSocketMessageType.Text, true, ct );
        }
    }
}
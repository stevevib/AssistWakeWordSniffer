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
        private readonly AppSettings _settings;
        private readonly RollingBuffer _audioBuffer;
        private readonly UdpAudioListenerService _udpService;
        private readonly string _wsUrl;
        private readonly string _accessToken;
        private int _messageId = 1;

        public AssistListenerService(
            ILogger<AssistListenerService> logger,
            RollingBuffer audioBuffer,
            AppSettings settings,
            UdpAudioListenerService udpService )
        {
            _logger = logger;
            _settings = settings;
            _audioBuffer = audioBuffer;
            _udpService = udpService;
            _wsUrl = _settings.HaUrl ?? "ws://homeassistant.local:8123/api/websocket";
            _accessToken = _settings.HaToken ?? "";
        }

        private async Task RunCleanupTask( CancellationToken ct )
        {
            _logger.LogInformation( $"{_settings.MapIcon( "🧹" )} Auto-Cleanup Service initialized (72 hr retention)." );

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
                            _logger.LogInformation( $"{_settings.MapIcon( "🧹" )} Cleanup: Removed {deletedCount} old captures." );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation( $"{_settings.MapIcon( "❌" )} Cleanup Error: {ex.Message}" );
                }

                // Run the check every hour
                await Task.Delay( TimeSpan.FromHours( 1 ), ct );
            }
        }

        protected override async Task ExecuteAsync( CancellationToken stoppingToken )
        {
            _logger.LogInformation( $"{_settings.MapIcon( "🚀" )} Assist Sniffer Service Started. Waiting for triggers..." );
            _ = RunCleanupTask( stoppingToken );

            while (!stoppingToken.IsCancellationRequested)
            {
                using var ws = new ClientWebSocket();
                try
                {
                    await ws.ConnectAsync( new Uri( _wsUrl ), stoppingToken );

                    if (await AuthenticateAsync( ws, stoppingToken ))
                    {
                        _logger.LogInformation( $"{_settings.MapIcon( "✅" )} Connected and Authenticated to HA." );
                        await SubscribeToPipelineEvents( ws, stoppingToken );
                        await ListenLoop( ws, stoppingToken );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation( $"{_settings.MapIcon( "❌" )} Connection lost: {ex.Message}. Retrying in 5s..." );
                    await Task.Delay( 5000, stoppingToken );
                }
            }
        }

        private async Task SubscribeToPipelineEvents( ClientWebSocket ws, CancellationToken ct )
        {
            string? entityId = _settings.HaSatelliteId;

            if (string.IsNullOrEmpty( entityId ))
            {
                _logger.LogInformation( $"{_settings.MapIcon( "❌" )} CONFIG ERROR: 'HA_SATELLITE_ID' is not set in .env or environment variables." );
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
                    to = "listening" // This is usually 'listening' or 'on'
                }
            };
            await SendJsonAsync( ws, subscribeMsg, ct );
            _logger.LogInformation( $"{_settings.MapIcon( "📡" )} Subscribed to {subscribeMsg.trigger.entity_id} events" );
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
                        _logger.LogInformation( $"{_settings.MapIcon( "❌" )} HA Subscription Failed: {message}" );
                    }

                    continue;
                }

                if (type.GetString() == "event")
                {
                    _logger.LogInformation( "" );
                    _logger.LogInformation( $"{_settings.MapIcon( "⚡" )} Wake Word Detected!" );
                    ProcessTrigger( root );
                }
            }
        }

        private void ProcessTrigger( JsonElement data )
        {
            _ = Task.Run( async ( ) =>
            {
                _logger.LogInformation( "⏳ Processing 12s centered clip..." );

                // We wait 5.5 seconds to ensure the 5 seconds of post-trigger audio 
                // is actually finished and sitting in the buffer.
                await Task.Delay( 5500 );

                try
                {
                    string timestamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss" );
                    string fileName = $"centered_{timestamp}.wav";
                    string folderPath = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "captures" );

                    if (!Directory.Exists( folderPath ))
                        Directory.CreateDirectory( folderPath );

                    string filePath = Path.Combine( folderPath, fileName );

                    // Grab 12 seconds instead of 10.
                    // This accounts for ~2s of wake-word duration + 5s pre + 5s post.
                    byte[] audioData = _audioBuffer.GetLastSeconds( 12 );
                    SaveWavFile( filePath, audioData );

                    _logger.LogInformation( $"{_settings.MapIcon( "💾" )} SAVE COMPLETE: {Path.GetFileName( filePath )}" );

                    // Log the volume stats for this interaction
                    string stats = _udpService.GetAudioStatsSummary();
                    _logger.LogInformation( stats );
                }
                catch (Exception ex)
                {
                    _logger.LogInformation( $"{_settings.MapIcon( "❌" )} Save Failed: {ex.Message}");
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

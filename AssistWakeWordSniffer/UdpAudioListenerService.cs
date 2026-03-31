using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssistWakeWordSniffer
{
    public class UdpAudioListenerService : BackgroundService
    {
        private readonly RollingBuffer _buffer;
        private readonly ILogger<UdpAudioListenerService> _logger;
        private readonly int _port = 1234; // Matches PS test command
        private static readonly object _consoleLock = new();

        public UdpAudioListenerService( RollingBuffer buffer, ILogger<UdpAudioListenerService> logger )
        {
            _buffer = buffer;
            _logger = logger;
        }

        protected override async Task ExecuteAsync( CancellationToken stoppingToken )
        {
            using var udpClient = new UdpClient( _port );
            _logger.LogInformation( "👂 UDP Sink Active. Listening on port {Port}...", _port );

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // ReceiveAsync returns a UdpReceiveResult containing the byte[]
                    var result = await udpClient.ReceiveAsync( stoppingToken );
                    byte[] data = result.Buffer;

                    // Fill the buffer for the sniffer
                    _buffer.Write( data, data.Length );
                    UpdateLevelMeter( data );
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError( ex, "❌ UDP Listener Error" );
            }
        }

        private void UpdateLevelMeter( byte[] data )
        {
            short max = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                if (i + 1 >= data.Length)
                    break;
                short sample = BitConverter.ToInt16( data, i );
                if (Math.Abs( sample ) > max)
                    max = (short)Math.Abs( sample );
            }

            int barLength = (max * 30) / 32768;
            string bar = new string( '█', barLength ).PadRight( 30, '░' );
            string meterText = $"Volume: [{bar}] {max:D5}";

            lock (_consoleLock)
            {
                // Store current position
                int currentLeft = Console.CursorLeft;
                int currentTop = Console.CursorTop;

                // Calculate the absolute bottom of the VISIBLE window
                // In some terminals, BufferHeight is huge, but WindowHeight is small.
                int lastLine = Console.CursorTop >= Console.WindowHeight
                    ? Console.CursorTop  // If we've scrolled, use current top
                    : Console.WindowHeight - 1;

                try
                {
                    Console.CursorVisible = false;
                    Console.SetCursorPosition( 0, lastLine );

                    // Use BackgroundColor to make the meter stand out from the logs
                    Console.BackgroundColor = ConsoleColor.DarkBlue;
                    Console.ForegroundColor = ConsoleColor.White;

                    Console.Write( meterText.PadRight( Console.WindowWidth - 1 ) );

                    Console.ResetColor();

                    // Restore cursor
                    if (currentTop < lastLine)
                    {
                        Console.SetCursorPosition( currentLeft, currentTop );
                    }
                }

                catch { /* Ignore cursor errors in certain restricted shells */ }
            }
        }
    }
}
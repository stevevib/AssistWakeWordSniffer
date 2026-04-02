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
        private readonly AppSettings _settings;
        private readonly int _port = 1234;

        // Statistics tracking for the audio stream
        private short _peak = 0;
        private double _sumSquare = 0;
        private long _sampleCount = 0;
        private short _min = short.MaxValue;

        /// <summary>
        /// Tracks the exact time the last UDP packet was received.
        /// Used by AssistListenerService to verify audio is still flowing before saving.
        /// </summary>
        public DateTime LastPacketTime { get; private set; } = DateTime.MinValue;

        public UdpAudioListenerService( RollingBuffer buffer, ILogger<UdpAudioListenerService> logger, AppSettings settings )
        {
            _buffer = buffer;
            _logger = logger;
            _settings = settings;
        }

        protected override async Task ExecuteAsync( CancellationToken stoppingToken )
        {
            using var udpClient = new UdpClient( _port );

            _logger.LogInformation( $"{_settings.MapIcon( "👂" )} UDP Sink Active. Listening on Port: {_port}" );

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = await udpClient.ReceiveAsync( stoppingToken );
                    byte[] data = result.Buffer;

                    // Update the timestamp for the staleness check
                    LastPacketTime = DateTime.Now;

                    // Write incoming raw PCM data to the rolling buffer
                    _buffer.Write( data, data.Length );

                    // Track stats (Peak, RMS, Min) for the current audio stream window
                    ProcessAudioStats( data );
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError( ex, $"{_settings.MapIcon( "❌" )} UDP Listener Error" );
            }
        }

        private void ProcessAudioStats( byte[] data )
        {
            // Audio is 16-bit PCM (2 bytes per sample)
            for (int i = 0; i < data.Length; i += 2)
            {
                if (i + 1 >= data.Length)
                    break;

                short sample = BitConverter.ToInt16( data, i );
                short absSample = Math.Abs( sample );

                // Update peak value for the current window
                if (absSample > _peak)
                    _peak = absSample;

                // Update minimum value (noise floor detection)
                if (absSample < _min)
                    _min = absSample;

                // Accumulate for RMS calculation
                _sumSquare += (double)sample * sample;
                _sampleCount++;
            }
        }

        /// <summary>
        /// Generates a formatted summary of audio levels since the last trigger.
        /// Respects the ASCII vs Emoji preference defined in AppSettings.
        /// </summary>
        public string GetAudioStatsSummary( )
        {
            if (_sampleCount == 0)
                return "Levels -> No data received.";

            double rms = Math.Sqrt( _sumSquare / _sampleCount );

            // Convert values to percentages (16-bit PCM max is 32767)
            double peakPct = (_peak / 32767.0) * 100;
            double rmsPct = (rms / 32767.0) * 100;
            double minPct = (_min / 32767.0) * 100;

            // Reset stats for the next capture window
            _peak = 0;
            _min = short.MaxValue;
            _sumSquare = 0;
            _sampleCount = 0;

            string icon = _settings.MapIcon( "📊" );
            string warnIcon = _settings.MapIcon( "⚠️" );

            // Highlight clipping if levels hit the digital ceiling
            string warning = peakPct >= 99 ? $" {warnIcon} CLIPPING!" : "";

            return $"{icon} Levels -> Peak: {peakPct:F0}% | RMS: {rmsPct:F0}% | Min: {minPct:F1}%{warning}";
        }
    }
}

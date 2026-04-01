using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
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

        private short _peak = 0;
        private double _sumSquare = 0;
        private long _sampleCount = 0;
        private short _min = short.MaxValue;

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

                    // Fill the buffer for the sniffer
                    _buffer.Write( data, data.Length );

                    // Track stats for the current audio stream
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
            for (int i = 0; i < data.Length; i += 2)
            {
                if (i + 1 >= data.Length)
                    break;

                short sample = BitConverter.ToInt16( data, i );
                short absSample = Math.Abs( sample );

                // Peak Detection
                if (absSample > _peak)
                    _peak = absSample;

                // Min/Noise Floor Detection
                if (absSample < _min)
                    _min = absSample;

                // RMS Calculation components
                _sumSquare += (double)sample * sample;
                _sampleCount++;
            }
        }

        /// <summary>
        /// Call this method when a .wav file is saved to get the 0-100% summary line.
        /// </summary>
        public string GetAudioStatsSummary( )
        {
            if (_sampleCount == 0)
                return "Levels -> No data received.";

            double rms = Math.Sqrt( _sumSquare / _sampleCount );

            // Convert to 0-100% (based on 16-bit PCM max of 32767)
            double peakPct = (_peak / 32767.0) * 100;
            double rmsPct = (rms / 32767.0) * 100;
            double minPct = (_min / 32767.0) * 100;

            // Reset for next capture
            _peak = 0;
            _min = short.MaxValue;
            _sumSquare = 0;
            _sampleCount = 0;

            string warning = peakPct >= 99 ? $" {_settings.MapIcon( "⚠️" )} CLIPPING!" : "";
            return $"{_settings.MapIcon( "📊" )} Levels -> Peak: {peakPct:F0}% | RMS: {rmsPct:F0}% | Min: {minPct:F1}%{warning}";
        }
    }
}
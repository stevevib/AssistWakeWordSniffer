using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistWakeWordSniffer
{
    public class AppSettings
    {
        // Matches the "HomeAssistant" section in appsettings.json
        public HomeAssistantSettings HomeAssistant { get; set; } = new();

        // Matches the "Audio" section in appsettings.json
        public AudioSettings Audio { get; set; } = new();

        // Root level settings
        public bool DebugUdpAudio { get; set; } = false;
        public bool UseAsciiLogs { get; set; } = false;

        // Flattened helpers for backward compatibility with the services
        public string HaUrl => HomeAssistant.Url;
        public string HaToken => HomeAssistant.Token;
        public string HaSatelliteId => HomeAssistant.SatelliteId;
        public string AudioDeviceName => Audio.DeviceName;
        public int AudioSecondsToBuffer => Audio.SecondsToBuffer;

        public class HomeAssistantSettings
        {
            public string Url { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
            public string SatelliteId { get; set; } = string.Empty;
        }

        public class AudioSettings
        {
            public string DeviceName { get; set; } = string.Empty;
            public int SecondsToBuffer { get; set; } = 20;
        }

        /// <summary>
        /// Centralized helper to get either an Emoji or an ASCII tag based on user settings.
        /// </summary>
        public string MapIcon( string emoji )
        {
            if (!UseAsciiLogs)
                return emoji;

            return emoji switch
            {
                "✅" => "[OK]",
                "📡" => "[NET]",
                "⚡" => "[TRG]",
                "⏳" => "[WAIT]",
                "💾" => "[SAVE]",
                "⚠️" => "[WARN]",
                "❌" => "[ERR]",
                "📊" => "[STATS]",
                "👂" => "[LISTEN]",
                "🚀" => "[START]",
                "🧹" => "[CLEAN]",
                "⚙️" => "[SYSTEM]",
                _ => $"[{emoji}]"
            };
        }
    }
}

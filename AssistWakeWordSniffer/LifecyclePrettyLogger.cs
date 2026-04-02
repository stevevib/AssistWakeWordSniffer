using Microsoft.Extensions.Logging;

namespace AssistWakeWordSniffer
{
    public class LifecyclePrettyLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly AppSettings _settings;
        private const string SystemIcon = "⚙️"; // Gear icon for system/host messages

        public LifecyclePrettyLogger( ILogger inner, AppSettings settings )
        {
            _inner = inner;
            _settings = settings;
        }

        public void Log<TState>( LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter )
        {
            var originalMessage = formatter( state, exception );

            // Prefix every system message with the system icon
            var formattedMessage = $"{_settings.MapIcon( SystemIcon )} {originalMessage}";

            // Forward to the real console logger
            _inner.Log( logLevel, eventId, formattedMessage, exception, ( s, e ) => s.ToString()! );
        }

        public bool IsEnabled( LogLevel logLevel ) => _inner.IsEnabled( logLevel );
        public IDisposable? BeginScope<TState>( TState state ) where TState : notnull => _inner.BeginScope( state );
    }

    public class LifecyclePrettyLoggerProvider : ILoggerProvider
    {
        private readonly AppSettings _settings;
        private readonly ILoggerFactory _factory;

        public LifecyclePrettyLoggerProvider( AppSettings settings )
        {
            _settings = settings;
            // Create the underlying factory that actually talks to the Console
            _factory = LoggerFactory.Create( builder =>
                builder.AddConsole( options => options.FormatterName = "clean" )
                       .AddConsoleFormatter<CleanConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>() );
        }

        public ILogger CreateLogger( string categoryName )
        {
            var logger = _factory.CreateLogger( categoryName );

            // We only want to "beautify" the Microsoft Hosting lifecycle messages
            if (categoryName.StartsWith( "Microsoft.Hosting.Lifetime" ))
            {
                return new LifecyclePrettyLogger( logger, _settings );
            }

            return logger;
        }

        public void Dispose( ) => _factory.Dispose();
    }
}
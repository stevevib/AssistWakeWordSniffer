using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace AssistWakeWordSniffer;

public class CleanConsoleFormatter : ConsoleFormatter
{
    public CleanConsoleFormatter( ) : base( "clean" ) { }

    public override void Write<TState>( in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter )
    {
        // Get just the raw message string
        string message = logEntry.Formatter( logEntry.State, logEntry.Exception );
        if (string.IsNullOrEmpty( message ))
            return;

        // Create a clean timestamp
        string timestamp = DateTime.Now.ToString( "HH:mm:ss " );

        // Write ONLY the timestamp and message
        // Ignore LogLevel (info:) and Category (Namespace.Service)
        textWriter.WriteLine( $"{timestamp}{message}" );
    }
}
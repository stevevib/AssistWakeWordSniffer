using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace AssistWakeWordSniffer
{
    public class CleanConsoleFormatter : ConsoleFormatter
    {
        public CleanConsoleFormatter( ) : base( "clean" ) { }

        public override void Write<TState>( in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter )
        {
            string message = logEntry.Formatter( logEntry.State, logEntry.Exception );

            if (string.IsNullOrEmpty( message ) || message == Environment.NewLine)
            {
                textWriter.WriteLine();
                return;
            }

            string timestamp = DateTime.Now.ToString( "HH:mm:ss " );
            textWriter.WriteLine( $"{timestamp}{message}" );
        }
    }
}
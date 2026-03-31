using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistWakeWordSniffer
{
    public class RollingBuffer
    {
        private readonly byte[] _buffer;
        private int _head; // This points to the NEXT write location (the oldest data)
        private readonly object _lock = new();

        public RollingBuffer( int sizeInBytes ) => _buffer = new byte[sizeInBytes];

        public void Write( byte[] data, int length )
        {
            Write( data.AsSpan( 0, length ) );
        }

        public void Write( ReadOnlySpan<byte> data )
        {
            lock (_lock)
            {
                // Optimized block copy would be faster, but foreach works with the % logic
                foreach (var b in data)
                {
                    _buffer[_head] = b;
                    _head = (_head + 1) % _buffer.Length;
                }
            }
        }

        /// <summary>
        /// Returns the full buffer contents, re-aligned so index 0 is the oldest data.
        /// </summary>
        public byte[] Dump( )
        {
            lock (_lock)
            {
                var result = new byte[_buffer.Length];
                
                // Copy from head to end (the older half)
                Array.Copy( _buffer, _head, result, 0, _buffer.Length - _head );
                
                // Copy from beginning to head (the newer half)
                Array.Copy( _buffer, 0, result, _buffer.Length - _head, _head );
                return result;
            }
        }

        /// <summary>
        /// Grabs the most recent X seconds of audio, correctly handling the circular wrap-around.
        /// </summary>
        public byte[] GetLastSeconds( int seconds )
        {
            int bytesPerSecond = 16000 * 2;
            int bytesToCopy = Math.Min( seconds * bytesPerSecond, _buffer.Length );
            byte[] result = new byte[bytesToCopy];

            lock (_lock)
            {
                // The 'head' is the oldest data/next write spot. 
                // To get the LATEST data, we look at what was JUST written (head - 1).
                // To get the start of our slice, we go back 'bytesToCopy' from 'head'.
                int readPos = (_head - bytesToCopy + _buffer.Length) % _buffer.Length;

                // Two-part copy to handle the wrap-around point in the middle of the slice
                int firstPartLen = Math.Min( bytesToCopy, _buffer.Length - readPos );
                Array.Copy( _buffer, readPos, result, 0, firstPartLen );

                if (firstPartLen < bytesToCopy)
                {
                    // Copy the remaining bytes from the start of the circular buffer
                    Array.Copy( _buffer, 0, result, firstPartLen, bytesToCopy - firstPartLen );
                }
            }

            return result;
        }
    }
}
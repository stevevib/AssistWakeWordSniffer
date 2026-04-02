using System;

namespace AssistWakeWordSniffer
{
    /// <summary>
    /// A thread-safe circular buffer for storing raw PCM audio data.
    /// Optimized with Array.Copy for high-performance block writes and reads.
    /// </summary>
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
                int dataLen = data.Length;

                // If the data is larger than the entire buffer, just take the last part
                if (dataLen >= _buffer.Length)
                {
                    data.Slice( dataLen - _buffer.Length ).CopyTo( _buffer );
                    _head = 0;
                    return;
                }

                int spaceToEnd = _buffer.Length - _head;
                if (dataLen <= spaceToEnd)
                {
                    // Fits in one go
                    data.CopyTo( _buffer.AsSpan( _head ) );
                }
                else
                {
                    // Wraps around the end
                    data.Slice( 0, spaceToEnd ).CopyTo( _buffer.AsSpan( _head ) );
                    data.Slice( spaceToEnd ).CopyTo( _buffer.AsSpan( 0 ) );
                }

                _head = (_head + dataLen) % _buffer.Length;
            }
        }

        /// <summary>
        /// Wipes the buffer. Used when audio goes stale to prevent 
        /// old audio from being mixed with new audio once the bridge recovers.
        /// </summary>
        public void Clear( )
        {
            lock (_lock)
            {
                Array.Clear( _buffer, 0, _buffer.Length );
                _head = 0;
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

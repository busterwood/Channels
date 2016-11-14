using System;
using System.Runtime.Serialization;

namespace BusterWood.Channels
{
    /// <summary>
    /// Thrown when a call to <see cref="Channel{T}.SendAsync(T)"/> is made when the channel has been closed.
    /// </summary>
    [Serializable]
    public class ClosedChannelException : Exception
    {
        public ClosedChannelException()
        {
        }

        public ClosedChannelException(string message) : base(message)
        {
        }

        public ClosedChannelException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ClosedChannelException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
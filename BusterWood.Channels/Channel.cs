using System.Threading;
using System.Threading.Tasks;

namespace BusterWood.Channels
{
    /// <summary>A channel for communicating between two asynchronous threads.</summary>
    public class Channel<T>
    {
        readonly object _gate = new object();
        Sender<T> _senders;
        Receiver<T> _receiver;
        CancellationToken _closed;

        /// <summary>Closing a channel prevents any further values being sent and will cancel the tasks of any waiting receviers, <see cref="ReceiveAsync"/></summary>
        public void Close()
        {
            lock (_gate)
            {
                if (_closed.IsCancellationRequested)
                    return;
                var source = new CancellationTokenSource();
                _closed = source.Token;
                source.Cancel();
                CancelAllWaitingReceivers();
            }
        }

        private void CancelAllWaitingReceivers()
        {
            for (var r = _receiver; r != null; r = r.Next)
                r.TrySetCanceled(_closed);
            _receiver = null;
        }

        /// <summary>Tries to send a value to a waiting receiver.</summary>
        /// <param name="value">the value to send</param>
        /// <returns>TRUE if the value was sent, FALSE if the channel was closed or there was no waiting receivers</returns>
        public bool TrySend(T value)
        {
            lock (_gate)
            {
                if (_closed.IsCancellationRequested)
                    return false;
                var receiver = RemoveReceiver();
                if (receiver == null)
                    return false;
                return receiver.TrySetResult(value);
            }
        }

        /// <summary>Sends a value to receiver, waiting until a receiver is ready to receive</summary>
        /// <param name="value">the value to send</param>
        /// <returns>A task that completes when the value has been sent to a receiver</returns>
        /// <exception cref="ClosedChannelException">The returned Task may be faulted with this exception when the channel is closed or has been closed whilst the send was waiting for a receiver</exception>
        public Task SendAsync(T value)
        {
            lock(_gate)
            {
                if (_closed.IsCancellationRequested)
                    return Task.FromException(new ClosedChannelException());
                var receiver = RemoveReceiver();
                if (receiver == null)
                    return AddSender(value).Task;
                receiver.TrySetResult(value);
                return Task.CompletedTask;
            }
        }

        private Receiver<T> RemoveReceiver()
        {
            var r = _receiver;
            if (r != null)
            {
                _receiver = r.Next;
                r.Next = null;
            }
            return r;
        }

        private Sender<T> AddSender(T value)
        {
            var sender = new Sender<T>(value);
            if (_senders == null)
                _senders = sender;
            else
                AddSenderToEndOfList(sender);
            return sender;
        }

        private void AddSenderToEndOfList(Sender<T> sender)
        {
            var s = _senders;
            while (s.Next != null)
                s = s.Next;
            s.Next = sender;
        }

        /// <summary>Tries to receive a value from a waiting sender.</summary>
        /// <param name="value">the value that was received, or default(T) when no sender is ready</param>
        /// <returns>TRUE if a sender was ready and <paramref name="value"/> is set, otherwise returns FALSE</returns>
        public bool TryReceive(out T value)
        {
            lock (_gate)
            {
                var sender = RemoveSender();
                if (sender == null)
                {
                    value = default(T);
                    return false;
                }
                value = sender.Value;
                sender.TrySetResult(true);
                return true;
            }
        }

        /// <summary>Receives a value, waiting for a sender is one is not ready</summary>
        /// <returns>A task that completes with a result when a sender is ready.  The task may also be cancelled is the channel is closed and there are no waiting senders</returns>
        public Task<T> ReceiveAsync()
        {
            lock (_gate)
            {
                var sender = RemoveSender();
                if (sender == null)
                {
                    if (_closed.IsCancellationRequested)
                        return Task.FromCanceled<T>(_closed);
                    return AddReceiver().Task;
                }
                var value = sender.Value;
                sender.TrySetResult(true);
                return Task.FromResult(value);
            }
        }

        private Sender<T> RemoveSender()
        {
            var s = _senders;
            if (s != null)
            {
                _senders = s.Next;
                s.Next = null;
            }
            return s;
        }

        private Receiver<T> AddReceiver()
        {
            var r = new Receiver<T>();
            if (_receiver == null)
                _receiver = r;
            else
                AddReceiverToEndOfList(r);
            return r;
        }

        private void AddReceiverToEndOfList(Receiver<T> receiver)
        {
            var r = _receiver;
            while (r.Next != null)
                r = r.Next;
            r.Next = receiver;
        }
    }

    class Sender<T> : TaskCompletionSource<bool>
    {
        public Sender<T> Next; // linked list
        public readonly T Value;

        public Sender(T value) : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            Value = value;
        }
    }

    class Receiver<T> : TaskCompletionSource<T>
    {
        public Receiver<T> Next; // linked list

        public Receiver() : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
        }
    }
}

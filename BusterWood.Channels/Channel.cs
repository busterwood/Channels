using System;
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
        Waiter _receiverWaiters;
        CancellationToken _closed;

        /// <summary>Has <see cref="Close"/> been called to shut down the channel?</summary>
        public bool IsClosed
        {
            get { return _closed.IsCancellationRequested; }
        }

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

        /// <summary>Synchronously sends a value to receiver, waiting until a receiver is ready to receive</summary>
        /// <param name="value">the value to send</param>
        /// <exception cref="OperationCanceledException">thrown when the channel <see cref="IsClosed"/></exception>
        public void Send(T value)
        {
            try
            {
                SendAsync(value).Wait();
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>Asynchronously sends a value to receiver, waiting until a receiver is ready to receive</summary>
        /// <param name="value">the value to send</param>
        /// <returns>A task that completes when the value has been sent to a receiver.  The returned task may be cancelled if the channel is closed</returns>
        public Task SendAsync(T value)
        {
            lock(_gate)
            {
                if (_closed.IsCancellationRequested)
                    return Task.FromCanceled(_closed);
                var receiver = RemoveReceiver();
                if (receiver != null)
                {
                    receiver.TrySetResult(value);
                    return Task.CompletedTask;
                }
                if (_receiverWaiters != null)
                    TriggerReceiverWaiter();
                return AddSender(value).Task;
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

        private void TriggerReceiverWaiter()
        {
            var rw = _receiverWaiters;
            _receiverWaiters = rw.Next;
            rw.Next = null;
            rw.TrySetResult(true);
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

        /// <summary>Synchronously receives a value, waiting for a sender is one is not ready</summary>
        /// <returns>The value that was sent</returns>
        /// <exception cref="OperationCanceledException">thrown when the channel <see cref="IsClosed"/> and there are no waiting senders</exception>
        public T Receive()
        {
            try
            {
                return ReceiveAsync().Result;
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>Asynchronously receives a value, waiting for a sender is one is not ready</summary>
        /// <returns>A task that completes with a result when a sender is ready.  The task may also be cancelled is the channel is closed and there are no waiting senders</returns>
        public Task<T> ReceiveAsync()
        {
            lock (_gate)
            {
                var sender = RemoveSender();
                if (sender != null)
                {
                    var value = sender.Value;
                    sender.TrySetResult(true);
                    return Task.FromResult(value);
                }
                if (_closed.IsCancellationRequested)
                    return Task.FromCanceled<T>(_closed);
                return AddReceiver().Task;
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

        /// <summary>Adds a waiter for a <see cref="Select"/></summary>
        internal void AddWaiter(Waiter waiter)
        {
            lock(_gate)
            {
                if (_receiverWaiters == null)
                    _receiverWaiters = waiter;
                else
                    AddWaiterToList(waiter);

                if (_senders != null)
                    waiter.TrySetResult(true);
            }
        }

        private void AddWaiterToList(Waiter waiter)
        {
            var rw = _receiverWaiters;
            while (rw.Next != null)
                rw = rw.Next;
            rw.Next = waiter;
        }

        /// <summary>Removes a waiter for a <see cref="Select"/></summary>
        internal void RemoveWaiter(Waiter waiter)
        {
            lock (_gate)
            {
                if (_receiverWaiters == waiter)
                    _receiverWaiters = waiter.Next;
                else
                    RemoveWaiterFromList(waiter);
            }
        }

        private void RemoveWaiterFromList(Waiter waiter)
        {
            var rw = _receiverWaiters;
            while (rw != null)
            {
                if (rw.Next == waiter)
                {
                    rw.Next = waiter.Next;
                    waiter.Next = null;
                    break;
                }
                rw = rw.Next;
            }
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

    class Waiter : TaskCompletionSource<bool>
    {
        public Waiter Next; // linked list

        public Waiter() : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
        }
    }
}

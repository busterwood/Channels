using System.Threading.Tasks;

namespace BusterWood.Channels
{
    public class Channel<T>
    {
        readonly object _gate = new object();
        Sender<T> _senders;
        Receiver<T> _receiver;

        public bool TrySend(T value)
        {
            lock (_gate)
            {
                var receiver = RemoveReceiver();
                if (receiver == null)
                    return false;
                return receiver.TrySetResult(value);
            }
        }

        public Task SendAsync(T value)
        {
            lock(_gate)
            {
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

        public Task<T> ReceiveAsync()
        {
            lock (_gate)
            {
                var sender = RemoveSender();
                if (sender == null)
                    return AddReceiver().Task;
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

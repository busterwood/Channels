using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusterWood.Channels
{
    /// <summary>A Select allows for receving on one on many channels, in priority order</summary>
    public class Select
    {
        static readonly Task<bool> True = Task.FromResult(true);
        static readonly Task<bool> False = Task.FromResult(false);

        List<ICase> cases = new List<ICase>();

        /// <summary>Adds a action to perform when a channel can be read</summary>
        /// <param name="ch">The channel to try to read from</param>
        /// <param name="action">the synchronous action to perform with the value that was read</param>
        public Select Receive<T>(Channel<T> ch, Action<T> action)
        {
            var @case = new ReceiveCase<T>(ch, action);
            cases.Add(@case);
            return this;
        }

        /// <summary>Adds a asynchronous action to perform when a channel can be read</summary>
        /// <param name="ch">The channel to try to read from</param>
        /// <param name="action">the asynchronous action to perform with the value that was read</param>
        public Select ReceiveAsync<T>(Channel<T> ch, Func<T, Task> action)
        {
            var @case = new ReceiveAsyncCase<T>(ch, action);
            cases.Add(@case);
            return this;
        }

        /// <summary>Reads from one (and only one) of the added channels and performs the associated action</summary>
        /// <returns>A task that completes when one channel has been read and the associated action performed</returns>
        public async Task ExecuteAsync()
        {
            for (;;)
            {
                // try to execute any case that is ready
                foreach (var c in cases)
                {
                    if (await c.TryExecuteAsync())
                        return;
                }

                // we must wait, no channels are ready
                var waiter = new Waiter();
                foreach (var c in cases)
                    c.RegisterWaiter(waiter);
                await waiter.Task;
                foreach (var c in cases)
                    c.Cleanup(waiter);
            }
        }

        interface ICase
        {
            Task<bool> TryExecuteAsync();
            void RegisterWaiter(Waiter tcs);
            void Cleanup(Waiter tcs);
        }

        class ReceiveAsyncCase<T> : ICase
        {
            readonly Channel<T> ch;
            readonly Func<T, Task> asyncAction;

            public ReceiveAsyncCase(Channel<T> ch, Func<T, Task> asyncAction)
            {
                this.ch = ch;
                this.asyncAction = asyncAction;
            }

            public Task<bool> TryExecuteAsync()
            {
                T val;
                return ch.TryReceive(out val) ? AsyncExcuteAction(val) : False;
            }

            private async Task<bool> AsyncExcuteAction(T val)
            {
                await asyncAction(val);
                return true;
            }

            public void RegisterWaiter(Waiter tcs)
            {
                ch.RegisterWaiter(tcs);
            }

            public void Cleanup(Waiter tcs)
            {
                ch.RemoveWaiter(tcs);
            }
        }

        class ReceiveCase<T> : ICase
        {
            readonly Channel<T> ch;
            readonly Action<T> action;

            public ReceiveCase(Channel<T> ch, Action<T> action)
            {
                this.ch = ch;
                this.action = action;
            }

            public Task<bool> TryExecuteAsync()
            {
                T val;
                if (ch.TryReceive(out val)) {
                    action(val);
                    return True;
                }
                return False;
            }

            public void RegisterWaiter(Waiter tcs)
            {
                ch.RegisterWaiter(tcs);
            }

            public void Cleanup(Waiter tcs)
            {
                ch.RemoveWaiter(tcs);
            }
        }
    }
}

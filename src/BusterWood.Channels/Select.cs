using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusterWood.Channels
{
    /// <summary>A Select allows for receving on one on many channels, in priority order</summary>
    public class Select
    {
        static readonly Task<bool> True = Task.FromResult(true);
        static readonly Task<bool> False = Task.FromResult(false);

        List<ICase> cases = new List<ICase>();
        ICase[] quickCases;

        /// <summary>Adds a action to perform when a channel can be read</summary>
        /// <param name="ch">The channel to try to read from</param>
        /// <param name="action">the synchronous action to perform with the value that was read</param>
        public Select OnReceive<T>(Channel<T> ch, Action<T> action)
        {
            var @case = new ReceiveCase<T>(ch, action);
            cases.Add(@case);
            quickCases = null;
            return this;
        }

        /// <summary>Adds a asynchronous action to perform when a channel can be read</summary>
        /// <param name="ch">The channel to try to read from</param>
        /// <param name="action">the asynchronous action to perform with the value that was read</param>
        public Select OnReceiveAsync<T>(Channel<T> ch, Func<T, Task> action)
        {
            var @case = new ReceiveAsyncCase<T>(ch, action);
            cases.Add(@case);
            quickCases = null;
            return this;
        }

        /// <summary>Reads from one (and only one) of the added channels and performs the associated action</summary>
        /// <returns>A task that completes when one channel has been read and the associated action performed</returns>
        public async Task ExecuteAsync()
        {
            // foreach an array is faster using a list
            if (quickCases == null)
                quickCases = cases.ToArray();

            for (;;)
            {
                // try to execute any case that is ready
                foreach (var c in quickCases)
                {
                    if (await c.TryExecuteAsync())
                        return;
                }

                // we must wait, no channels are ready
                var waiter = new Waiter();
                foreach (var c in quickCases)
                    c.AddWaiter(waiter);
                await waiter.Task;
                foreach (var c in quickCases)
                    c.RemoveWaiter(waiter);
            }
        }

        interface ICase
        {
            Task<bool> TryExecuteAsync();
            void AddWaiter(Waiter tcs);
            void RemoveWaiter(Waiter tcs);
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

            async Task<bool> AsyncExcuteAction(T val)
            {
                await asyncAction(val);
                return true;
            }

            public void AddWaiter(Waiter tcs)
            {
                ch.AddWaiter(tcs);
            }

            public void RemoveWaiter(Waiter tcs)
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

            public void AddWaiter(Waiter tcs)
            {
                ch.AddWaiter(tcs);
            }

            public void RemoveWaiter(Waiter tcs)
            {
                ch.RemoveWaiter(tcs);
            }
        }
    }
}

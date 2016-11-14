using BusterWood.Channels;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    public class SelectTests
    {
        [Test]
        public void executing_a_select_when_one_channel_is_ready_to_send_completes_immediately()
        {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<int>();

            var got1 = new TaskCompletionSource<int>();
            var got2 = new TaskCompletionSource<int>();
            var select = new Select()
                .Receive(ch1, val => got1.TrySetResult(val))
                .Receive(ch2, val => got2.TrySetResult(val));

            var sendTask = ch2.SendAsync(2);

            var selTask = select.ExecuteAsync();
            if (!selTask.IsCompleted)
                Assert.Fail("Expected select to wait, status = " + selTask.Status);

            if (!got2.Task.Wait(100))
                Assert.Fail("did not get the value to the TCS");

            if (got2.Task.Result != 2)
                Assert.Fail("Expected ch2 to get 2 but got " + got2.Task.Result);

            if (!selTask.IsCompleted)
                Assert.Fail("Expected select to be compelte");

            if (!sendTask.IsCompleted)
                Assert.Fail("Expected select to be compelte");

            if (got1.Task.Wait(50))
                Assert.Fail("did not expect value in ch1");
        }

        [Test]
        public void executing_a_select_waits_for_one_case_to_succeed()
        {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<int>();

            var got1 = new TaskCompletionSource<int>();
            var got2 = new TaskCompletionSource<int>();
            var select = new Select()
                .Receive(ch1, val => got1.TrySetResult(val))
                .Receive(ch2, val => got2.TrySetResult(val));

            var st = select.ExecuteAsync();
            if (st.IsCompleted)
                Assert.Fail("Expected select to wait, status = " + st.Status);

            while ((st.Status & TaskStatus.WaitingForActivation) != TaskStatus.WaitingForActivation)
                Thread.Sleep(10);

            Thread.Sleep(10);

            if (!ch1.SendAsync(2).Wait(100))
                Assert.Fail("Failed to SendAsync");

            if (!got1.Task.Wait(100))
                Assert.Fail("did not get the value to the TCS");

            if (!st.IsCompleted)
                Assert.Fail("Expected select to be compelte");

            if (got2.Task.Wait(50))
                Assert.Fail("did not expect value in ch2");
        }
    }
}

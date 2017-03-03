using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusterWood.Channels
{
    public static class Timeout
    {
        public static Channel<DateTime> After(TimeSpan timeout)
        {
            var c = new Channel<DateTime>();
            Task.Delay(timeout).ContinueWith(_ => c.SendAsync(DateTime.UtcNow));
            return c;
        }
    }
}

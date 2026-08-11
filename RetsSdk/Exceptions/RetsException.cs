using System;

namespace CrestApps.RetsSdk.Exceptions
{
    public class RetsException : Exception
    {
        public RetsException()
             : base("Rets server throw an unknow error")
        {
        }

        public RetsException(string message)
             : base(message)
        {
        }

        public RetsException(string message, int replyCode)
             : base($"{message} (ReplyCode: {replyCode})")
        {
            ReplyCode = replyCode;
        }

        /// <summary>
        /// The RETS ReplyCode the server returned, or <c>null</c> when it could not be determined.
        /// </summary>
        public int? ReplyCode { get; }
    }
}

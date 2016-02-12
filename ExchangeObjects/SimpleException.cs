using System;
using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class SimpleException
    {
        [DataMember] public string ExceptionType;
        [DataMember] public string Message;
        [DataMember] public string StackTrace;
        [DataMember] public SimpleException InnerException;

        public SimpleException(Exception e)
        {
            ExceptionType = e.GetType().FullName;
            Message = e.Message;
            StackTrace = e.StackTrace;
            if (e.InnerException != null)
            {
                InnerException = new SimpleException(e.InnerException);
            }
        }
    }
}

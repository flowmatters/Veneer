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
        [DataMember] public string PythonStackTrace;
        [DataMember] public SimpleException InnerException;

        public SimpleException(Exception e, string pythonStackTrace = null)
        {
            ExceptionType = e.GetType().FullName;
            Message = e.Message;
            StackTrace = e.StackTrace;
            PythonStackTrace = pythonStackTrace;
            if (e.InnerException != null)
            {
                InnerException = new SimpleException(e.InnerException);
            }
        }
    }
}

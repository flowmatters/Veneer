using System;
using System.Runtime.Serialization;
using FlowMatters.Source.Veneer.ExchangeObjects;

namespace FlowMatters.Source.Veneer.RemoteScripting
{
    [DataContract] public  class IronPythonResponse
    {
        [DataMember] public string StandardOut;
        [DataMember] public string StandardError;
        [DataMember] public VeneerResponse Response;
        [DataMember] public SimpleException Exception;
    }
}
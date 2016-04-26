﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    class ListResponse : VeneerResponse
    {
        [DataMember] public IEnumerable<VeneerResponse> Value;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer;
using RiverSystem.DataManagement.DataManager;

namespace FlowMatters.Source.Veneer.ExchangeObjects.DataSources
{
    [DataContract]
    public class SimpleDataItem
    {
        public SimpleDataItem(InputSetItem isi,bool summary=true)
        {
            Name = isi.Name;
            InputSets = isi.InputSets.Select(iset => iset.Name).ToArray();
            Details = isi.DataSource.PersistedData.Select(ddi => new SimpleDataDetails(ddi,summary)).ToArray();
        }

        public SimpleDataItem(GenericDataDetails gdd)
        {
            Name = gdd.Name;
            Details = gdd.AssociatedData.Select(ddi => new SimpleDataDetails(ddi)).ToArray();
        }

        public bool MatchInputSet(string inputSet)
        {
            return InputSets.Any(iSet => SourceService.URLSafeString(iSet) == inputSet);

        }
        [DataMember]
        public string Name;

        [DataMember]
        public string[] InputSets;

        [DataMember]
        public SimpleDataDetails[] Details;
    }
}

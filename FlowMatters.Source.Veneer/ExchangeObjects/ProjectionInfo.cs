using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using RiverSystem;
using RiverSystem.PreProcessing.Enumerations;
using RiverSystem.PreProcessing.ProjectionInfo;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class ProjectionInfo
    {
        public ProjectionInfo(AbstractProjectionInfo p)
        {
            if (p == null)
            {
                return;
            }

            Projection = p.ProjectionDescription;

            if (p as MGAProjectionInfo == null) return;
            MGAProjectionInfo mga = (MGAProjectionInfo)p;
            Zone = mga.Zone;
            Hemisphere = (mga.NorthFalseOrigin == 0) ? "North" : "South";

            //else if(p as AlbersProjectionInfo != null)
            //{
            //    AlbersProjectionInfo albers = (AlbersProjectionInfo) p;
            //}
        }

        [DataMember] public string Projection;

        [DataMember] public int Zone;

        [DataMember] public string Hemisphere;

        public void AssignTo(RiverSystemScenario scenario)
        {
            if (Zone != 0)
            {
                scenario.GeographicData.Projection = new MGAProjectionInfo(Zone,
                    Hemisphere == "North" ? UTMHemispheres.North : UTMHemispheres.South);
                return;
            }

            throw new NotSupportedException();
        }
    }
}

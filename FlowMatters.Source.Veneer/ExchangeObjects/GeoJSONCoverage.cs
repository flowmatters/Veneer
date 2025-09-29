using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer;
using TIME.DataTypes.Polygons;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class GeoJSONCoverage : VeneerResponse
    {
        public GeoJSONCoverage()
        {
            initialise();
        }

        public GeoJSONCoverage(GEORegionData[] coverage)
        {
            List<GeoJSONFeature> featureList = new List<GeoJSONFeature>();
            GEORegions geometry = (GEORegions) coverage[0].geometry;
            for (int i = 0; i < geometry.Count; i++)
            {
                var geoRegion = geometry.item(i);
                Dictionary<string, object> values = new Dictionary<string, object>();
                for (int col = 0; col < coverage.Length; col++)
                {
                    var data = coverage[col];

                    double internalValue;
                    try
                    {
                        internalValue = data.itemForGEORegion(geoRegion);
                    }
                    catch
                    {
                        internalValue = data[i];
                    }

                    object value;
                    try
                    {
                        value = data.cellObject(internalValue);
                    }
                    catch
                    {
                        value = internalValue;
                    }

                    values[data.name] = value;
                }
                featureList.Add(new GeoJSONFeature(geoRegion, values));

            }
            features = featureList.ToArray();
        }

        private void initialise()
        {
            
        }

        [DataMember]
        public GeoJSONFeature[] features;

        [DataMember]
        public string type
        {
            get { return _type; }
            private set { _type = value; }
        }
        private string _type = "FeatureCollection";
    }
}

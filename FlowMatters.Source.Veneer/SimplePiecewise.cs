using System.Runtime.Serialization;
using FlowMatters.Source.Veneer.ExchangeObjects;
using RiverSystem.Functions.Variables;
using TIME.Core;

namespace FlowMatters.Source.Veneer
{
    [DataContract]
    public class SimplePiecewise : VeneerResponse
    {
        public SimplePiecewise() { }

        public SimplePiecewise(LinearVariable source)
        {
            if (source == null) return;

            XName = source.XName;
            YName = source.YName;
            XUnits = source.XUnit.SIUnits;
            YUnits = source.YUnit.SIUnits;

            Entries = new double[source.Entries.Count][];
            for (int i = 0; i < Entries.Length; i++)
            {
                Entries[i] = new double[]
                {
                    source.Entries[i].X,
                    source.Entries[i].Y
                };
            }
        }

        public void ApplyTo(LinearVariable linV)
        {
            linV.XName = XName ?? linV.XName;
            linV.YName = YName ?? linV.YName;

            linV.XUnit = Unit.parse(XUnits ?? linV.XUnit.SIUnits);
            linV.YUnit = Unit.parse(YUnits ?? linV.YUnit.SIUnits);

            if (Entries != null)
            {
                while (linV.Entries.Count > Entries.Length)
                    linV.Entries.RemoveAt(Entries.Length);

                while(Entries.Length>linV.Entries.Count)
                    linV.Entries.Add(new LinearFunctionVariableEntry());

                for (int i = 0; i < Entries.Length; i++)
                {
                    linV.Entries[i].X = Entries[i][0];
                    linV.Entries[i].Y = Entries[i][1];
                }
            }
        }

        [DataMember] public string XName, YName, XUnits, YUnits;
        [DataMember] public double[][] Entries;
    }
}
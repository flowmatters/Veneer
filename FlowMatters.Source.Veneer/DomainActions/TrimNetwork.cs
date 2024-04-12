using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using RiverSystem;
using RiverSystem.Catchments;
using RiverSystem.Catchments.Constituents;
using RiverSystem.Constituents;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Nodes;
using RiverSystem.WaterUser;
using TIME.Core;
using TIME.Core.Metadata;
using TIME.DataTypes.IO;
using TIME.DataTypes.Polygons;
using TIME.Tools.Reflection;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Utility class for trimming a Source node link network to just the elements upstream of defined outlets.
    /// Intended to be called from a veneer-py script.
    /// </summary>
    public class TrimNetwork
    {
        public static void Trim(RiverSystemScenario scenario, List<string> newOutletLinks)
        {
            var trimmer = new TrimNetwork();
            trimmer.Scenario = scenario;
            trimmer.NewOutletLinks = newOutletLinks.Select(name => scenario.Network.LinkWithName(name) as Link).ToList();
            trimmer.Run();
        }
        private TrimNetwork()
        {
            NewOutletLinks = new List<Link>();
        }

        public RiverSystemScenario Scenario { get; set; }

        public List<Link> NewOutletLinks { get; private set; }

        public void Run()
        {
            var theOutletLinks = new List<Link>();

            //foreach (Link li in Scenario.Network.outletLinks())
            theOutletLinks.AddRange(Scenario.Network.outletLinks().Where(l =>
            {
                Node n = l.to as RiverSystem.Node;
                return !(n.NodeModel is DemandNodeModel);
            }).Select(l => (RiverSystem.Link)l));

            ignoreLinks = new List<Link>();
            ignoreLinks.AddRange(NewOutletLinks);
            removeElements(theOutletLinks);
        }


        private List<string> nodesPreviouslyRemoved;

        private List<Link> ignoreLinks = null;

        private void removeElements(List<Link> startingLinks)
        {
            //Set subcatchraster to null to make things quicker
            Scenario.GeographicData.SubCatchmentRaster = null;

            //Intersected Subcats/FUs
            if (Scenario.GeographicData.IntersectedSCFUPolygons != null)
            {
                Scenario.GeographicData.IntersectedSCFUPolygons = null;
            }

            //reporting regions
            if (Scenario.GeographicData.ReportingRegions != null)
            {
                Scenario.GeographicData.ReportingRegions = null;
            }

            catDblsToRemove = new List<double>();
            nodesPreviouslyRemoved = new List<string>();

            int keepCatchCount = 0;
            foreach (Link li in ignoreLinks)
            {
                double numCatchments = countUpstreamCatchmentsForThisLink(li, 0);
                keepCatchCount += (int)numCatchments;
            }

            foreach (Link li in startingLinks)
                removeEverythingUpstreamOfAndIncludingLink(li);

            foreach (double theCatVal in catDblsToRemove)
            {
                GEORegion gr = Scenario.GeographicData.SubCatchmentOutline.findRegionWithValue(theCatVal);
                Scenario.GeographicData.SubCatchmentOutline.regions.remove(gr);

                if (Scenario.GeographicData.SubCatchmentOutline.Categories.ContainsKey(theCatVal))
                {
                    Scenario.GeographicData.SubCatchmentOutline.Categories.Remove(theCatVal);
                }
            }

            string theTempFolder = Scenario.Project.TemporaryFolder;
            string idNum = Scenario.GeographicData.SubCatchmentOutline.PersistenceId.ToString();

            saveShapefile(theTempFolder + "\\" + idNum + ".shp", Scenario.GeographicData.SubCatchmentOutline);

            TIME.Management.Memory.ClearAllAvailableMemory();
            Thread.Sleep(1500);
        }

        private double upCatchCount;

        private double countUpstreamCatchmentsForThisLink(Link li, double currentCount)
        {
            if (currentCount == 0)
            {
                //A start link, add one
                currentCount += 1;
                upCatchCount = 1;
            }

            foreach (Link upLink in li.UpstreamNode.UpstreamLinks)
            {
                //Don't want links associated only with extractions
                if (Scenario.Network.CatchmentForLink(upLink) != null)
                {
                    currentCount += 1;
                    upCatchCount += 1;
                    countUpstreamCatchmentsForThisLink(upLink, currentCount);
                }
            }

            return upCatchCount;
        }

        private void removeEverythingUpstreamOfAndIncludingLink(Link l)
        {
            if (l == null)
            {
                return;
            }

            if (ignoreLinks.Contains(l))
            {
                return;
            }

            Node un = (Node)l.UpstreamNode;
            TIME.DataTypes.NodeLinkNetwork.Link[] uplinks = Scenario.Network.upstreamLinks(un);

            List<Link> dLinks = new List<Link>();
            foreach (Link dLink in un.DownstreamLinks)
            {
                if (dLink == l)
                    continue;

                if (!ignoreLinks.Contains(dLink))
                    dLinks.Add(dLink);
            }

            //get rid of any no-catchment based downstream links - prob WaterUser connected
            foreach (Link dl in dLinks)
            {
                Node theUNode = (Node)dl.UpstreamNode;

                bool deleteTheLink = true;
                foreach (Link iLink in ignoreLinks)
                {
                    if (iLink.DownstreamNode == theUNode)
                    {
                        deleteTheLink = false;
                    }
                }

                if (deleteTheLink)
                {
                    SafeRemoveNode((Node)dl.DownstreamNode);

                    localDeleteLink(dl);
                }
            }

            foreach (Link l2 in uplinks)
            {
                if (!ignoreLinks.Contains(l2))
                    removeEverythingUpstreamOfAndIncludingLink(l2);
            }

            //This checking here for cases where our 'ignore' link is also an outlet link...
            if (ignoreLinks.Contains(l)) return;

            Catchment thisCat = (Catchment)Scenario.Network.CatchmentForLink(l);
            if (thisCat != null)
            {
                removeCatchmentFromScenario(thisCat);
            }
            localDeleteLink(l);
        }

        private void localRemoveNode(Node theNode)
        {
            if (nodesPreviouslyRemoved.Contains(theNode.Name)) return;

            if (theNode.NodeModel is StorageNodeModel)
            {
                var storage = ((StorageNodeModel)theNode.NodeModel);
                while(storage.OutletPaths.Count > 0){
                    storage.RemoveOutletPath(storage.OutletPaths[0].Link);
                }
            }

            SafeRemoveNode(theNode);
        }

        private void localDeleteLink(Link theLink)
        {
            //Need to also check first if the upstream node is a storage, in which case this may be the outlet link!

            EventHandler<FlowRoutingModelChangeArgs> the_flowRoutingChanged_event = theLink.flowRoutingChanged;
            theLink.flowRoutingChanged = null;

            Node theUpNode = (Node)theLink.UpstreamNode;

            if (theUpNode.NodeModel is StorageNodeModel)
            {
                //We'll need to clear outlet paths that use this link
                //unless they've cleared already
                if (!nodesPreviouslyRemoved.Contains(theUpNode.Name))
                {
                    ((StorageNodeModel)theUpNode.NodeModel).RemoveOutletPath(theLink);
                }
            }

            if (theUpNode.UpstreamLinks.Count == 0)
            {
                localRemoveNode(theUpNode);
            }

            Node theDNode = (Node)theLink.DownstreamNode;

            bool deleteTheDownNode = true;

            foreach (Link iLink in ignoreLinks)
            {
                if (iLink.DownstreamNode == theDNode)
                {
                    deleteTheDownNode = false;
                    break;
                }
            }

            if (deleteTheDownNode)
                SafeRemoveNode(theDNode);

            theLink.RoutingOrdering = null;
            theLink.RatingCurveLibrary = null;
            SafelyRemoveLink(theLink);

            theLink.flowRoutingChanged = the_flowRoutingChanged_event;
        }

        private void UnhookVariables(INetworkElement feature)
        {
            var modelledVars = Scenario.Network.FunctionManager.GetVariablesForElement(feature);
            foreach (var mv in modelledVars)
                mv.ProjectViewRow = null;
        }

        private void SafelyRemoveLink(Link theLink)
        {
            UnhookVariables(theLink);
            Scenario.Network.Delete(theLink);
        }

        private void SafeRemoveNode(Node theNode)
        {
            if (nodesPreviouslyRemoved.Contains(theNode.Name))
                return;

            UnhookVariables(theNode);
            nodesPreviouslyRemoved.Add(theNode.Name);
            Scenario.Network.Remove(theNode);
        }

        List<double> catDblsToRemove;
        private void removeCatchmentFromScenario(Catchment cat)
        {
            var geoData = Scenario.GeographicData;
            var subCatchmentOutline = geoData.SubCatchmentOutline;
            if (subCatchmentOutline != null)
            {
                bool haveMatch = false;
                double theCatVal = -99;

                foreach (KeyValuePair<double, object> kvp in subCatchmentOutline.Categories)
                {
                    string scName = (string)kvp.Value;

                    if (scName != cat.Name) continue;
                    theCatVal = kvp.Key;
                    haveMatch = true;
                    break;
                }

                if (haveMatch)
                    catDblsToRemove.Add(theCatVal);
            }

            foreach (FunctionalUnitDefinition fudef in Scenario.SystemFunctionalUnitConfiguration.fuDefinitions)
            {

                FunctionalUnit fu = cat.getFunctionalUnit(fudef);
                if (fu is StandardFunctionalUnit)
                {
                    removeUsagesForSFU(cat, (StandardFunctionalUnit)fu);
                }
            }

            Scenario.Network.Remove(cat);
        }

        public void removeUsagesForSFU(Catchment cat, StandardFunctionalUnit SFU)
        {
            //RRmodel
            if (SFU.rainfallRunoffModel != null)
            {
                removeUsagesForSpecificModel(SFU.rainfallRunoffModel);

            }

            CatchmentElementConstituentData CECD =
                Scenario.Network.ConstituentsManagement.GetConstituentData<CatchmentElementConstituentData>(cat);
            FunctionalUnitConstituentData FUCD = CECD.GetFunctionalUnitData(SFU);

            foreach (ConstituentContainer CC in FUCD.ConstituentModels)
            {
                foreach (ConstituentSourceContainer CSC in CC.ConstituentSources)
                {
                    removeUsagesForSpecificModel(CSC.GenerationModel);
                    removeUsagesForSpecificModel(CSC.FilterModel);
                }
            }

        }

        public void removeUsagesForSpecificModel(Model theModel)
        {
            if (theModel == null) return;

            foreach (MemberInfo MI in GetFieldsAndProperties(theModel.GetType()))
            {
                ReflectedItem RI = ReflectedItem.NewItem(MI, theModel);

                //Need this to 'ignore' redundant AggregatedConstituentModels
                //if (typeof(Model).IsAssignableFrom(RI.itemType) && RI.itemValue != null)
                if (RI.itemValue != null)
                {
                    if (RI.itemValue is Model)
                    {
                        //This member is a model itself
                        removeUsagesForSpecificModel((Model)RI.itemValue);
                        continue;
                    }
                }

                string GDDName = Scenario.Network.DataManager.GetUsageFullName(RI);
                if (!String.IsNullOrEmpty(GDDName))//Will be "" if no usages for this RI
                {
                    string[] split = GDDName.Split(new[] { '.' });
                    if (split.Count() <= 1)
                    {
                        return;
                    }
                    string groupName = string.Join(".", split.Take(split.Count() - 1));

                    bool removeTS = true;
                    GenericDataDetails GDD = Scenario.Network.DataManager.DataGroups.Where(g => g.Name == groupName).Select(@group => @group.GetUsage(split.Last())).FirstOrDefault(gdd => gdd != null);
                    //GenericDataDetails GDD = Scenario.Network.DataManager.GetDetails(GDDName);
                    if (GDD.Usages.Count > 1)
                    {
                        //Other items need this timeseries, don't remove
                        removeTS = false;
                    }

                    DataUsage DU = GDD.Usages.First(x => x.ReflectedItem.Equals(RI));

                    GDD.Usages.Remove(DU);

                    if (removeTS)
                    {
                        string DGName = GDDName.Split('.')[0];
                        DataGroupItem DGI = Scenario.Network.DataManager.DataGroups.FirstOrDefault(x => x.Name == DGName);

                        DGI.RemoveItem(GDD);
                        Scenario.Network.DataManager.Refresh();
                    }

                }
            }
        }

        public static List<MemberInfo> GetFieldsAndProperties(Type type)
        {
            List<MemberInfo> targetMembers = new List<MemberInfo>();

            targetMembers.AddRange(type.GetFields());
            targetMembers.AddRange(type.GetProperties());

            return targetMembers;
        }

        //Added by Rob to avoid looping through individual records
        public static void saveShapefile(string filename, GEORegionData shapeData)
        {
            ShapeFileIO sfl = new ShapeFileIO();

            sfl.Use(shapeData);

            sfl.Save(filename);
        }

    }
}
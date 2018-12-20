using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RiverSystem.Controls.Icons;
using TIME.Core;
using TIME.Management;

namespace FlowMatters.Source.Veneer
{
    class ResourceHelpers
    {
        public static Bitmap FindByName(string s)
        {
#if V3 || V4_0 || V4_1 || V4_2_0 
            Type modelType = Finder.typesInherting(typeof(IDomainObject)).Where(t => t.Name == s).FirstOrDefault();
#else
            var modelType = AssemblyManager.FindTypes(typeof(IDomainObject),allowAbstract:false,allowIgnore:true).FirstOrDefault(t => t.Name == s);
#endif
            if (modelType == null)
            {
                s += "240";
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Assembly assembly = assemblies.First(a => a.FullName.Contains("RiverSystem.Controls"));
                Type resourcesType = assembly.GetType("RiverSystem.Controls.Properties.Resources");
                PropertyInfo resourceProperty = resourcesType.GetProperty(s, BindingFlags.Public | BindingFlags.Static);
                Bitmap result = (Bitmap) resourceProperty.GetValue(null, new object[0]);

                return result;
            }

            return (Bitmap) IconLookup.GetIcon(modelType);
        }

        public static string ContentTypeForFilename(string fn)
        {
            string contentType = "";
            FileInfo fileInfo = new FileInfo(fn);
            string extension = fileInfo.Extension;
            switch (extension)
            {
                case ".html":
                case ".htm":
                    contentType = "text/html";
                    break;
                case ".jpeg":
                case ".jpg":
                    contentType = "image/jpeg";
                    break;
                case ".png":
                    contentType = "image/png";
                    break;
                case ".json":
                    contentType = "application/json";
                    break;
                case ".js":
                    contentType = "application/javascript";
                    break;
                case ".css":
                    contentType = "text/css";
                    break;
            }
            return contentType;
        }
    }
}

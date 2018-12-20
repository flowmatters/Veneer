using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer.ExchangeObjects;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Utils;
using NUnit.Framework;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumer.Forms;
using RiverSystem.ApplicationLayer.Interfaces;
using RiverSystem.Forms;
using RiverSystem.Functions.Variables;
using TIME.DataTypes;
using TIME.DataTypes.Polygons;
using TIME.Management;
using TIME.Tools.Reflection;

namespace FlowMatters.Source.Veneer.RemoteScripting
{
    class ScriptRunner
    {
        public RiverSystemScenario Scenario { get; set; }
        public IProjectHandler<RiverSystemProject> ProjectHandler { get; set; }

        public IronPythonResponse Run(IronPythonScript script)
        {
            object actual = null;
            SimpleException ex = null;
            MemoryStream outputStream = new MemoryStream();
            StringWriter outputWriter = new StringWriter();

            MemoryStream errorStream = new MemoryStream();
            StringWriter errorWriter = new StringWriter();

            try
            {
                var engine = Python.CreateEngine();

                if(SynchronizationContext.Current==null)
                    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

                AddAssemblyReferences(engine);
                engine.Runtime.IO.SetOutput(outputStream, outputWriter);
                engine.Runtime.IO.SetErrorOutput(errorStream, errorWriter);

                var scope = engine.CreateScope();
                scope.SetVariable("scenario", Scenario);

                if (ProjectHandler == null)
                {
                    ProjectHandler = ProjectManager.Instance?.ProjectHandler;
                }
                scope.SetVariable("project_handler", ProjectHandler);
                var sourceCode = engine.CreateScriptSourceFromString(script.Script);
                actual = sourceCode.Execute<object>(scope);
                if (scope.ContainsVariable("result"))
                    actual = scope.GetVariable("result");
            }
            catch (Exception e)
            {
                ex = new SimpleException(e);
            }

            return new IronPythonResponse()
            {
                Response = AsKnownDataContract(actual),
                StandardError = errorWriter.ToString(),
                StandardOut = outputWriter.ToString(),
                Exception = ex
            };
        }

        private static void AddAssemblyReferences(ScriptEngine engine)
        {
#if V3 || V4_0 || V4_1 || V4_2_0
            List<string> ignoreList = new List<string>(Finder.DllsThatAreIrrelevantToFinder);
            List<string> myList = new List<string>
            {
                "system.core.dll"
            };
            foreach (Assembly a in  AppDomain.CurrentDomain.GetAssemblies())
            {
                var dllName = a.ManifestModule.Name.ToLower();

                // skip looking for user options if we are a system/3rd party
                // assembly
                if (ignoreList.Contains(dllName) && !myList.Contains(dllName))
                    continue;
                engine.Runtime.LoadAssembly(a);
            }
#else
            var myList = new HashSet<string>{ "system.core.dll" };
            var includeList = AssemblyManager.Assemblies();
            foreach(var a in includeList.Where(a=> !myList.Contains(a.ManifestModule.Name.ToLower())))
                engine.Runtime.LoadAssembly(a);
#endif
        }

        private VeneerResponse AsKnownDataContract(object actual)
        {
            if (actual == null)
                return null;
            if (actual is TimeSeries)
                return new SimpleTimeSeries((TimeSeries) actual);
            if(actual is LinearVariable)
                return new SimplePiecewise((LinearVariable) actual);
            if (actual is bool)
                return new BooleanResponse {Value = (bool)actual};
            if(actual is string)
                return new StringResponse {Value=(string)actual};
            if(actual is GEORegionData[])
                return new GeoJSONCoverage((GEORegionData[])actual);
            if (actual is IronPython.Runtime.PythonDictionary)
            {
                var dict = (IronPython.Runtime.PythonDictionary) actual;
                var keys = dict.keys();
                return new DictResponse
                {
                    Entries = keys.Select(k => new KeyValueResponse
                    {
                        Key = AsKnownDataContract(k),
                        Value = AsKnownDataContract(dict.get(k))
                    })
                };
            }
            if (actual is DataTable)
            {
                var table = (DataTable) actual;
                return new ListResponse
                {
                    Value = table.Rows.OfType<DataRow>().Select(row =>
                    {
                        return new DictResponse
                        {
                            Entries = table.Columns.OfType<DataColumn>().Select(col => new KeyValueResponse
                            {
                                Key = AsKnownDataContract(col.ColumnName),
                                Value = AsKnownDataContract(row[col])
                            })
                        };
                    })
                };
            }
            if (actual is IEnumerable)
            {
                IEnumerable enumerable = (IEnumerable) actual;
                return new ListResponse
                {
                    Value = enumerable.Select(e => AsKnownDataContract(e)).AsEnumerable<VeneerResponse>()
                };
            }
            double num;
            if (double.TryParse(actual.ToString(), out num))
                return new NumericResponse {Value = num};

            return new StringResponse {Value = actual.ToString()};
        }
    }
}

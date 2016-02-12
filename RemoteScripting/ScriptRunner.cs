using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer.ExchangeObjects;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using RiverSystem;
using RiverSystem.Forms;
using RiverSystem.Functions.Variables;
using TIME.DataTypes;
using TIME.Management;
using TIME.Tools.Reflection;

namespace FlowMatters.Source.Veneer.RemoteScripting
{
    class ScriptRunner
    {
        public RiverSystemScenario Scenario { get; set; }
    
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

                AddAssemblyReferences(engine);
                engine.Runtime.IO.SetOutput(outputStream, outputWriter);
                engine.Runtime.IO.SetErrorOutput(errorStream, errorWriter);

                var scope = engine.CreateScope();
                scope.SetVariable("scenario", Scenario);
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
            List<string> ignoreList = new List<string>(Finder.DllsThatAreIrrelevantToFinder);
            foreach (Assembly a in  AppDomain.CurrentDomain.GetAssemblies())
            {
                var dllName = a.ManifestModule.Name.ToLower();
                if (dllName == "system.core.dll")
                    continue;

                // skip looking for user options if we are a system/3rd party
                // assembly
                if (ignoreList.Contains(dllName))
                    continue;

                engine.Runtime.LoadAssembly(a);
            }
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
            double num;
            if (double.TryParse(actual.ToString(), out num))
                return new NumericResponse {Value = num};

            return new StringResponse {Value = actual.ToString()};
        }
    }
}

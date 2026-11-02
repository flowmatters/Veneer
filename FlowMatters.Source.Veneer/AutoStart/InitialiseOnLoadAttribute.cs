using System;
using TIME.Core.Metadata;

namespace FlowMatters.Source.Veneer.AutoStart
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false), Serializable]
    public class InitialiseOnLoadAttribute : DisplayPathAttribute
    {
        private static bool _initialised=false;

        public static ProjectLoadListener _listener;

        static InitialiseOnLoadAttribute()
        {
            // Earliest Veneer code to run inside Source: this type is touched when Source
            // scans plugin attributes, well before a project is loaded. VeneerCmd installs
            // the guard directly instead, and reaches here later via MarkInitialised().
            LogDispatchGuard.Install();
        }

        public static void MarkInitialised()
        {
            _initialised = true;
        }

        public InitialiseOnLoadAttribute(string path):base(path)
        {
            if (!_initialised)
            {
                _listener = ProjectLoadListener.Instance;
            }

            _initialised = true;
        }
    }
}

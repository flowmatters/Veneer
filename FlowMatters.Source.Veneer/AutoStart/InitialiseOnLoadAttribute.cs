using System;
using TIME.Core.Metadata;

namespace FlowMatters.Source.Veneer.AutoStart
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false), Serializable]
    public class InitialiseOnLoadAttribute : DisplayPathAttribute
    {
        private static bool _initialised=false;

        public static ProjectLoadListener _listener;
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

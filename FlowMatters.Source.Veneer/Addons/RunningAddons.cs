using System;
using System.Collections.Generic;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// How many instances of each addon are currently running, so the menu can
    /// disable an item while its process is alive.
    ///
    /// A count rather than a set: `allowMultiple` permits concurrent instances, and
    /// with a set the first one exiting would clear the state while the others were
    /// still running -- the label would lie. The count also feeds the label directly.
    ///
    /// Not "pure" (it holds mutable state), but free of WinForms and RiverSystem
    /// types, so it is unit-testable without a loaded scenario.
    ///
    /// Deliberately does NOT implement IAddonLifecycle: that interface is internal,
    /// this type is public like its neighbours in Addons/, and routing every
    /// decrement through OneShotLifecycle is what makes double-fire impossible.
    /// </summary>
    public class RunningAddons
    {
        private readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// The NORMALISED menu path plus the name. Keying on addon.menu raw would be
        /// wrong: SplitMenuPath maps null/whitespace to "Reporting", trims segments and
        /// drops empties, so null, "", "   ", "Reporting", " Reporting " and "Reporting|"
        /// all render one menu item while producing six different raw keys. "Same key"
        /// must mean "same rendered location".
        /// </summary>
        public static string Key(VeneerAddon addon)
        {
            var menu = string.Join("|", MenuLayout.SplitMenuPath(addon == null ? null : addon.menu));
            return menu + "\0" + (addon == null ? null : addon.name);
        }

        public void MarkRunning(VeneerAddon addon)
        {
            var key = Key(addon);
            lock (_counts)
            {
                int n;
                _counts.TryGetValue(key, out n);
                _counts[key] = n + 1;
            }
        }

        /// <summary>
        /// Floored at zero rather than throwing. Called from AddonLauncher's threadpool
        /// watcher, where an escaping exception would be swallowed as an unobserved task
        /// exception and the count would strand.
        /// </summary>
        public void Finished(VeneerAddon addon)
        {
            var key = Key(addon);
            lock (_counts)
            {
                int n;
                if (!_counts.TryGetValue(key, out n) || n <= 0) return;
                if (n == 1) _counts.Remove(key);
                else _counts[key] = n - 1;
            }
        }

        public int RunningCount(VeneerAddon addon)
        {
            lock (_counts)
            {
                int n;
                _counts.TryGetValue(Key(addon), out n);
                return n;
            }
        }
    }
}

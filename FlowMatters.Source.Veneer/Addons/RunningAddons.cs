using System;
using System.Collections.Generic;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// How many instances of each addon are currently running, so the menu can
    /// disable an item while its process is alive.
    ///
    /// A count rather than a set: a later task is expected to add an `allowMultiple`
    /// flag permitting concurrent instances of one addon, and with a set the first
    /// one exiting would clear the state while the others were still running -- the
    /// label would lie. The count also feeds the label directly.
    ///
    /// Not "pure" (it holds mutable state), but free of WinForms and RiverSystem
    /// types, so it is unit-testable without a loaded scenario.
    ///
    /// At this commit, nothing stops a double-fired `Finished` from under-counting
    /// (see the note on `Finished` itself) -- there is no lifecycle guard yet
    /// ensuring a watcher calls it at most once per launch. A later task is
    /// expected to route every decrement through such a guard; until that lands,
    /// two `Finished` calls for one `MarkRunning` will floor the count early and
    /// silently re-enable a menu item while another instance is still running.
    /// This class only counts what it is told -- it cannot detect that on its own.
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
        ///
        /// Tolerates a null `addon` (and a null `addon.name`) rather than throwing:
        /// every entry point on this class exists to be safe to call from a
        /// threadpool watcher with no observer for an exception (see `Finished`).
        /// That tolerance has one leak: `Key(null)` yields "Reporting\0", identical
        /// to an addon whose `menu` and `name` are both null. `VeneerAddon.Validate`
        /// never checks `name`, so a `.veneer` entry missing "name" is reachable and
        /// would share that bucket with a genuinely absent addon -- the one case
        /// where "same key" does not mean "same rendered menu item".
        /// </summary>
        public static string Key(VeneerAddon addon)
        {
            var menu = string.Join("|", MenuLayout.SplitMenuPath(addon == null ? null : addon.menu));
            // "\0" (not "|") separates menu from name. "|" already delimits menu
            // segments, so it is not safe to reuse: an addon name containing "|",
            // or an empty name dropped by a naive "join everything, skipping
            // blanks" implementation, could make two different (menu, name) pairs
            // produce the same string. "\0" will not appear in a realistic menu
            // segment or addon name, so it carries no such ambiguity.
            return menu + "\0" + (addon == null ? null : addon.name);
        }

        /// <summary>
        /// Records one more running instance of `addon`. Must be called BEFORE the
        /// child process is started, not after: if the process could exit and its
        /// watcher call `Finished` before this runs, the decrement would land on a
        /// count that was never incremented and get floored to zero -- the menu
        /// item would never disable even though the launch is legitimate. Enforcing
        /// that ordering is the launcher's job, not this class's; this class only
        /// trusts the order its caller calls it in.
        /// </summary>
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
        /// exception and the count would strand. Tolerates a null `addon` for the same
        /// no-throw reason (see `Key`).
        ///
        /// The floor has a cost: it hides a double-fired `Finished` for one
        /// `MarkRunning` (e.g. a watcher that observes the same process exit twice).
        /// That decrements one instance too many, under-counting a still-running
        /// addon and potentially re-enabling its menu item early, with nothing here
        /// to detect it. Nothing at this commit prevents that double fire -- see the
        /// class-level note.
        /// </summary>
        public void Finished(VeneerAddon addon)
        {
            var key = Key(addon);
            lock (_counts)
            {
                int n;
                // `n <= 0` cannot happen today: MarkRunning only ever stores n+1
                // (so >=1), and this method removes the key outright at n==1
                // rather than storing a zero (see below) -- a present key is
                // therefore always >=1. Kept as a guard against that invariant
                // changing later, not because zero-valued entries are expected now.
                if (!_counts.TryGetValue(key, out n) || n <= 0) return;
                // Remove rather than store zero, so the dictionary holds an entry
                // only for addons actually running -- a finished addon leaves no
                // trace instead of parking a "0" count behind.
                if (n == 1) _counts.Remove(key);
                else _counts[key] = n - 1;
            }
        }

        public int RunningCount(VeneerAddon addon)
        {
            // Key is pure and static; compute it outside the lock, consistent
            // with MarkRunning and Finished, rather than holding the monitor
            // across a Split + several LINQ operators + a Join. This is the
            // member called on the UI thread once per addon on every dropdown
            // open, so it is the one most worth keeping cheap under contention.
            var key = Key(addon);
            lock (_counts)
            {
                int n;
                _counts.TryGetValue(key, out n);
                return n;
            }
        }
    }
}

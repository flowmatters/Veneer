using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Forms;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Forms;
using RiverSystem.Forms.DockingPanels;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Keeps the Source GUI's data sources tree in step when Veneer removes data sources.
    /// </summary>
    /// <remarks>
    /// The tree adds an entry for each data source added to DataManager.DataGroups, but drops
    /// one only through its own Delete command: a data source removed by code leaves its entry
    /// behind, named as it was, pointing at the removed data source. A data source later created
    /// under that name then clashes with the leftover entry, and the tree shows its "same name"
    /// error. Source's own "delete all data sources" rebuilds the panel for the same reason, and
    /// so does this, after any change that removed a data source.
    /// </remarks>
    internal static class DataSourcesTree
    {
        /// <summary>
        /// Run change; if it removed any data source from dm, call refresh once afterwards - even
        /// if change then failed. A failure to refresh is reported to warn, and does not fail the
        /// change. A null refresh (VeneerCmd, which has no tree) is never called.
        /// </summary>
        internal static void ChangeThenRefreshIfRemoved(DataManager dm, Action change, Action refresh,
                                                        Action<string> warn)
        {
            var removed = false;
            NotifyCollectionChangedEventHandler watch = (sender, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Remove ||
                    e.Action == NotifyCollectionChangedAction.Replace ||
                    e.Action == NotifyCollectionChangedAction.Reset)
                    removed = true;
            };
            dm.DataGroups.CollectionChanged += watch;
            try
            {
                change();
            }
            finally
            {
                dm.DataGroups.CollectionChanged -= watch;
                if (removed && refresh != null)
                {
                    try
                    {
                        refresh();
                    }
                    catch (Exception e)
                    {
                        warn?.Invoke("Could not refresh Source's data sources tree after removing a data source: " +
                                     e.Message + ". Its tree may show the removed data source until the project is reopened.");
                    }
                }
            }
        }

        /// <summary>
        /// Rebuild every data sources panel open in the Source GUI, from its scenario's data
        /// manager, as Source does after deleting all data sources. GUI only, on the UI thread.
        /// </summary>
        /// <remarks>
        /// Uses only DataSourcesExplorerHost's public Scenario property: setting it re-binds the
        /// panel (it unbinds, then builds a new view model from the data manager). The panels
        /// are found through the controls of the open forms, which include the main window's
        /// docked and auto-hidden panels and any floating windows.
        /// </remarks>
        internal static void RebindPanels(Action<string> warn)
        {
            foreach (var host in OpenPanels())
            {
                var scenario = host.Scenario;
                if (scenario == null)
                    continue;
                try
                {
                    host.Scenario = scenario;
                }
                catch (Exception e)
                {
                    warn?.Invoke("Could not refresh a data sources panel: " + e.Message);
                }
            }
        }

        private static IEnumerable<DataSourcesExplorerHost> OpenPanels()
        {
            var roots = Application.OpenForms.Cast<Form>().ToList();
            if (MainForm.Instance != null && !roots.Contains(MainForm.Instance))
                roots.Add(MainForm.Instance);

            var seen = new HashSet<Control>();
            var pending = new Stack<Control>(roots);
            while (pending.Count > 0)
            {
                var control = pending.Pop();
                if (!seen.Add(control))
                    continue;
                if (control is DataSourcesExplorerHost host)
                {
                    yield return host;
                    continue;
                }
                foreach (Control child in control.Controls)
                    pending.Push(child);
            }
        }
    }
}

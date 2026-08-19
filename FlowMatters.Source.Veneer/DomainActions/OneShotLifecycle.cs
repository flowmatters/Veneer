using System;
using System.Threading;
using FlowMatters.Source.Veneer.Addons;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Fires its callback at most once, however many times Finished is called and from
    /// however many threads.
    ///
    /// Owned by VeneerMenu.LaunchAddon and threaded down into AddonLauncher, so that ONE
    /// object spans both layers. That placement is the point: LaunchAddon's own catch
    /// also has to report, and a wrapper created inside Launch would leave it outside the
    /// guard. The escaping path is real -- Run's Start()-failure branch reports, its next
    /// log.Write throws, Launch's catch reports (a no-op), ITS log.Write throws, and the
    /// exception reaches LaunchAddon's catch, which would decrement a second time. At
    /// count 1 the floor in RunningAddons hides that; with two instances live the count
    /// would go 3 -> 2 -> 1 and the label would lie, which is exactly what counting
    /// instead of set-membership was meant to prevent.
    /// </summary>
    internal sealed class OneShotLifecycle : IAddonLifecycle
    {
        private readonly Action<VeneerAddon> _onFinished;
        private int _fired;

        public OneShotLifecycle(Action<VeneerAddon> onFinished)
        {
            _onFinished = onFinished;
        }

        public void Finished(VeneerAddon addon)
        {
            if (Interlocked.Exchange(ref _fired, 1) != 0) return;
            if (_onFinished != null) _onFinished(addon);
        }
    }
}

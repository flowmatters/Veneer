using System;
using System.Threading;
using FlowMatters.Source.Veneer.Addons;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Fires its callback at most once, however many times Finished is called and from
    /// however many threads.
    ///
    /// One instance per launch, never shared. Finished takes a VeneerAddon argument
    /// because IAddonLifecycle's contract is per-call, not because a single
    /// OneShotLifecycle is meant to serve many addons or many launches -- it is not.
    /// Parking one instance somewhere longer-lived (per menu item, say, instead of per
    /// click) would fire once for the first launch and then silently swallow the
    /// decrement for every launch after it, addon argument included.
    ///
    /// Will be owned by VeneerMenu.LaunchAddon and threaded down into AddonLauncher (Task
    /// 6), so that ONE object spans both layers -- at this commit nothing constructs or
    /// wires it in. That placement is the point: LaunchAddon's own catch also has to
    /// report, and a wrapper created inside Launch would leave it outside the guard. The
    /// escaping path is real -- Run's Start()-failure branch reports, its next log.Write
    /// throws, Launch's catch reports (a no-op), ITS log.Write throws, and the exception
    /// reaches LaunchAddon's catch, which would decrement a second time. At count 1 the
    /// floor in RunningAddons hides that; with two instances live the count would go
    /// 3 -> 2 -> 1 and the label would lie, which is exactly what counting instead of
    /// set-membership was meant to prevent.
    ///
    /// A double-report is never surfaced anywhere -- the second and later Finished calls
    /// are silent no-ops, not errors. That silence is not "we don't care": every
    /// constructed double-report path above is triggered BY a throwing log.Write, so at
    /// the exact moment a duplicate would occur, the channel it would be reported on is
    /// the thing that just failed. There is nothing left to tell it to.
    /// </summary>
    internal sealed class OneShotLifecycle : IAddonLifecycle
    {
        private readonly Action<VeneerAddon> _onFinished;
        private int _fired;

        public OneShotLifecycle(Action<VeneerAddon> onFinished)
        {
            // Unlike RunningAddons' null tolerance (a threadpool watcher with no
            // observer for an exception), this null cannot be a race -- it is a
            // constructor argument supplied on the UI thread, so a null here is only
            // ever a wiring bug. Tolerating it would mean a mis-wired LaunchAddon never
            // decrements and greys the menu item forever, silently, which is the exact
            // failure this feature exists to prevent. Fail immediately instead.
            if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
            _onFinished = onFinished;
        }

        public void Finished(VeneerAddon addon)
        {
            // Same idiom as LogDispatchGuard.Install (LogDispatchGuard.cs:30-33): a
            // single Interlocked.Exchange both tests and sets the flag in one atomic
            // step, so at most one caller ever sees a previous value of 0. The
            // obvious-looking rewrite -- `if (_fired == 1) return; _fired = 1;` -- opens
            // a window between the read and the write where two threads can both pass
            // the check before either sets the flag, so both call through: an
            // undetected double-report. That exact rewrite survived
            // Parallel.For(0, 200, ...) 21 times out of 21 in testing; only a test that
            // parks many threads on one gate and releases them together, over many
            // rounds, catches it at all -- and even then only PROBABILISTICALLY, at
            // about 19 runs in 20 as measured (see FinishedIsAtomicUnderContention,
            // which documents the measurement). So a green suite is weak evidence that
            // this line is atomic; the line itself is the evidence. Do not "simplify" it.
            if (Interlocked.Exchange(ref _fired, 1) != 0) return;
            _onFinished(addon);
        }
    }
}

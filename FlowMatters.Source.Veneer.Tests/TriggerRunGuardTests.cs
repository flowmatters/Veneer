using System;
using System.Net;
using CoreWCF.Web;
using FlowMatters.Source.Veneer.ExchangeObjects;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class TriggerRunGuardTests
    {
        [TearDown]
        public void ReleaseClaim()
        {
            SourceService._currentScenarioInvoker = null;
        }

        /// <summary>
        /// A claimed invoker whose run has not started yet (IsRunning is false: RunScenario has not
        /// assigned its task) must still refuse a second POST /runs, or that request would replace
        /// the claim and the first run's finally would release the second run's.
        /// </summary>
        [Test] public void TriggerRun_RefusesWithConflict_WhileARunIsClaimedButNotYetRunning()
        {
            var claimed = new ScenarioInvoker();
            Assert.That(claimed.IsRunning, Is.False, "precondition: the claim has not started its task");
            SourceService._currentScenarioInvoker = claimed;

            var service = new SourceService();
            var fault = Assert.Throws<WebFaultException<SimulationFault>>(
                (Action)(() => service.TriggerRun(new RunParameters())));

            Assert.That(fault.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(SourceService._currentScenarioInvoker, Is.SameAs(claimed), "the refused request must leave the existing claim in place");
        }
    }
}

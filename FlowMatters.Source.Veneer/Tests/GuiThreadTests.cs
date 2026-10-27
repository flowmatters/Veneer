using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// GuiThread.Run, against a stand-in for Source's UI thread: a dedicated thread working
    /// through a queue, as a WinForms message loop does, which can be held to stand for a UI
    /// thread that is busy.
    /// </summary>
    [TestFixture]
    public class GuiThreadTests
    {
        private sealed class FakeUiThread : ISynchronizeInvoke, IDisposable
        {
            private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _running = new ManualResetEventSlim(true);

            public FakeUiThread()
            {
                _thread = new Thread(() =>
                {
                    foreach (var work in _queue.GetConsumingEnumerable())
                    {
                        _running.Wait();
                        work();
                    }
                }) { IsBackground = true };
                _thread.Start();
            }

            public int ManagedThreadId => _thread.ManagedThreadId;

            /// <summary>Stop working through the queue, as a UI thread busy elsewhere does.</summary>
            public void Hold() => _running.Reset();

            public void Release() => _running.Set();

            public bool InvokeRequired => Thread.CurrentThread != _thread;

            public IAsyncResult BeginInvoke(Delegate method, object[] args)
            {
                var done = new TaskCompletionSource<object>();
                _queue.Add(() =>
                {
                    try { done.SetResult(method.DynamicInvoke(args)); }
                    catch (Exception e) { done.SetException(e); }
                });
                return done.Task;
            }

            public object EndInvoke(IAsyncResult result) => ((Task<object>)result).GetAwaiter().GetResult();

            public object Invoke(Delegate method, object[] args) => EndInvoke(BeginInvoke(method, args));

            public void Dispose()
            {
                _running.Set();
                _queue.CompleteAdding();
            }
        }

        private FakeUiThread _ui;

        [SetUp] public void StartUiThread() => _ui = new FakeUiThread();

        [TearDown] public void StopUiThread() => _ui.Dispose();

        [Test] public void WithNoUiThreadTheActionRunsHere()
        {
            int ranOn = -1;
            GuiThread.Run((ISynchronizeInvoke)null, () => ranOn = Thread.CurrentThread.ManagedThreadId,
                TimeSpan.FromSeconds(5));
            Assert.That(ranOn, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
        }

        [Test] public void FromAnotherThreadTheActionRunsOnTheUiThread()
        {
            int ranOn = -1;
            GuiThread.Run(_ui, () => ranOn = Thread.CurrentThread.ManagedThreadId, TimeSpan.FromSeconds(5));
            Assert.That(ranOn, Is.EqualTo(_ui.ManagedThreadId));
        }

        [Test] public void ItReturnsOnlyOnceTheActionHasFinished()
        {
            bool finished = false;
            GuiThread.Run(_ui, () => { Thread.Sleep(200); finished = true; }, TimeSpan.FromSeconds(5));
            Assert.That(finished, Is.True);
        }

        [Test] public void OnTheUiThreadAlreadyTheActionRunsDirectly()
        {
            // Marshalling to the UI thread from the UI thread would wait on itself.
            int ranOn = -1;
            GuiThread.Run(_ui, () =>
                GuiThread.Run(_ui, () => ranOn = Thread.CurrentThread.ManagedThreadId, TimeSpan.FromSeconds(5)),
                TimeSpan.FromSeconds(5));
            Assert.That(ranOn, Is.EqualTo(_ui.ManagedThreadId));
        }

        [Test] public void TheActionsExceptionIsRaisedHereAsItself()
        {
            Action fails = () => throw new InvalidOperationException("no such data source");
            var thrown = Assert.Throws<InvalidOperationException>(
                (TestDelegate)(() => GuiThread.Run(_ui, fails, TimeSpan.FromSeconds(5))));
            Assert.That(thrown.Message, Is.EqualTo("no such data source"));
        }

        [Test] public void AUiThreadThatNeverGetsToTheActionTimesOutAndTheActionIsDropped()
        {
            bool ran = false;
            _ui.Hold();
            Assert.Throws<TimeoutException>(
                (TestDelegate)(() => GuiThread.Run(_ui, () => { ran = true; }, TimeSpan.FromMilliseconds(200))));

            // Once the UI thread catches up, the request has already failed: the change must not
            // then be made behind the caller's back.
            _ui.Release();
            Thread.Sleep(300);
            Assert.That(ran, Is.False);
        }

        [Test] public void AnActionStillRunningAtTheTimeoutIsWaitedFor()
        {
            bool finished = false;
            GuiThread.Run(_ui, () => { Thread.Sleep(500); finished = true; }, TimeSpan.FromMilliseconds(100));
            Assert.That(finished, Is.True);
        }

        [Test] public void AValueCanBeReturnedFromTheUiThread()
        {
            int value = GuiThread.Run(_ui, () => Thread.CurrentThread.ManagedThreadId, TimeSpan.FromSeconds(5));
            Assert.That(value, Is.EqualTo(_ui.ManagedThreadId));
        }
    }
}

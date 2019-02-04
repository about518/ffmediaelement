﻿namespace Unosquare.FFME.Primitives
{
    using System;
    using System.Threading;

    /// <summary>
    /// Provides a base implementation for a background worker that uses a thread
    /// of the given priority to execute cycles.
    /// </summary>
    /// <seealso cref="WorkerBase" />
    public abstract class ThreadWorkerBase : WorkerBase
    {
        private readonly object SyncLock = new object();
        private readonly Thread Thread;
        private readonly ManualResetEventSlim TimeoutElapsed = new ManualResetEventSlim(true);
        private CancellationTokenSource DelayCancellation = new CancellationTokenSource();

        private bool HasStarted = false;
        private int m_CycleDelay = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadWorkerBase"/> class.
        /// </summary>
        /// <param name="name">The name of the worker.</param>
        /// <param name="priority">The thread priority.</param>
        protected ThreadWorkerBase(string name, ThreadPriority priority)
            : base(name)
        {
            Thread = new Thread(RunThreadLoop)
            {
                IsBackground = true,
                Priority = priority,
                Name = name
            };
        }

        /// <summary>
        /// Gets or sets the cycle delay.
        /// </summary>
        private int CycleDelay
        {
            get { lock (SyncLock) return m_CycleDelay; }
            set { lock (SyncLock) m_CycleDelay = value; }
        }

        /// <inheritdoc />
        protected override void ScheduleCycle(int delay)
        {
            lock (SyncLock)
            {
                CycleDelay = delay;

                if (HasStarted == false)
                {
                    if (delay > 0)
                        Thread.Sleep(delay);

                    Thread.Start();
                    HasStarted = true;
                    return;
                }

                if (delay == 0)
                {
                    TimeoutElapsed.Set();
                    return;
                }

                TimeoutElapsed.Reset();
            }
        }

        /// <inheritdoc />
        protected override void InterruptDelay()
        {
            TimeoutElapsed.Set();
            DelayCancellation.Cancel();
        }

        /// <summary>
        /// Executes the wait part of the cycle. Override this method if you wish to
        /// control the delay manually or based on an event other than a timeout.
        /// </summary>
        /// <param name="wantedDelay">The wanted delay.</param>
        /// <param name="ct">The cancellation token.</param>
        protected virtual void Delay(int wantedDelay, CancellationToken ct)
        {
            // Perform the wait using an event and cancellation tokens
            if (wantedDelay != 0)
            {
                try { TimeoutElapsed.Wait(wantedDelay, ct); }
                catch (OperationCanceledException) { /* ignore token cancellation */ }
            }
        }

        /// <inheritdoc />
        protected override void DisposeManagedState()
        {
            TimeoutElapsed.Set();
            DelayCancellation.Cancel();
            try { Thread.Join(); }
            catch (ThreadStateException) { /* ignore the state */ }
            TimeoutElapsed.Dispose();
            DelayCancellation.Dispose();
        }

        /// <summary>
        /// Runs the thread loop.
        /// </summary>
        private void RunThreadLoop()
        {
            while (WorkerState != WorkerState.Stopped)
            {
                // Execute the worker cycle normally
                ExecuteWorkerCycle();

                // Execute the delay
                Delay(CycleDelay, DelayCancellation.Token);

                lock (SyncLock)
                {
                    if (DelayCancellation.IsCancellationRequested)
                        DelayCancellation = new CancellationTokenSource();
                }
            }
        }
    }
}

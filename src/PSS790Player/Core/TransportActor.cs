using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PSS790Player.Core
{
    /// <summary>
    /// Single-writer actor that serializes all MIDI transport lifecycle mutations onto one dedicated thread.
    /// Prevents concurrent locks and out-of-order Play/Pause/Stop/Seek/Load/Disconnect races.
    /// </summary>
    public sealed class TransportActor : IDisposable
    {
        private readonly struct WorkItem
        {
            public WorkItem(Action action, ManualResetEventSlim? syncSignal, TaskCompletionSource<object?>? tcs, ExceptionDispatchInfo?[]? errorSlot)
            {
                Action = action;
                SyncSignal = syncSignal;
                Tcs = tcs;
                ErrorSlot = errorSlot;
            }

            public Action Action { get; }
            public ManualResetEventSlim? SyncSignal { get; }
            public TaskCompletionSource<object?>? Tcs { get; }
            public ExceptionDispatchInfo?[]? ErrorSlot { get; }
        }

        private readonly Channel<WorkItem> _channel;
        private readonly Thread _actorThread;
        private int _actorThreadId;
        private volatile bool _isStopping;

        public bool IsCurrentThreadActor => Environment.CurrentManagedThreadId == Volatile.Read(ref _actorThreadId);

        public TransportActor(string threadName = "MidiTransportActor")
        {
            _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _actorThread = new Thread(RunLoop)
            {
                Name = threadName,
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _actorThread.Start();
        }

        private void RunLoop()
        {
            Volatile.Write(ref _actorThreadId, Environment.CurrentManagedThreadId);
            var reader = _channel.Reader;

            try
            {
                while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    while (reader.TryRead(out var item))
                    {
                        ExecuteItem(in item);
                    }
                }
            }
            catch (ChannelClosedException) { }
            catch (OperationCanceledException) { }
        }

        private static void ExecuteItem(in WorkItem item)
        {
            try
            {
                item.Action();
                item.Tcs?.TrySetResult(null);
            }
            catch (Exception ex)
            {
                if (item.ErrorSlot != null)
                {
                    item.ErrorSlot[0] = ExceptionDispatchInfo.Capture(ex);
                }
                item.Tcs?.TrySetException(ex);
            }
            finally
            {
                item.SyncSignal?.Set();
            }
        }

        /// <summary>
        /// Executes work sequentially on the actor thread (or inline if already executing on the actor thread).
        /// </summary>
        public void Invoke(Action action)
        {
            if (action == null) return;
            if (IsCurrentThreadActor)
            {
                action();
                return;
            }
            if (_isStopping) return;

            using var signal = new ManualResetEventSlim(false);
            var errorSlot = new ExceptionDispatchInfo?[1];
            if (!_channel.Writer.TryWrite(new WorkItem(action, signal, null, errorSlot)))
            {
                return;
            }

            signal.Wait();
            errorSlot[0]?.Throw();
        }

        /// <summary>
        /// Evaluates a function sequentially on the actor thread (or inline on re-entry).
        /// </summary>
        public T Invoke<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (IsCurrentThreadActor) return func();
            if (_isStopping) return default!;

            T result = default!;
            Invoke(() => result = func());
            return result;
        }

        /// <summary>
        /// Posts work asynchronously without waiting (safe from timer/device callbacks to avoid deadlocks).
        /// </summary>
        public void Post(Action action)
        {
            if (action == null || _isStopping) return;
            _channel.Writer.TryWrite(new WorkItem(action, null, null, null));
        }

        /// <summary>
        /// Enqueues work asynchronously and returns a Task that resolves once the actor completes it.
        /// </summary>
        public Task InvokeAsync(Action action)
        {
            if (action == null) return Task.CompletedTask;
            if (IsCurrentThreadActor)
            {
                try
                {
                    action();
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }
            if (_isStopping) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_channel.Writer.TryWrite(new WorkItem(action, null, tcs, null)))
            {
                return Task.CompletedTask;
            }
            return tcs.Task;
        }

        /// <summary>
        /// Runs a final cleanup delegate (if queued before channel close), completes the channel, and joins the actor thread.
        /// </summary>
        public void Shutdown(Action? finalCleanup = null, int joinTimeoutMs = 4000)
        {
            if (_isStopping) return;

            if (finalCleanup != null && !IsCurrentThreadActor)
            {
                using var signal = new ManualResetEventSlim(false);
                var errorSlot = new ExceptionDispatchInfo?[1];
                if (_channel.Writer.TryWrite(new WorkItem(finalCleanup, signal, null, errorSlot)))
                {
                    _isStopping = true;
                    _channel.Writer.TryComplete();
                    signal.Wait(joinTimeoutMs);
                }
                else
                {
                    _isStopping = true;
                    _channel.Writer.TryComplete();
                }
            }
            else
            {
                _isStopping = true;
                if (IsCurrentThreadActor) finalCleanup?.Invoke();
                _channel.Writer.TryComplete();
            }

            if (!IsCurrentThreadActor && _actorThread.IsAlive)
            {
                _actorThread.Join(joinTimeoutMs);
            }
        }

        public void Dispose() => Shutdown();
    }
}

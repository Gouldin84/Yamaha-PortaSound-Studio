using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PSS790Player.Core
{
    public class MidiDevicesChangedEventArgs : EventArgs
    {
        public List<string> OutputDevices { get; set; } = new();
        public List<string> InputDevices { get; set; } = new();
        public List<string> AddedOutputs { get; set; } = new();
        public List<string> RemovedOutputs { get; set; } = new();
        public List<string> AddedInputs { get; set; } = new();
        public List<string> RemovedInputs { get; set; } = new();

        public bool HasChanges =>
            AddedOutputs.Count > 0 ||
            RemovedOutputs.Count > 0 ||
            AddedInputs.Count > 0 ||
            RemovedInputs.Count > 0;
    }

    /// <summary>
    /// Monitors Windows Multimedia MIDI subsystem for real-time device additions and removals (hot-swapping).
    /// Combines OS-level PnP triggers (WM_DEVICECHANGE) with a background polling fallback.
    /// </summary>
    public class MidiDeviceMonitor : IDisposable
    {
        private readonly Func<List<string>> _getOutputDevices;
        private readonly Func<List<string>> _getInputDevices;
        private readonly System.Timers.Timer _pollingTimer;
        private readonly object _lock = new();

        private List<string> _lastOutputs = new();
        private List<string> _lastInputs = new();
        private CancellationTokenSource? _debounceCts;
        private volatile bool _isDisposed;

        public event EventHandler<MidiDevicesChangedEventArgs>? DevicesChanged;

        public IReadOnlyList<string> CurrentOutputDevices
        {
            get { lock (_lock) return _lastOutputs.ToList(); }
        }

        public IReadOnlyList<string> CurrentInputDevices
        {
            get { lock (_lock) return _lastInputs.ToList(); }
        }

        public MidiDeviceMonitor(
            Func<List<string>>? getOutputDevices = null,
            Func<List<string>>? getInputDevices = null,
            double pollingIntervalMs = 1500)
        {
            _getOutputDevices = getOutputDevices ?? MidiPlayer.GetOutputDeviceNames;
            _getInputDevices = getInputDevices ?? MidiRecorder.GetInputDeviceNames;

            try
            {
                _lastOutputs = _getOutputDevices() ?? new List<string>();
                _lastInputs = _getInputDevices() ?? new List<string>();
            }
            catch
            {
                _lastOutputs = new List<string>();
                _lastInputs = new List<string>();
            }

            _pollingTimer = new System.Timers.Timer(pollingIntervalMs)
            {
                AutoReset = true,
                Enabled = true
            };
            _pollingTimer.Elapsed += (s, e) =>
            {
                try
                {
                    if (!_isDisposed)
                    {
                        CheckForChanges();
                    }
                }
                catch { }
            };
        }

        /// <summary>
        /// Explicitly triggers a rescan, optionally after a debounce interval (e.g. for WM_DEVICECHANGE where Windows takes a few ms to finish registering driver endpoints).
        /// </summary>
        public void TriggerRescan(int delayMs = 300)
        {
            if (_isDisposed) return;

            CancellationToken token;
            lock (_lock)
            {
                if (_isDisposed) return;

                try { _debounceCts?.Cancel(); } catch { }
                try { _debounceCts?.Dispose(); } catch { }

                _debounceCts = new CancellationTokenSource();
                token = _debounceCts.Token;
            }

            if (delayMs <= 0)
            {
                CheckForChanges();
                return;
            }

            Task.Delay(delayMs, token).ContinueWith(t =>
            {
                try
                {
                    if (!t.IsCanceled && !_isDisposed)
                    {
                        CheckForChanges();
                    }
                }
                catch { }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Queries current devices, compares against cached snapshot, and fires DevicesChanged if any device was added or removed.
        /// </summary>
        public MidiDevicesChangedEventArgs? CheckForChanges()
        {
            if (_isDisposed) return null;

            MidiDevicesChangedEventArgs? argsToRaise = null;

            lock (_lock)
            {
                if (_isDisposed) return null;

                List<string> currentOuts;
                List<string> currentIns;

                try
                {
                    currentOuts = _getOutputDevices() ?? new List<string>();
                }
                catch
                {
                    currentOuts = new List<string>();
                }

                try
                {
                    currentIns = _getInputDevices() ?? new List<string>();
                }
                catch
                {
                    currentIns = new List<string>();
                }

                var addedOutputs = currentOuts.Except(_lastOutputs, StringComparer.OrdinalIgnoreCase).ToList();
                var removedOutputs = _lastOutputs.Except(currentOuts, StringComparer.OrdinalIgnoreCase).ToList();
                var addedInputs = currentIns.Except(_lastInputs, StringComparer.OrdinalIgnoreCase).ToList();
                var removedInputs = _lastInputs.Except(currentIns, StringComparer.OrdinalIgnoreCase).ToList();

                var hasChanges = addedOutputs.Count > 0 || removedOutputs.Count > 0 ||
                                 addedInputs.Count > 0 || removedInputs.Count > 0;

                _lastOutputs = currentOuts.ToList();
                _lastInputs = currentIns.ToList();

                if (hasChanges)
                {
                    argsToRaise = new MidiDevicesChangedEventArgs
                    {
                        OutputDevices = currentOuts,
                        InputDevices = currentIns,
                        AddedOutputs = addedOutputs,
                        RemovedOutputs = removedOutputs,
                        AddedInputs = addedInputs,
                        RemovedInputs = removedInputs
                    };
                }
            }

            if (argsToRaise != null && !_isDisposed)
            {
                Task.Run(() =>
                {
                    if (_isDisposed) return;
                    try
                    {
                        DevicesChanged?.Invoke(this, argsToRaise);
                    }
                    catch { }
                });
            }

            return argsToRaise;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                try
                {
                    _pollingTimer.Stop();
                    _pollingTimer.Dispose();
                }
                catch { }

                try { _debounceCts?.Cancel(); } catch { }
                try { _debounceCts?.Dispose(); } catch { }
                _debounceCts = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}

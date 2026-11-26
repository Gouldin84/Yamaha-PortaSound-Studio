using System;
using System.Collections.Generic;
using System.Linq;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    /// <summary>
    /// Owns hardware MIDI output/input device lifecycle, hot-swap PnP monitoring (WM_DEVICECHANGE),
    /// preferred device matching, and active Yamaha model detection (PSS-790 / PSS-680).
    /// </summary>
    public sealed class HardwareDeviceService : IDisposable
    {
        private readonly AppStateStore _state;
        private readonly MidiPlayer _player;
        private readonly MidiRecorder _recorder;
        private readonly MidiDeviceMonitor _deviceMonitor;
        private volatile bool _isDisposed;

        public event Action<HardwareModel>? HardwareModelAutoDetected;
        public event Action<string?>? OutputDeviceChanged;

        public MidiPlayer Player => _player;
        public MidiRecorder Recorder => _recorder;

        public HardwareDeviceService(AppStateStore state)
        {
            _state = state;
            _player = new MidiPlayer { ActiveModel = _state.ActiveModel };
            _recorder = new MidiRecorder { ThruTargetPlayer = _player };

            var initialOuts = MidiPlayer.GetOutputDeviceNames();
            var initialIns = MidiRecorder.GetInputDeviceNames();
            _state.OutputDevices = initialOuts;
            _state.InputDevices = initialIns;

            string? preferredOut = PickPreferredOutputDevice(initialOuts);
            if (!string.IsNullOrEmpty(preferredOut))
            {
                _player.SetOutputDevice(preferredOut);
                _state.SelectedOutputDevice = _player.CurrentOutputDeviceName;
                AutoSwitchModelFromDeviceName(_state.SelectedOutputDevice, notify: false);
            }

            string? preferredIn = PickPreferredInputDevice(initialIns);
            if (!string.IsNullOrEmpty(preferredIn))
            {
                _recorder.SetInputDevice(preferredIn);
                _state.SelectedInputDevice = _recorder.CurrentInputDeviceName;
            }

            _deviceMonitor = new MidiDeviceMonitor(
                MidiPlayer.GetOutputDeviceNames,
                MidiRecorder.GetInputDeviceNames);
            _deviceMonitor.DevicesChanged += OnDevicesChanged;
        }

        public void TriggerRescan(int delayMs = 300)
        {
            if (_isDisposed) return;
            _deviceMonitor.TriggerRescan(delayMs);
            if (delayMs <= 0)
            {
                RefreshDeviceListsToStore();
            }
        }

        public void SelectOutputDevice(string? deviceName)
        {
            if (_isDisposed) return;
            _player.SetOutputDevice(deviceName);
            string? resolved = _player.CurrentOutputDeviceName;
            bool modelSwitched = AutoSwitchModelFromDeviceName(resolved, notify: true);

            _state.Mutate(s =>
            {
                s.OutputDevices = MidiPlayer.GetOutputDeviceNames();
                s.SelectedOutputDevice = resolved;
            });

            if (modelSwitched)
            {
                HardwareModelAutoDetected?.Invoke(HardwareModelExtensions.ParseModel(_state.ActiveModel));
            }
            OutputDeviceChanged?.Invoke(resolved);
        }

        public void SelectInputDevice(string? deviceName)
        {
            if (_isDisposed) return;
            _recorder.SetInputDevice(deviceName);
            _state.Mutate(s =>
            {
                s.InputDevices = MidiRecorder.GetInputDeviceNames();
                s.SelectedInputDevice = _recorder.CurrentInputDeviceName;
            });
        }

        public void RefreshDeviceListsToStore()
        {
            if (_isDisposed) return;
            _state.Mutate(s =>
            {
                s.OutputDevices = MidiPlayer.GetOutputDeviceNames();
                s.InputDevices = MidiRecorder.GetInputDeviceNames();
                s.SelectedOutputDevice = _player.CurrentOutputDeviceName;
                s.SelectedInputDevice = _recorder.CurrentInputDeviceName;
            });
        }

        private void OnDevicesChanged(object? sender, MidiDevicesChangedEventArgs e)
        {
            if (_isDisposed) return;

            var currentOut = _player.CurrentOutputDeviceName;
            var currentIn = _recorder.CurrentInputDeviceName;

            if (!string.IsNullOrEmpty(currentOut) &&
                e.RemovedOutputs.Contains(currentOut, StringComparer.OrdinalIgnoreCase))
            {
                _player.HandleDeviceDisconnected();
                _state.AddToast($"MIDI output device disconnected: {currentOut}", ToastSeverity.Warning);
            }

            if (string.IsNullOrEmpty(_player.CurrentOutputDeviceName) && e.AddedOutputs.Count > 0)
            {
                string? newPreferred = PickPreferredHardwareOutput(e.AddedOutputs) ?? e.AddedOutputs.FirstOrDefault();
                if (!string.IsNullOrEmpty(newPreferred))
                {
                    _player.SetOutputDevice(newPreferred);
                    _state.AddToast($"Connected MIDI output: {newPreferred}", ToastSeverity.Success);
                    AutoSwitchModelFromDeviceName(_player.CurrentOutputDeviceName, notify: true);
                    OutputDeviceChanged?.Invoke(_player.CurrentOutputDeviceName);
                }
            }

            if (!string.IsNullOrEmpty(currentIn) &&
                e.RemovedInputs.Contains(currentIn, StringComparer.OrdinalIgnoreCase))
            {
                _recorder.HandleDeviceDisconnected();
                _state.AddToast($"MIDI input device disconnected: {currentIn}", ToastSeverity.Warning);
            }

            if (string.IsNullOrEmpty(_recorder.CurrentInputDeviceName) && e.AddedInputs.Count > 0)
            {
                string? newIn = PickPreferredInputDevice(e.AddedInputs) ?? e.AddedInputs.FirstOrDefault();
                if (!string.IsNullOrEmpty(newIn))
                {
                    _recorder.SetInputDevice(newIn);
                    _state.AddToast($"Connected MIDI input: {newIn}", ToastSeverity.Success);
                }
            }

            _state.Mutate(s =>
            {
                s.OutputDevices = e.OutputDevices;
                s.InputDevices = e.InputDevices;
                s.SelectedOutputDevice = _player.CurrentOutputDeviceName;
                s.SelectedInputDevice = _recorder.CurrentInputDeviceName;
            });
        }

        private bool AutoSwitchModelFromDeviceName(string? deviceName, bool notify)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;

            string? targetModel = null;
            if (deviceName.Contains("PSS-790", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Contains("PSS790", StringComparison.OrdinalIgnoreCase))
            {
                targetModel = "pss790";
            }
            else if (deviceName.Contains("PSS-680", StringComparison.OrdinalIgnoreCase) ||
                     deviceName.Contains("PSS680", StringComparison.OrdinalIgnoreCase) ||
                     deviceName.Contains("PSS-480", StringComparison.OrdinalIgnoreCase) ||
                     deviceName.Contains("PSS-780", StringComparison.OrdinalIgnoreCase))
            {
                targetModel = "pss680";
            }

            if (targetModel != null && !string.Equals(_state.ActiveModel, targetModel, StringComparison.OrdinalIgnoreCase))
            {
                _state.ActiveModel = targetModel;
                _player.ActiveModel = targetModel;
                if (notify)
                {
                    string label = targetModel == "pss680" ? "Yamaha PSS-680" : "Yamaha PSS-790";
                    _state.AddToast($"Hardware detected: switched profile to {label}.", ToastSeverity.Info);
                }
                return true;
            }
            return false;
        }

        private static string? PickPreferredOutputDevice(List<string> devices)
        {
            if (devices.Count == 0) return null;
            return PickPreferredHardwareOutput(devices)
                ?? devices.FirstOrDefault(d => d.Contains("Microsoft GS", StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault();
        }

        private static string? PickPreferredHardwareOutput(List<string> devices) =>
            devices.FirstOrDefault(d => d.Contains("PSS", StringComparison.OrdinalIgnoreCase) ||
                                        d.Contains("Yamaha", StringComparison.OrdinalIgnoreCase) ||
                                        d.Contains("USB", StringComparison.OrdinalIgnoreCase));

        private static string? PickPreferredInputDevice(List<string> devices) =>
            devices.FirstOrDefault(d => d.Contains("PSS", StringComparison.OrdinalIgnoreCase) ||
                                        d.Contains("Yamaha", StringComparison.OrdinalIgnoreCase) ||
                                        d.Contains("USB", StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault();

        public void PrepareForShutdown()
        {
            try { _recorder.MidiThruEnabled = false; _recorder.StopRecording(); } catch { }
            try { _player.Stop(); } catch { }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            PrepareForShutdown();
            _isDisposed = true;
            _deviceMonitor.DevicesChanged -= OnDevicesChanged;
            try { _deviceMonitor.Dispose(); } catch { }
            try { _recorder.Dispose(); } catch { }
            try { _player.Dispose(); } catch { }
        }
    }
}

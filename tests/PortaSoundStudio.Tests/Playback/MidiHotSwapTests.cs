using System;
using System.Collections.Generic;
using System.Threading;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class MidiHotSwapTests
    {
        [Fact]
        public void DeviceMonitor_DetectsAddedAndRemovedOutputDevices()
        {
            var mockOutputs = new List<string> { "Yamaha PSS-790", "Virtual Port 1" };
            var mockInputs = new List<string> { "Yamaha PSS-790 IN" };

            using var monitor = new MidiDeviceMonitor(
                getOutputDevices: () => new List<string>(mockOutputs),
                getInputDevices: () => new List<string>(mockInputs),
                pollingIntervalMs: 10000); // long interval so we control check manually

            Assert.Equal(2, monitor.CurrentOutputDevices.Count);
            Assert.Contains("Yamaha PSS-790", monitor.CurrentOutputDevices);

            // Simulate plugging in a new USB interface
            mockOutputs.Add("Roland UM-ONE");

            var changeResult = monitor.CheckForChanges();

            Assert.NotNull(changeResult);
            Assert.True(changeResult.HasChanges);
            Assert.Single(changeResult.AddedOutputs);
            Assert.Equal("Roland UM-ONE", changeResult.AddedOutputs[0]);
            Assert.Empty(changeResult.RemovedOutputs);

            // Simulate unplugging "Virtual Port 1"
            mockOutputs.Remove("Virtual Port 1");

            var unplugResult = monitor.CheckForChanges();

            Assert.NotNull(unplugResult);
            Assert.True(unplugResult.HasChanges);
            Assert.Single(unplugResult.RemovedOutputs);
            Assert.Equal("Virtual Port 1", unplugResult.RemovedOutputs[0]);
            Assert.Empty(unplugResult.AddedOutputs);
        }

        [Fact]
        public void DeviceMonitor_DetectsAddedAndRemovedInputDevices()
        {
            var mockOutputs = new List<string>();
            var mockInputs = new List<string> { "USB MIDI IN" };

            using var monitor = new MidiDeviceMonitor(
                getOutputDevices: () => new List<string>(mockOutputs),
                getInputDevices: () => new List<string>(mockInputs),
                pollingIntervalMs: 10000);

            Assert.Single(monitor.CurrentInputDevices);

            // Add input device
            mockInputs.Add("KeyLab 49 IN");
            var res = monitor.CheckForChanges();

            Assert.NotNull(res);
            Assert.Single(res.AddedInputs);
            Assert.Equal("KeyLab 49 IN", res.AddedInputs[0]);

            // Remove device
            mockInputs.Remove("USB MIDI IN");
            var res2 = monitor.CheckForChanges();

            Assert.NotNull(res2);
            Assert.Single(res2.RemovedInputs);
            Assert.Equal("USB MIDI IN", res2.RemovedInputs[0]);
        }

        [Fact]
        public void DeviceMonitor_TriggerRescan_FiresDevicesChangedEvent()
        {
            var mockOutputs = new List<string> { "Port A" };
            var mockInputs = new List<string>();

            using var monitor = new MidiDeviceMonitor(
                getOutputDevices: () => new List<string>(mockOutputs),
                getInputDevices: () => new List<string>(mockInputs),
                pollingIntervalMs: 10000);

            var eventFired = new ManualResetEventSlim(false);
            MidiDevicesChangedEventArgs? receivedArgs = null;

            monitor.DevicesChanged += (s, e) =>
            {
                receivedArgs = e;
                eventFired.Set();
            };

            // Hot-plug new device
            mockOutputs.Add("Port B");
            monitor.TriggerRescan(delayMs: 50);

            bool signaled = eventFired.Wait(TimeSpan.FromSeconds(2));

            Assert.True(signaled, "DevicesChanged event should have fired within timeout.");
            Assert.NotNull(receivedArgs);
            Assert.Contains("Port B", receivedArgs.AddedOutputs);
        }

        [Fact]
        public void MidiPlayer_HandleDeviceDisconnected_CleansUpStateGracefully()
        {
            using var player = new MidiPlayer();
            player.SetOutputDevice("NonExistentPort"); // Will fail safely without throwing

            string? disconnectedName = null;
            player.OutputDeviceDisconnected += (s, name) =>
            {
                disconnectedName = name;
            };

            // Simulate hardware unplug mid-session
            player.HandleDeviceDisconnected();

            Assert.Null(player.CurrentOutputDeviceName);
            Assert.False(player.IsPlaying);
        }

        [Fact]
        public void MidiPlayer_SetOutputDevice_NullOrEmptyClearsDeviceWithoutCrashing()
        {
            using var player = new MidiPlayer();
            player.SetOutputDevice((string?)null);
            Assert.Null(player.CurrentOutputDeviceName);

            player.SetOutputDevice("");
            Assert.Null(player.CurrentOutputDeviceName);
        }
    }
}

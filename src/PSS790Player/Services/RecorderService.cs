using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Core;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    /// <summary>
    /// Manages MIDI input recording, count-in, MIDI Thru, post-take editing (trim, quantize),
    /// real-time chord analysis, and export into the GM / PSS libraries.
    /// </summary>
    public sealed class RecorderService : IDisposable
    {
        private readonly AppStateStore _state;
        private readonly HardwareDeviceService _devices;
        private readonly PlaybackCoordinator _playback;
        private readonly LibraryService _library;
        private volatile bool _isDisposed;

        public RecorderService(
            AppStateStore state,
            HardwareDeviceService devices,
            PlaybackCoordinator playback,
            LibraryService library)
        {
            _state = state;
            _devices = devices;
            _playback = playback;
            _library = library;

            var rec = _devices.Recorder;
            rec.InputActivity += OnRecorderInputActivity;
            rec.StateChanged += OnRecorderStateChanged;
            rec.Tick += OnRecorderTick;
            rec.InputDeviceDisconnected += OnInputDeviceDisconnected;
        }

        public void Initialize()
        {
            RefreshRecordingsLibrary();
        }

        public async Task StartRecordingAsync()
        {
            if (_isDisposed) return;
            try
            {
                await _devices.Recorder.StartRecordingAsync(_state.RecorderBpm, _state.RecorderCountIn);
            }
            catch (Exception ex)
            {
                _state.AddToast($"Failed to start recording: {ex.Message}", ToastSeverity.Error);
            }
        }

        public async Task StopRecordingAsync()
        {
            if (_isDisposed) return;
            var modelSnap = _state.ActiveModel;
            bool autoTrim = _state.RecorderAutoTrim;

            await Task.Run(() =>
            {
                var rec = _devices.Recorder;
                rec.StopRecording();
                int trimmedTicks = autoTrim ? rec.TrimCurrentTakeSilence() : 0;
                if (rec.LastRecordedFile != null)
                {
                    MidiRecorder.ApplyHardwareTrackLabels(rec.LastRecordedFile, modelSnap);
                }

                var stats = rec.GetLastTakeStats();
                var autoSavedPath = rec.AutoSaveTake(modelSnap);
                var dto = new RecorderTakeReadyDto
                {
                    NoteCount = stats.noteCount,
                    EventCount = stats.eventCount,
                    DurationSeconds = stats.durationSeconds,
                    DurationFormatted = TimeSpan.FromSeconds(stats.durationSeconds).ToString(@"mm\:ss"),
                    TrimmedTicks = trimmedTicks,
                    AutoSavedPath = autoSavedPath
                };

                var recordings = rec.GetRecordingsLibrary();
                _state.Mutate(s =>
                {
                    s.LastTakeStats = dto;
                    s.RecordingsLibrary = recordings;
                });

                if (stats.noteCount > 0 && !string.IsNullOrEmpty(autoSavedPath))
                {
                    _state.AddToast($"Saved take ({stats.noteCount} notes): {Path.GetFileName(autoSavedPath)}", ToastSeverity.Success);
                }
            });
        }

        public void TrimCurrentTake()
        {
            var rec = _devices.Recorder;
            var before = rec.GetLastTakeStats();
            int trimmed = rec.TrimCurrentTakeSilence();
            var after = rec.GetLastTakeStats();

            _state.Mutate(s => s.LastTakeStats = new RecorderTakeReadyDto
            {
                NoteCount = after.noteCount,
                EventCount = after.eventCount,
                DurationSeconds = after.durationSeconds,
                DurationFormatted = TimeSpan.FromSeconds(after.durationSeconds).ToString(@"mm\:ss"),
                TrimmedTicks = trimmed
            });

            _state.AddToast(trimmed > 0
                ? $"Trimmed leading silence ({before.durationSeconds:F1}s -> {after.durationSeconds:F1}s)."
                : "Take already starts cleanly at Beat 1.", ToastSeverity.Info);
        }

        public void QuantizeCurrentTake(int grid = 16)
        {
            int clampedGrid = Math.Clamp(grid, 4, 32);
            var rec = _devices.Recorder;
            int snapped = rec.QuantizeCurrentTake(clampedGrid);
            var stats = rec.GetLastTakeStats();

            _state.Mutate(s => s.LastTakeStats = new RecorderTakeReadyDto
            {
                NoteCount = stats.noteCount,
                EventCount = stats.eventCount,
                DurationSeconds = stats.durationSeconds,
                DurationFormatted = TimeSpan.FromSeconds(stats.durationSeconds).ToString(@"mm\:ss"),
                QuantizedGrid = clampedGrid
            });

            _state.AddToast($"Quantized {snapped} notes cleanly to 1/{clampedGrid} grid.", ToastSeverity.Success);
        }

        public void PreviewCurrentTake()
        {
            var file = _devices.Recorder.LastRecordedFile;
            if (file == null)
            {
                _state.AddToast("No recorded take available to preview.", ToastSeverity.Warning);
                return;
            }
            _devices.Player.LoadMidi(file);
            _devices.Player.Play();
        }

        public void StopPreview()
        {
            _devices.Player.Stop();
            _state.Mutate(s => s.PreviewingRecordingPath = null);
        }

        public void RefreshRecordingsLibrary()
        {
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var recordings = _devices.Recorder.GetRecordingsLibrary();
                _state.Mutate(s => s.RecordingsLibrary = recordings);
            });
        }

        public void PreviewRecording(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            _ = Task.Run(() =>
            {
                try
                {
                    var prevMidi = MidiFile.Read(filePath, Constants.DefaultReadingSettings);
                    _devices.Player.LoadMidi(prevMidi);
                    _devices.Player.Play();
                    _state.Mutate(s => s.PreviewingRecordingPath = filePath);
                }
                catch (Exception ex)
                {
                    _state.AddToast($"Failed to preview recording: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void LoadRecordingInPlayer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.RecordingsDir))
            {
                return;
            }

            _state.Mutate(s => s.ActiveTab = StudioTab.Player);
            _ = _playback.LoadFileAsync(filePath, autoPlay: false);
        }

        public void ExportRecordingToGm(string filePath, string? customName = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            _ = Task.Run(() =>
            {
                try
                {
                    var exported = _devices.Recorder.ExportRecordingToGeneralMidi(filePath, customName);
                    _library.RefreshLibrary();
                    _state.AddToast($"Exported to General MIDI library: {Path.GetFileName(exported)}", ToastSeverity.Success);
                }
                catch (Exception ex)
                {
                    _state.AddToast($"Failed to export to GM: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void ExportRecordingToPss(string filePath, string? customName = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var targetModel = _state.ActiveModel;
            _ = Task.Run(() =>
            {
                try
                {
                    var label = targetModel == "pss680" ? "PSS-680" : "PSS-790";
                    var exported = _devices.Recorder.ExportRecordingToPss(filePath, customName, targetModel);
                    _library.RefreshLibrary();
                    _state.AddToast($"Exported to {label} library: {Path.GetFileName(exported)}", ToastSeverity.Success);
                }
                catch (Exception ex)
                {
                    _state.AddToast($"Failed to export recording: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void DeleteRecording(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !AppPaths.IsPathInsideDirectory(filePath, AppPaths.RecordingsDir)) return;
            _devices.Recorder.DeleteRecording(filePath, AppPaths.RecordingsDir);
            RefreshRecordingsLibrary();
            _state.AddToast($"Deleted recording take: {Path.GetFileName(filePath)}", ToastSeverity.Info);
        }

        public void RenameRecording(string filePath, string newName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(newName) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.RecordingsDir))
            {
                return;
            }

            try
            {
                var renamed = _devices.Recorder.RenameRecording(filePath, newName, AppPaths.RecordingsDir);
                RefreshRecordingsLibrary();
                _state.AddToast($"Renamed recording to: {Path.GetFileName(renamed)}", ToastSeverity.Success);
            }
            catch (Exception ex)
            {
                _state.AddToast($"Failed to rename recording: {ex.Message}", ToastSeverity.Error);
            }
        }

        public void SaveTake(string? fileName, bool isNativePss)
        {
            var resolvedName = string.IsNullOrWhiteSpace(fileName)
                ? $"Take_{DateTime.Now:yyyyMMdd_HHmmss}.mid"
                : fileName.Trim();
            var model = _state.ActiveModel;

            _ = Task.Run(() =>
            {
                try
                {
                    var savedPath = isNativePss
                        ? _devices.Recorder.SaveNativeTake(resolvedName, model)
                        : _devices.Recorder.ExportTakeToGeneralMidi(resolvedName, model);
                    _library.RefreshLibrary();
                    RefreshRecordingsLibrary();
                    _state.AddToast($"Saved take: {Path.GetFileName(savedPath)}", ToastSeverity.Success);
                }
                catch (Exception ex)
                {
                    _state.AddToast($"Failed to save take: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void SendTakeToPlayer(string? title = null)
        {
            var takeFile = _devices.Recorder.LastRecordedFile;
            if (takeFile == null) return;
            var cloned = takeFile.Clone();
            MidiRecorder.EmbedRecordingMarker(cloned, _state.ActiveModel);
            _playback.LoadMidiObject(cloned, string.IsNullOrWhiteSpace(title) ? "Recorded Take" : title);
            _state.Mutate(s => s.ActiveTab = StudioTab.Player);
        }

        public void SetMidiThru(bool enabled)
        {
            _devices.Recorder.MidiThruEnabled = enabled;
            _state.Mutate(s => s.RecorderMidiThru = enabled);
        }

        public void SetBpm(int bpm) => _state.Mutate(s => s.RecorderBpm = Math.Clamp(bpm, 40, 240));
        public void SetCountIn(bool enabled) => _state.Mutate(s => s.RecorderCountIn = enabled);
        public void SetMetronomeEnabled(bool enabled) => _state.Mutate(s => s.RecorderMetronomeEnabled = enabled);
        public void SetAutoTrim(bool enabled) => _state.Mutate(s => s.RecorderAutoTrim = enabled);
        public void OpenRecordingsFolder()
        {
            AppPaths.EnsureDirectoriesExist();
            AppPaths.OpenFolderInExplorer(AppPaths.RecordingsDir);
        }

        public (string ChordName, string NotesText, string Subtitle) ComputeInboundChordDisplay()
        {
            List<int> notes;
            lock (_state.ActiveInboundNotes)
            {
                notes = _state.ActiveInboundNotes.Keys.ToList();
            }

            if (notes.Count == 0)
                return ("STANDBY", "--", "Active Notes");

            string[] pitchNames = notes.Select(FormatPitch).ToArray();
            string joined = string.Join(" ", pitchNames);

            if (notes.Count == 1)
                return (pitchNames[0], joined, $"MIDI Note #{notes[0]}");

            if (notes.Count == 2)
            {
                int semi = (notes[1] - notes[0]) % 12;
                return ($"{pitchNames[0]} - {pitchNames[1]}", joined, GetIntervalName(semi));
            }

            var pitchClasses = notes.Select(n => n % 12).Distinct().OrderBy(x => x).ToList();
            int bassPc = notes[0] % 12;

            foreach (int root in pitchClasses)
            {
                string rootName = NoteNames[root];
                int[] intervals = pitchClasses.Select(p => (p - root + 12) % 12).OrderBy(x => x).ToArray();
                foreach (var (name, pattern) in ChordDefinitions)
                {
                    if (pattern.Length == intervals.Length && pattern.SequenceEqual(intervals))
                    {
                        string chord = bassPc != root ? $"{rootName} {name} / {NoteNames[bassPc]}" : $"{rootName} {name}";
                        return (chord, joined, $"Formula: {name}");
                    }
                }
            }

            return (string.Join(" ", pitchNames.Take(4)), joined, "Compound Chord");
        }

        private void OnRecorderInputActivity(object? sender, ChannelActivityEventArgs e)
        {
            if (_isDisposed) return;
            _state.Mutate(s =>
            {
                if (e.Channel is >= 0 and < 16)
                {
                    s.RecorderChannelActive[e.Channel] = e.IsActive && e.Velocity > 0;
                    s.RecorderChannelVelocity[e.Channel] = e.Velocity;
                    if (e.NoteNumber > 0)
                    {
                        s.RecorderChannelNote[e.Channel] = e.NoteNumber;
                    }
                }

                if (e.NoteNumber > 0)
                {
                    lock (s.ActiveInboundNotes)
                    {
                        if (e.IsActive && e.Velocity > 0)
                            s.ActiveInboundNotes[e.NoteNumber] = (e.Channel, e.Velocity);
                        else
                            s.ActiveInboundNotes.Remove(e.NoteNumber);
                    }
                }
            });
        }

        private void OnRecorderStateChanged(object? sender, RecorderStateEventArgs e)
        {
            if (_isDisposed) return;
            _state.Mutate(s =>
            {
                s.RecorderState = e.State;
                s.RecorderCountInBeat = e.CountInBeat;
                if (e.Bpm > 0) s.RecorderBpm = e.Bpm;
                if (e.State == RecorderState.Idle || e.State == RecorderState.Stopped)
                {
                    Array.Clear(s.RecorderChannelActive);
                    lock (s.ActiveInboundNotes)
                    {
                        s.ActiveInboundNotes.Clear();
                    }
                }
            });
        }

        private void OnRecorderTick(object? sender, RecorderTickEventArgs e)
        {
            if (_isDisposed) return;
            _state.Mutate(s =>
            {
                s.RecorderElapsedSeconds = e.ElapsedSeconds;
                s.RecorderEventCount = e.EventCount;
                s.RecorderNoteCount = e.NoteCount;
                s.RecorderBar = e.CurrentBar;
                s.RecorderBeat = e.CurrentBeat;
            });
        }

        private void OnInputDeviceDisconnected(object? sender, string deviceName)
        {
            _state.Mutate(s =>
            {
                s.RecorderState = RecorderState.Idle;
                s.SelectedInputDevice = null;
            });
        }

        private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        private static string FormatPitch(int note) => $"{NoteNames[note % 12]}{(note / 12) - 1}";

        private static string GetIntervalName(int semitones) => semitones switch
        {
            0 => "Unison / Octave",
            1 => "Minor 2nd",
            2 => "Major 2nd",
            3 => "Minor 3rd",
            4 => "Major 3rd",
            5 => "Perfect 4th",
            6 => "Tritone",
            7 => "Perfect 5th",
            8 => "Minor 6th",
            9 => "Major 6th",
            10 => "Minor 7th",
            11 => "Major 7th",
            _ => "Interval"
        };

        private static readonly (string Name, int[] Intervals)[] ChordDefinitions =
        {
            ("Maj", new[] { 0, 4, 7 }),
            ("Min", new[] { 0, 3, 7 }),
            ("7", new[] { 0, 4, 7, 10 }),
            ("Maj7", new[] { 0, 4, 7, 11 }),
            ("Min7", new[] { 0, 3, 7, 10 }),
            ("Dim", new[] { 0, 3, 6 }),
            ("Dim7", new[] { 0, 3, 6, 9 }),
            ("m7b5", new[] { 0, 3, 6, 10 }),
            ("Aug", new[] { 0, 4, 8 }),
            ("Sus4", new[] { 0, 5, 7 }),
            ("Sus2", new[] { 0, 2, 7 }),
            ("6", new[] { 0, 4, 7, 9 }),
            ("Min6", new[] { 0, 3, 7, 9 }),
            ("Add9", new[] { 0, 2, 4, 7 }),
            ("9", new[] { 0, 2, 4, 7, 10 }),
            ("Maj9", new[] { 0, 2, 4, 7, 11 }),
            ("Min9", new[] { 0, 2, 3, 7, 10 })
        };

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            var rec = _devices.Recorder;
            rec.InputActivity -= OnRecorderInputActivity;
            rec.StateChanged -= OnRecorderStateChanged;
            rec.Tick -= OnRecorderTick;
            rec.InputDeviceDisconnected -= OnInputDeviceDisconnected;
        }
    }
}

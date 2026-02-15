using SimHub.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace LaunchPlugin
{
    internal sealed class ShiftAssistAudio
    {
        private const string DefaultFileRelativePath = "ShiftAssist/DefaultBeep.wav";

        private readonly Func<LaunchPluginSettings> _settingsProvider;
        private readonly object _playerSync = new object();
        private string _resolvedDefaultPath;
        private bool _warnedMissingCustom;
        private bool _loggedSoundChoice;
        private string _lastLoggedPath;
        private bool _lastLoggedCustom;
        private bool _embeddedMissingLogged;
        private bool _embeddedUnavailable;
        private bool _embeddedResourceNameResolved;
        private string _embeddedResourceName;
        private MediaPlayer _player;
        private string _playerPath;
        private EventHandler _playerMediaEndedHandler;
        private EventHandler<ExceptionEventArgs> _playerMediaFailedHandler;

        public ShiftAssistAudio(Func<LaunchPluginSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        public bool EnsureDefaultBeepExtracted()
        {
            if (_embeddedUnavailable)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_resolvedDefaultPath) && File.Exists(_resolvedDefaultPath))
            {
                return true;
            }

            try
            {
                string root = PluginStorage.GetCommonFolder();
                string targetPath = Path.Combine(root, "LalaPlugin", DefaultFileRelativePath);
                string folder = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                if (!File.Exists(targetPath))
                {
                    string resourceName = ResolveEmbeddedResourceName();
                    if (string.IsNullOrWhiteSpace(resourceName))
                    {
                        return false;
                    }

                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            MarkEmbeddedUnavailable("[LalaPlugin:ShiftAssist] Embedded default beep resource stream missing.");
                            return false;
                        }

                        using (var output = File.Create(targetPath))
                        {
                            stream.CopyTo(output);
                        }
                    }
                }

                _resolvedDefaultPath = targetPath;
                return true;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:ShiftAssist] Failed to extract embedded beep: {ex.Message}");
                return false;
            }
        }

        private string ResolveEmbeddedResourceName()
        {
            if (_embeddedUnavailable)
            {
                return null;
            }

            if (_embeddedResourceNameResolved)
            {
                return _embeddedResourceName;
            }

            _embeddedResourceNameResolved = true;
            var assembly = Assembly.GetExecutingAssembly();
            _embeddedResourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("ShiftAssist_DefaultBeep.wav", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(_embeddedResourceName))
            {
                MarkEmbeddedUnavailable("[LalaPlugin:ShiftAssist] Embedded default beep resource missing.");
                return null;
            }

            return _embeddedResourceName;
        }

        private void MarkEmbeddedUnavailable(string message)
        {
            _embeddedUnavailable = true;
            if (_embeddedMissingLogged)
            {
                return;
            }

            _embeddedMissingLogged = true;
            SimHub.Logging.Current.Error(message);
        }

        public void ResetInvalidCustomWarning()
        {
            _warnedMissingCustom = false;
            _loggedSoundChoice = false;
            _lastLoggedPath = null;
        }

        public void PlayShiftBeep()
        {
            var settings = _settingsProvider?.Invoke();
            if (settings != null)
            {
                if (!settings.ShiftAssistBeepSoundEnabled || settings.ShiftAssistBeepVolumePct <= 0)
                {
                    HardStop();
                    return;
                }
            }

            string path = ResolvePlaybackPath(out bool usingCustom);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string absolutePath = ToAbsolutePath(path);
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return;
            }

            MaybeLogSoundChoice(absolutePath, usingCustom);

            int volumePct = settings?.ShiftAssistBeepVolumePct ?? 100;
            if (volumePct < 0) volumePct = 0;
            if (volumePct > 100) volumePct = 100;
            double volume = volumePct / 100.0;

            PlayOnce(new Uri(absolutePath, UriKind.Absolute), volume);
        }

        private void PlayOnce(Uri uri, double volume)
        {
            lock (_playerSync)
            {
                bool warnedPlayFailure = false;

                Action<string, Exception> warnOnce = (message, ex) =>
                {
                    if (warnedPlayFailure)
                    {
                        return;
                    }

                    warnedPlayFailure = true;
                    string suffix = ex == null ? string.Empty : $": {ex.Message}";
                    SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] {message}{suffix}");
                };

                Action cleanup = null;
                bool cleanupDone = false;

                cleanup = () =>
                {
                    if (cleanupDone)
                    {
                        return;
                    }

                    cleanupDone = true;

                    try
                    {
                        DetachPlayerHandlersUnsafe();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (_player != null)
                        {
                            _player.Stop();
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (_player != null)
                        {
                            _player.Close();
                        }
                    }
                    catch
                    {
                    }

                    _player = null;
                    _playerPath = null;
                };

                try
                {
                    HardStopUnsafe();

                    _player = new MediaPlayer();
                    _playerPath = uri.LocalPath;

                    _playerMediaEndedHandler = (sender, args) =>
                    {
                        cleanup();
                    };

                    _playerMediaFailedHandler = (sender, args) =>
                    {
                        warnOnce($"Failed to play sound '{_playerPath}'", args != null ? args.ErrorException : null);
                        cleanup();
                    };

                    _player.MediaEnded += _playerMediaEndedHandler;
                    _player.MediaFailed += _playerMediaFailedHandler;

                    _player.Volume = volume;
                    _player.Open(uri);
                    _player.Play();
                }
                catch (Exception ex)
                {
                    warnOnce($"Failed to play sound '{uri}'", ex);
                    cleanup();
                }
            }
        }

        public void HardStop()
        {
            lock (_playerSync)
            {
                try
                {
                    HardStopUnsafe();
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] HardStop failed: {ex.Message}");
                }
            }
        }

        private void HardStopUnsafe()
        {
            DetachPlayerHandlersUnsafe();

            try
            {
                if (_player != null)
                {
                    _player.Stop();
                }
            }
            catch
            {
            }

            try
            {
                if (_player != null)
                {
                    _player.Close();
                }
            }
            catch
            {
            }

            _player = null;
            _playerPath = null;
        }

        private void DetachPlayerHandlersUnsafe()
        {
            if (_player == null)
            {
                _playerMediaEndedHandler = null;
                _playerMediaFailedHandler = null;
                return;
            }

            if (_playerMediaEndedHandler != null)
            {
                _player.MediaEnded -= _playerMediaEndedHandler;
                _playerMediaEndedHandler = null;
            }

            if (_playerMediaFailedHandler != null)
            {
                _player.MediaFailed -= _playerMediaFailedHandler;
                _playerMediaFailedHandler = null;
            }
        }

        private string ToAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string candidate = path;
            if (!Path.IsPathRooted(candidate))
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                candidate = Path.Combine(baseDirectory, candidate);
            }

            return Path.GetFullPath(candidate);
        }

        private string ResolvePlaybackPath(out bool usingCustom)
        {
            usingCustom = false;

            var settings = _settingsProvider?.Invoke();
            if (settings != null && settings.ShiftAssistUseCustomWav)
            {
                string customPath = settings.ShiftAssistCustomWavPath;
                string customAbsolutePath = ToAbsolutePath(customPath);
                if (!string.IsNullOrWhiteSpace(customAbsolutePath) && File.Exists(customAbsolutePath))
                {
                    usingCustom = true;
                    return customAbsolutePath;
                }

                if (!_warnedMissingCustom)
                {
                    _warnedMissingCustom = true;
                    SimHub.Logging.Current.Info("[LalaPlugin:ShiftAssist] WARNING custom wav missing/invalid, falling back to embedded default");
                }
            }

            if (!EnsureDefaultBeepExtracted())
            {
                return null;
            }

            return _resolvedDefaultPath;
        }

        private void MaybeLogSoundChoice(string path, bool usingCustom)
        {
            if (_loggedSoundChoice && _lastLoggedCustom == usingCustom && string.Equals(_lastLoggedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _loggedSoundChoice = true;
            _lastLoggedCustom = usingCustom;
            _lastLoggedPath = path;
            if (usingCustom)
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Sound=Custom path='{path}'");
            }
            else
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Sound=EmbeddedDefault path='{path}'");
            }
        }

        public void Dispose()
        {
            HardStop();
        }
    }
}

using SimHub.Plugins;
using System;
using System.IO;
using System.Media;
using System.Reflection;

namespace LaunchPlugin
{
    internal sealed class ShiftAssistAudio
    {
        private const string EmbeddedResourceName = "LaunchPlugin.Resources.ShiftAssist_DefaultBeep.wav";
        private const string DefaultFileRelativePath = "ShiftAssist/DefaultBeep.wav";

        private readonly Func<LaunchPluginSettings> _settingsProvider;
        private string _resolvedDefaultPath;
        private bool _warnedMissingCustom;
        private bool _loggedSoundChoice;
        private string _lastLoggedPath;
        private bool _lastLoggedCustom;
        private SoundPlayer _player;
        private string _playerPath;

        public ShiftAssistAudio(Func<LaunchPluginSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        public bool EnsureDefaultBeepExtracted()
        {
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
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                    {
                        if (stream == null)
                        {
                            SimHub.Logging.Current.Error("[LalaPlugin:ShiftAssist] Embedded default beep resource missing.");
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

        public void ResetInvalidCustomWarning()
        {
            _warnedMissingCustom = false;
            _loggedSoundChoice = false;
            _lastLoggedPath = null;
        }

        public void PlayShiftBeep()
        {
            string path = ResolvePlaybackPath(out bool usingCustom);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                if (!string.Equals(_playerPath, path, StringComparison.OrdinalIgnoreCase) || _player == null)
                {
                    _player?.Dispose();
                    _player = new SoundPlayer(path);
                    _player.Load();
                    _playerPath = path;
                }

                _player.Play();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] Failed to play sound '{path}': {ex.Message}");
            }

            MaybeLogSoundChoice(path, usingCustom);
        }

        private string ResolvePlaybackPath(out bool usingCustom)
        {
            usingCustom = false;

            var settings = _settingsProvider?.Invoke();
            if (settings != null && settings.ShiftAssistUseCustomWav)
            {
                string customPath = settings.ShiftAssistCustomWavPath;
                if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    usingCustom = true;
                    return customPath;
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
            _player?.Dispose();
            _player = null;
            _playerPath = null;
        }
    }
}

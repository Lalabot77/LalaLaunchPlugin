using SimHub.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

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
        private SoundPlayer _player;
        private string _playerPath;
        private int _lastVolumePct;
        private string _lastSourcePathUsedForScaling;
        private DateTime _lastSourceWriteUtc;
        private string _lastScaledPath;
        private bool _warnedUnsupportedScaling;
        private bool _warnedScaledFallback;

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

        public bool TryPlayBeep(out DateTime issuedUtc)
        {
            issuedUtc = DateTime.MinValue;

            var settings = _settingsProvider?.Invoke();
            if (settings != null)
            {
                if (!settings.ShiftAssistBeepSoundEnabled || settings.ShiftAssistBeepVolumePct <= 0)
                {
                    HardStop();
                    return false;
                }
            }

            string sourcePath = ResolvePlaybackPath(out bool usingCustom);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            string absoluteSourcePath = ToAbsolutePath(sourcePath);
            if (string.IsNullOrWhiteSpace(absoluteSourcePath) || !File.Exists(absoluteSourcePath))
            {
                return false;
            }

            int volumePct = Math.Max(0, Math.Min(100, settings?.ShiftAssistBeepVolumePct ?? 100));
            string playbackPath = volumePct >= 100
                ? absoluteSourcePath
                : EnsureScaledWav(absoluteSourcePath, volumePct);

            MaybeLogSoundChoice(absoluteSourcePath, usingCustom);

            if (!TryPlayPath(playbackPath, out issuedUtc, out string playbackError))
            {
                if (!string.Equals(playbackPath, absoluteSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_warnedScaledFallback)
                    {
                        _warnedScaledFallback = true;
                        SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] WARNING scaled beep failed; falling back to original. error='{playbackError}'");
                    }

                    return TryPlayPath(absoluteSourcePath, out issuedUtc, out _);
                }

                SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] Failed to play sound '{playbackPath}': {playbackError}");
                return false;
            }

            return issuedUtc != DateTime.MinValue;
        }

        public bool TryPlayBeepWithVolumeOverride(int volumePctOverride, out DateTime issuedUtc)
        {
            issuedUtc = DateTime.MinValue;

            var settings = _settingsProvider?.Invoke();
            if (settings != null && !settings.ShiftAssistBeepSoundEnabled)
            {
                HardStop();
                return false;
            }

            int volumePct = Math.Max(0, Math.Min(100, volumePctOverride));
            if (volumePct <= 0)
            {
                HardStop();
                return false;
            }

            string sourcePath = ResolvePlaybackPath(out bool usingCustom);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            string absoluteSourcePath = ToAbsolutePath(sourcePath);
            if (string.IsNullOrWhiteSpace(absoluteSourcePath) || !File.Exists(absoluteSourcePath))
            {
                return false;
            }

            string playbackPath = volumePct >= 100
                ? absoluteSourcePath
                : EnsureScaledWav(absoluteSourcePath, volumePct);

            MaybeLogSoundChoice(absoluteSourcePath, usingCustom);

            if (!TryPlayPath(playbackPath, out issuedUtc, out string playbackError))
            {
                if (!string.Equals(playbackPath, absoluteSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_warnedScaledFallback)
                    {
                        _warnedScaledFallback = true;
                        SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] WARNING scaled beep failed; falling back to original. error='{playbackError}'");
                    }

                    return TryPlayPath(absoluteSourcePath, out issuedUtc, out _);
                }

                SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] Failed to play sound '{playbackPath}': {playbackError}");
                return false;
            }

            return issuedUtc != DateTime.MinValue;
        }

        private bool TryPlayPath(string path, out DateTime issuedUtc, out string error)
        {
            issuedUtc = DateTime.MinValue;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "empty playback path";
                return false;
            }

            if (!File.Exists(path))
            {
                error = "playback file missing";
                return false;
            }

            try
            {
                lock (_playerSync)
                {
                    if (_player == null || !string.Equals(_playerPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        _player = new SoundPlayer(path);
                        _player.Load();
                        _playerPath = path;
                    }

                    issuedUtc = DateTime.UtcNow;
                    _player.Play();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void HardStop()
        {
            lock (_playerSync)
            {
                try
                {
                    if (_player != null)
                    {
                        _player.Stop();
                    }
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] HardStop failed: {ex.Message}");
                }
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

        private string EnsureScaledWav(string sourcePath, int volumePct)
        {
            if (volumePct >= 100)
            {
                return sourcePath;
            }

            try
            {
                var sourceInfo = new FileInfo(sourcePath);
                DateTime writeUtc = sourceInfo.LastWriteTimeUtc;
                if (string.Equals(_lastSourcePathUsedForScaling, sourcePath, StringComparison.OrdinalIgnoreCase)
                    && _lastVolumePct == volumePct
                    && _lastSourceWriteUtc == writeUtc
                    && !string.IsNullOrWhiteSpace(_lastScaledPath)
                    && File.Exists(_lastScaledPath))
                {
                    return _lastScaledPath;
                }

                string scaledRoot = Path.Combine(PluginStorage.GetCommonFolder(), "LalaPlugin", "ShiftAssist", "Scaled");
                Directory.CreateDirectory(scaledRoot);
                string cacheKey = $"{sourcePath}|{sourceInfo.Length}|{writeUtc.Ticks}|{volumePct}";
                string hash = ComputeSha256(cacheKey);
                string finalPath = Path.Combine(scaledRoot, hash + ".wav");
                if (File.Exists(finalPath))
                {
                    _lastVolumePct = volumePct;
                    _lastSourcePathUsedForScaling = sourcePath;
                    _lastSourceWriteUtc = writeUtc;
                    _lastScaledPath = finalPath;
                    return finalPath;
                }

                string tempPath = finalPath + ".tmp";
                try
                {
                    ScalePcm16Wav(sourcePath, tempPath, volumePct / 100f);
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
                    }

                    File.Move(tempPath, finalPath);
                }
                finally
                {
                    TryDeleteFile(tempPath);
                }

                _lastVolumePct = volumePct;
                _lastSourcePathUsedForScaling = sourcePath;
                _lastSourceWriteUtc = writeUtc;
                _lastScaledPath = finalPath;
                return finalPath;
            }
            catch (NotSupportedException)
            {
                if (!_warnedUnsupportedScaling)
                {
                    _warnedUnsupportedScaling = true;
                    SimHub.Logging.Current.Warn("[LalaPlugin:ShiftAssist] WARNING beep volume scaling unsupported for this WAV format. Playing original at SimHub volume.");
                }

                return sourcePath;
            }
            catch (Exception ex)
            {
                if (!_warnedScaledFallback)
                {
                    _warnedScaledFallback = true;
                    SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] WARNING scaled beep failed; falling back to original. error='{ex.Message}'");
                }

                return sourcePath;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // best effort cleanup only
            }
        }

        private static string ComputeSha256(string input)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static void ScalePcm16Wav(string sourcePath, string destinationPath, float gain)
        {
            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(input))
            {
                if (input.Length < 12)
                {
                    throw new NotSupportedException("Invalid WAV header");
                }

                string riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
                reader.ReadUInt32();
                string wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (!string.Equals(riff, "RIFF", StringComparison.Ordinal) || !string.Equals(wave, "WAVE", StringComparison.Ordinal))
                {
                    throw new NotSupportedException("Not a RIFF/WAVE file");
                }

                long dataOffset = -1;
                int dataSize = 0;
                short audioFormat = 0;
                short bitsPerSample = 0;

                while (input.Position + 8 <= input.Length)
                {
                    string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    int chunkSize = reader.ReadInt32();
                    long chunkDataStart = input.Position;
                    if (chunkSize < 0 || chunkDataStart + chunkSize > input.Length)
                    {
                        throw new NotSupportedException("Invalid WAV chunk");
                    }

                    if (string.Equals(chunkId, "fmt ", StringComparison.Ordinal))
                    {
                        if (chunkSize < 16)
                        {
                            throw new NotSupportedException("Invalid fmt chunk");
                        }

                        audioFormat = reader.ReadInt16();
                        reader.ReadInt16(); // channels
                        reader.ReadInt32(); // sampleRate
                        reader.ReadInt32(); // byteRate
                        reader.ReadInt16(); // blockAlign
                        bitsPerSample = reader.ReadInt16();
                    }
                    else if (string.Equals(chunkId, "data", StringComparison.Ordinal))
                    {
                        dataOffset = chunkDataStart;
                        dataSize = chunkSize;
                    }

                    input.Position = chunkDataStart + chunkSize + (chunkSize % 2);
                }

                if (audioFormat != 1 || bitsPerSample != 16 || dataOffset < 0 || dataSize <= 0 || (dataSize % 2) != 0)
                {
                    throw new NotSupportedException("Unsupported WAV format");
                }

                byte[] fileBytes = File.ReadAllBytes(sourcePath);
                int start = checked((int)dataOffset);
                int end = start + dataSize;
                for (int i = start; i < end; i += 2)
                {
                    short sample = (short)(fileBytes[i] | (fileBytes[i + 1] << 8));
                    int scaled = (int)Math.Round(sample * gain);
                    if (scaled > short.MaxValue)
                    {
                        scaled = short.MaxValue;
                    }
                    else if (scaled < short.MinValue)
                    {
                        scaled = short.MinValue;
                    }

                    short outputSample = (short)scaled;
                    fileBytes[i] = (byte)(outputSample & 0xFF);
                    fileBytes[i + 1] = (byte)((outputSample >> 8) & 0xFF);
                }

                File.WriteAllBytes(destinationPath, fileBytes);
            }
        }
    }
}

using BluesAimTrain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BluesShared
{
    // Matches BluesBar.Systems.Profile schema (same property names).
    public class Profile
    {
        public string PlayerName { get; set; } = "b1uepack";

        public long Coins { get; set; } = 0;
        public long LifetimeEarned { get; set; } = 0;
        public long LifetimeSpent { get; set; } = 0;

        public HashSet<string> UnlockedCursors { get; set; } = new();
        public HashSet<string> UnlockedBackgrounds { get; set; } = new();
        public HashSet<string> UnlockedSfx { get; set; } = new();

        public string EquippedCursor { get; set; } = "Default";
        public string EquippedBackground { get; set; } = "Default";
        public string EquippedSfx { get; set; } = "Default";

        public long NetWorth => LifetimeEarned;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<ChallengeRunEntry> ChallengeHistory { get; set; } = new();
        public int SchemaVersion { get; set; } = 2;

    }

    public sealed class ProfileSync : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly JsonSerializerOptions _jsonOptions;
        private FileSystemWatcher? _watcher;

        private readonly object _debounceLock = new();
        private Timer? _debounceTimer;

        public string DataDir { get; }
        public string ProfilePath { get; }

        // Fires whenever profile coins may have changed (from either app).
        public event Action<long>? CoinsChanged;

        public ProfileSync(
            string dataDir,
            string profilePath,
            string mutexName = @"Global\BluesBar_ProfileLock")
        {
            DataDir = dataDir;
            ProfilePath = profilePath;
            _mutex = new Mutex(false, mutexName);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        public static ProfileSync CreateDefault(string? profileDirOverride = null)
        {
            // Default matches BluesBar
            var dir = profileDirOverride;
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BluesBar");
            }

            var path = Path.Combine(dir, "profile.json");
            return new ProfileSync(dir, path);
        }


        public Profile LoadOrCreateLocked()
        {
            Directory.CreateDirectory(DataDir);

            return WithLock(() =>
            {
                if (!File.Exists(ProfilePath))
                {
                    var p = new Profile();
                    SaveInternalLocked(p);
                    return p;
                }

                try
                {
                    var json = File.ReadAllText(ProfilePath);
                    var loaded = JsonSerializer.Deserialize<Profile>(json, _jsonOptions);
                    return loaded ?? new Profile();
                }
                catch
                {
                    // If temporarily mid-write or corrupted, fall back safely.
                    return new Profile();
                }
            });
        }

        public long ReadCoinsLocked()
        {
            return WithLock(() =>
            {
                if (!File.Exists(ProfilePath)) return 0;
                try
                {
                    var json = File.ReadAllText(ProfilePath);
                    var p = JsonSerializer.Deserialize<Profile>(json, _jsonOptions);
                    return p?.Coins ?? 0;
                }
                catch { return 0; }
            });
        }

        // AimTrain should call this (ONLY adds).
        public long EarnLocked(long amount, string reason = "Earn")
        {
            if (amount <= 0) return ReadCoinsLocked();

            long newCoins = 0;

            WithLock(() =>
            {
                var p = LoadOrCreateLocked_NoLock();
                p.Coins += amount;
                p.LifetimeEarned += amount;
                p.UpdatedUtc = DateTime.UtcNow;

                SaveInternalLocked_NoLock(p);

                newCoins = p.Coins;
            });

            CoinsChanged?.Invoke(newCoins);
            return newCoins;
        }

        // BluesBar should call this for gambling/store spending.
        public bool SpendLocked(long amount, out long newCoins, string reason = "Spend")
        {
            long tempNewCoins = ReadCoinsLocked();
            newCoins = tempNewCoins;

            if (amount <= 0) return true;

            bool ok = false;

            WithLock(() =>
            {
                var p = LoadOrCreateLocked_NoLock();

                if (p.Coins < amount)
                {
                    ok = false;
                    tempNewCoins = p.Coins;
                    return;
                }

                p.Coins -= amount;
                p.LifetimeSpent += amount;
                p.UpdatedUtc = DateTime.UtcNow;

                SaveInternalLocked_NoLock(p);

                ok = true;
                tempNewCoins = p.Coins;
            });

            newCoins = tempNewCoins;

            if (ok) CoinsChanged?.Invoke(newCoins);
            return ok;
        }

        // Optional: use for admin/debug hooks
        public void SetCoinsLocked(long coins)
        {
            if (coins < 0) coins = 0;

            WithLock(() =>
            {
                var p = LoadOrCreateLocked_NoLock();
                p.Coins = coins;
                p.UpdatedUtc = DateTime.UtcNow;
                SaveInternalLocked_NoLock(p);
            });

            CoinsChanged?.Invoke(coins);
        }

        public void StartWatching()
        {
            StopWatching();

            Directory.CreateDirectory(DataDir);

            _watcher = new FileSystemWatcher(DataDir)
            {
                Filter = Path.GetFileName(ProfilePath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            _watcher.Changed += OnProfileFileEvent;
            _watcher.Created += OnProfileFileEvent;
            _watcher.Renamed += OnProfileFileEvent;
            _watcher.EnableRaisingEvents = true;
        }

        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnProfileFileEvent;
                _watcher.Created -= OnProfileFileEvent;
                _watcher.Renamed -= OnProfileFileEvent;
                _watcher.Dispose();
                _watcher = null;
            }

            lock (_debounceLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }

        private void OnProfileFileEvent(object sender, FileSystemEventArgs e)
        {
            lock (_debounceLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ =>
                {
                    var coins = ReadCoinsLocked();
                    CoinsChanged?.Invoke(coins);
                }, null, 40, Timeout.Infinite);
            }
        }

        // ----- internal save/load helpers -----

        private Profile LoadOrCreateLocked_NoLock()
        {
            // assumes mutex already held
            Directory.CreateDirectory(DataDir);

            if (!File.Exists(ProfilePath))
            {
                var p = new Profile();
                SaveInternalLocked_NoLock(p);
                return p;
            }

            try
            {
                var json = File.ReadAllText(ProfilePath);
                return JsonSerializer.Deserialize<Profile>(json, _jsonOptions) ?? new Profile();
            }
            catch
            {
                return new Profile();
            }
        }

        private void SaveInternalLocked(Profile p)
        {
            WithLock(() => SaveInternalLocked_NoLock(p));
        }

        private void SaveInternalLocked_NoLock(Profile p)
        {
            // assumes mutex already held
            Directory.CreateDirectory(DataDir);

            p.UpdatedUtc = DateTime.UtcNow;

            var tmp = ProfilePath + ".tmp";
            var backup = ProfilePath + ".bak";

            var json = JsonSerializer.Serialize(p, _jsonOptions);

            File.WriteAllText(tmp, json);

            if (File.Exists(ProfilePath))
            {
                File.Copy(ProfilePath, backup, true);
                File.Replace(tmp, ProfilePath, null);
            }
            else
            {
                File.Move(tmp, ProfilePath);
            }
        }
        private void WithLock(Action action)
        {
            bool taken = false;
            try
            {
                taken = _mutex.WaitOne(TimeSpan.FromSeconds(2));
                if (!taken) throw new TimeoutException("Failed to acquire profile mutex.");
                action();
            }
            finally
            {
                if (taken)
                {
                    try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
                }
            }
        }

        private T WithLock<T>(Func<T> func)
        {
            bool taken = false;
            try
            {
                taken = _mutex.WaitOne(TimeSpan.FromSeconds(2));
                if (!taken) throw new TimeoutException("Failed to acquire profile mutex.");
                return func();
            }
            finally
            {
                if (taken)
                {
                    try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
                }
            }
        }

        public void Dispose()
        {
            StopWatching();
            _debounceTimer?.Dispose();
            _mutex.Dispose();
        }
    }
}


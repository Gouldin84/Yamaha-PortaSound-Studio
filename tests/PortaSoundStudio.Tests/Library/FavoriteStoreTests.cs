using System;
using System.Collections.Generic;
using System.IO;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class FavoriteStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _favFile;
        private readonly string _gmDir;
        private readonly string _pss790Dir;
        private readonly string _pss680Dir;

        public FavoriteStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PSS790_FavTests_" + Guid.NewGuid().ToString("N"));
            _favFile = Path.Combine(_tempDir, "favourites.json");
            _gmDir = Path.Combine(_tempDir, "GM Midi");
            _pss790Dir = Path.Combine(_tempDir, "PSS-790 Midi");
            _pss680Dir = Path.Combine(_tempDir, "PSS-680 Midi");

            Directory.CreateDirectory(_tempDir);
            Directory.CreateDirectory(_gmDir);
            Directory.CreateDirectory(_pss790Dir);
            Directory.CreateDirectory(_pss680Dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void ToggleFavorite_AddsAndRemovesCleanly()
        {
            var store = new FavoriteStore(_favFile);
            string songPath = Path.Combine(_gmDir, "Rock", "Jump.mid");

            Assert.False(store.IsFavorite(songPath));

            bool added = store.ToggleFavorite(songPath);
            Assert.True(added);
            Assert.True(store.IsFavorite(songPath));

            bool removed = store.ToggleFavorite(songPath);
            Assert.False(removed);
            Assert.False(store.IsFavorite(songPath));
        }

        [Fact]
        public void FavoriteStore_SyncsAcrossLibraries()
        {
            var store = new FavoriteStore(_favFile);
            string gmSong = Path.Combine(_gmDir, "Synthwave", "Laser.mid");
            string pss790Song = Path.Combine(_pss790Dir, "Synthwave", "Laser.mid");
            string pss680Song = Path.Combine(_pss680Dir, "Synthwave", "Laser.mid");

            // Favorite from GM Midi
            store.AddFavorite(gmSong);

            // Should be recognized across PSS-790 and PSS-680 libraries
            Assert.True(store.IsFavorite(gmSong));
            Assert.True(store.IsFavorite(pss790Song));
            Assert.True(store.IsFavorite(pss680Song));

            // Remove from PSS-680 library
            store.RemoveFavorite(pss680Song);

            // Should be removed across all 3 libraries
            Assert.False(store.IsFavorite(gmSong));
            Assert.False(store.IsFavorite(pss790Song));
            Assert.False(store.IsFavorite(pss680Song));
        }

        [Fact]
        public void FavoriteStore_CaseInsensitiveMatching()
        {
            var store = new FavoriteStore(_favFile);
            string lowerPath = Path.Combine(_gmDir, "pop", "beat.mid");
            string upperPath = Path.Combine(_pss790Dir, "POP", "BEAT.MID");

            store.AddFavorite(lowerPath);
            Assert.True(store.IsFavorite(upperPath));
        }

        [Fact]
        public void FavoriteStore_PersistsToDiskAndReloads()
        {
            var store1 = new FavoriteStore(_favFile);
            store1.AddFavorite("Soundtracks/Theme.mid");
            store1.AddFavorite("Jazz/TakeFive.mid");

            Assert.True(File.Exists(_favFile));

            // Load new instance from same file
            var store2 = new FavoriteStore(_favFile);
            var favorites = store2.GetAllFavorites();

            Assert.Equal(2, favorites.Count);
            Assert.Contains("Jazz/TakeFive.mid", favorites);
            Assert.Contains("Soundtracks/Theme.mid", favorites);
            Assert.True(store2.IsFavorite("Soundtracks/Theme.mid"));
        }

        [Fact]
        public void FavoriteStore_PruneMissing_RemovesStaleFavorites()
        {
            var store = new FavoriteStore(_favFile);
            store.AddFavorite("Rock/Alive.mid");
            store.AddFavorite("Rock/Deleted.mid");
            store.AddFavorite("Ambient/Space.mid");

            var existingOnDisk = new List<string>
            {
                Path.Combine(_gmDir, "Rock", "Alive.mid"),
                Path.Combine(_pss790Dir, "Ambient", "Space.mid")
            };

            int pruned = store.PruneMissing(existingOnDisk);
            Assert.Equal(1, pruned);

            var remaining = store.GetAllFavorites();
            Assert.Equal(2, remaining.Count);
            Assert.True(store.IsFavorite("Rock/Alive.mid"));
            Assert.True(store.IsFavorite("Ambient/Space.mid"));
            Assert.False(store.IsFavorite("Rock/Deleted.mid"));
        }
    }
}

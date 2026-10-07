using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;
using ClaudeModelPicker.Services;

namespace ClaudeModelPicker.Tests
{
    public class ModeStoreTests
    {
        private static string TempFile() =>
            Path.Combine(Path.GetTempPath(), "cmp-mode-" + Guid.NewGuid().ToString("N"), "mode.txt");

        [Theory]
        [InlineData("passive", PickerMode.Passive)]
        [InlineData(" PASSIVE\r\n", PickerMode.Passive)]
        [InlineData("active", PickerMode.Active)]
        [InlineData("off", PickerMode.Active)]      // unknown = the normal mode
        [InlineData("", PickerMode.Active)]
        [InlineData(null, PickerMode.Active)]
        public void Parse(string? text, PickerMode expected) => Assert.Equal(expected, ModeStore.Parse(text));

        [Fact]
        public void MissingFile_IsActive()
        {
            using var store = new ModeStore(TempFile(), watch: false);
            Assert.Equal(PickerMode.Active, store.Mode);
        }

        [Fact]
        public void Set_WritesFile_AndRaisesChanged()
        {
            var path = TempFile();
            using var store = new ModeStore(path, watch: false);
            PickerMode? seen = null;
            store.Changed += m => seen = m;

            store.Set(PickerMode.Passive);

            Assert.Equal(PickerMode.Passive, store.Mode);
            Assert.Equal(PickerMode.Passive, seen);
            Assert.Equal("passive", File.ReadAllText(path));
            Assert.Equal(PickerMode.Passive, ModeStore.Read(path));
        }

        [Fact]
        public void FileWrittenByAnotherApp_IsPickedUp()
        {
            // ClaudeUsageMonitor writes mode.txt from its window while CMP runs.
            var path = TempFile();
            using var store = new ModeStore(path);
            File.WriteAllText(path, "passive");

            var timer = Stopwatch.StartNew();
            while (store.Mode != PickerMode.Passive && timer.ElapsedMilliseconds < 3000)
                Thread.Sleep(20);

            Assert.Equal(PickerMode.Passive, store.Mode);
        }
    }
}

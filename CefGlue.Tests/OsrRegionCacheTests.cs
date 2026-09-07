using System;
using NUnit.Framework;
using Xilium.CefGlue.BrowserProcess.FrameDelivery;
using Xilium.CefGlue.Common.Shared;

namespace CefGlue.Tests
{
    /// <summary>
    /// The render side's per-browser region bookkeeping. Like <see cref="SharedRegionTests"/> this
    /// deliberately does not inherit TestBase, so no browser starts: the cache is plain lifetime
    /// rules over real <see cref="SharedRegion"/>s, and both halves run in one process.
    /// </summary>
    public class OsrRegionCacheTests
    {
        private static string UniqueName() => "CG_TEST_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        [Test]
        public void Resolve_ReturnsTheSameMappingForTheSameName()
        {
            var name = UniqueName();
            using var writer = SharedRegion.Create(name, 4096);
            var cache = new OsrRegionCache();

            var first = cache.Resolve(browserId: 1, name, 4096);
            var second = cache.Resolve(browserId: 1, name, 4096);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first), "the mapping must live across frames, not be re-opened per frame");
        }

        [Test]
        public void Release_DisposesAndForgetsTheRegion()
        {
            var name = UniqueName();
            using var writer = SharedRegion.Create(name, 4096);
            var cache = new OsrRegionCache();
            cache.Resolve(browserId: 1, name, 4096);

            Assert.That(cache.Release(1), Is.True);
            Assert.That(cache.Release(1), Is.False, "the second release has nothing left to free");
        }

        [Test]
        public void Release_ForABrowserThatNeverPainted_IsANoOp()
        {
            var cache = new OsrRegionCache();

            Assert.That(cache.Release(99), Is.False);
        }

        [Test]
        public void Resolve_WithANameThatCannotBeOpened_ForgetsTheCachedRegion()
        {
            var name = UniqueName();
            using var writer = SharedRegion.Create(name, 4096);
            var cache = new OsrRegionCache();
            cache.Resolve(browserId: 1, name, 4096);

            Assert.That(cache.Resolve(browserId: 1, UniqueName(), 4096), Is.Null);
            Assert.That(cache.Release(1), Is.False, "a name that opens nothing must leave the cache empty");
        }
    }
}
